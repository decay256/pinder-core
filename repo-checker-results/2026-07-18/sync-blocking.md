### Finding 1: Admin prompt save blocks the FastAPI event loop with YAML file I/O
**File**: `pinder-web/src/pinder-backend/routes/admin.py:1130`
**Issue**: `async def admin_save_prompt(...)` performs synchronous disk and YAML work before its first offload: `with resolved.open("r", encoding="utf-8") as fh: data = yaml_rt.load(fh)` at lines 1167-1168 and `with resolved.open("w", encoding="utf-8") as fh: yaml_rt.dump(data, fh)` at lines 1199-1200. Only the later git commit is sent through `await to_thread.run_sync(...)`.
**Impact**: A staging admin prompt save can monopolize the FastAPI event-loop thread while reading, parsing, serializing, and writing prompt YAML. Under slow storage or large prompt files, unrelated async requests on that worker can stall behind an admin edit.
**Urgency**: U1 - topic default; this is an async HTTP route doing blocking filesystem and CPU-bound YAML work directly on the event loop.
**Fixer-Agent Action Plan**: Move the full read/parse/mutate/write unit into a synchronous helper and call it with `await to_thread.run_sync(...)`, mirroring `admin_content_write._apply_prompts_update_edit`. Add a regression test that monkeypatches `anyio.to_thread.run_sync` and proves the prompt-save YAML mutation helper is dispatched through it before `commit_and_push_to_main`.

### Finding 2: Unity visual asset async routes synchronously stat, read, and hash assets
**File**: `pinder-web/src/pinder-backend/routes/visual_assets.py:122`
**Issue**: The async handlers call `_get_service()` inline: `return _manifest_response(_get_service().manifest_result(), ...)` at line 122 and `_get_service().asset_result(...)` at lines 138-141. `_get_service()` calls `_manifest_signature(root)` at lines 83 and 90, which performs `manifest_path.stat()` at line 71; on cache miss it calls `UnityVisualAssetService.from_configured_root(root)` at line 89, which reaches `manifest_path.read_bytes()` in `pinder-web/src/pinder-backend/visual_assets.py:220` and hashes every declared asset through `_sha256_file(disk_path)` at line 277.
**Impact**: The first request after startup, a manifest change, or any cache miss can block the event loop while reading the manifest and hashing up to the configured asset limits. Even cache hits still do synchronous filesystem `stat` calls per request, so a slow asset mount can stall unrelated FastAPI traffic.
**Urgency**: U1 - topic default; async public HTTP routes invoke blocking filesystem and hashing work directly on the event loop.
**Fixer-Agent Action Plan**: Offload service refresh/signature work with `to_thread.run_sync`, or make these endpoints synchronous `def` handlers if the whole path is intentionally blocking. Preserve `FileResponse` for payload serving, and add tests that spy on the offload path for both manifest and asset requests when the service is cold and when the manifest signature changes.

### Finding 3: Session runner blocks inside async setup while loading characters
**File**: `pinder-core/session-runner/Program.CharacterLoader.cs:49`
**Issue**: `LoadCharacter(...)` synchronously blocks on an async character store call: `CharacterDefinition? def = store.LoadAsync(id).GetAwaiter().GetResult();`. The caller is the async setup path, `internal static async Task<GameSetupResult> SetupSessionAsync(...)` in `pinder-core/session-runner/Program.Setup.cs:24`, which invokes `LoadCharacter(...)` at lines 125-126.
**Impact**: The session-runner's async startup path can tie up its async continuation thread while `DirectoryCharacterStore.LoadAsync` performs asynchronous file I/O. Today this is CLI tooling, but it increases deadlock/hang risk if the setup path is reused under a synchronization context or embedded in another async host.
**Urgency**: U2 - de-escalated one level from U1 because the current caller is CLI tooling, not a production request handler.
**Fixer-Agent Action Plan**: Convert `LoadCharacter` to `LoadCharacterAsync`, await `store.LoadAsync(id, ct)`, and update `SetupSessionAsync` to await both player/datee loads. Add/adjust session-runner setup tests to exercise slug loading through the async path without any `.GetAwaiter().GetResult()`.

### Finding 4: Narrative harness exposes public sync wrappers over async loaders
**File**: `pinder-core/src/Pinder.NarrativeHarness/HarnessCharacterLoader.cs:73`
**Issue**: The public sync wrappers block on async implementations: `return LoadAsync(slug, archetypesEnabled).GetAwaiter().GetResult();` at line 73 and `return LoadBaseGameDefinitionAsync().GetAwaiter().GetResult();` at line 172. The comments say they are CLI-only, and the GameApi admin controller correctly uses `LoadAsync` at `pinder-web/src/Pinder.GameApi/Controllers/AdminNarrativeHarnessController.cs:212` and `LoadBaseGameDefinitionAsync` at line 215.
**Impact**: The current web path avoids these wrappers, but they remain public API in a shared harness assembly. A future request-driven caller can accidentally use the sync wrapper and block an ASP.NET request thread on asynchronous file I/O.
**Urgency**: U2 - de-escalated one level from U1 because present production request code uses the async counterparts and current sync call sites are CLI/tests.
**Fixer-Agent Action Plan**: Move the sync wrappers into CLI-only code or obsolete them with analyzer-visible guidance, then update `tools/NarrativeHarness/Program.cs` to await `LoadAsync` and `LoadBaseGameDefinitionAsync`. Add a guardrail test or analyzer check that `Pinder.GameApi` does not call `HarnessCharacterLoader.Load` or `LoadBaseGameDefinition`.

### Finding 5: LLM adapter tests synchronously read HttpContent inside async test flows
**File**: `pinder-core/tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicClientSpecTests.EdgeCases.cs:247`
**Issue**: Async tests capture request bodies through sync-over-async calls such as `capturedBody = req.Content!.ReadAsStringAsync().Result;` at line 247, `capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();` in `pinder-core/tests/Pinder.LlmAdapters.Tests/OpenAi/OpenAiStreamingTransportTests.HappyPaths.cs:203`, and repeated `ReadAsStringAsync().GetAwaiter().GetResult()` capture lambdas in `pinder-core/tests/Pinder.LlmAdapters.Tests/OpenAi/Issue947_PromptCacheControlTests.cs:67`.
**Impact**: These tests can hang or mask async scheduling bugs if the content read ever stops completing synchronously. They also normalize `.Result`/`.GetResult()` in code that is specifically validating async HTTP transport behavior.
**Urgency**: U2 - de-escalated one level from U1 because the blocking is test-only, but it still affects async transport regression coverage.
**Fixer-Agent Action Plan**: Change the test capture handlers to support asynchronous capture, or buffer request content in the fake handler with an awaited `ReadAsStringAsync` before returning. Update the affected tests to await captured-body extraction and add a static guardrail over `tests/Pinder.LlmAdapters.Tests` for `ReadAsStringAsync().Result` and `GetAwaiter().GetResult()`.

### Finding 6: DC bias tests block on async GameSession turn APIs
**File**: `pinder-core/tests/Pinder.Core.Tests/Issue1168_DcBiasTests.cs:127`
**Issue**: Synchronous xUnit tests call async game-session APIs through sync-over-async: `var turnStart = session.StartTurnAsync().GetAwaiter().GetResult();` at line 127, `var turnResult = session.ResolveTurnAsync(0).GetAwaiter().GetResult();` at line 128, and the same pattern repeats at lines 157-158, 176-177, and 188-189.
**Impact**: The tests exercise the same async turn pipeline used by production, but by blocking on it they can deadlock under a future test synchronization context and make async regressions harder to diagnose.
**Urgency**: U2 - de-escalated one level from U1 because the blocking is confined to tests.
**Fixer-Agent Action Plan**: Convert the affected `[Fact] public void` tests to `public async Task`, replace each `.GetAwaiter().GetResult()` with `await`, and run the targeted `Pinder.Core.Tests` fixture. Add a lightweight static test that rejects new `.GetAwaiter().GetResult()` calls in core tests except documented synchronous API shims.

No approved-exception suppressions were applied; the currently acceptable exception bullets are error-leakage patterns and did not match this sync-blocking audit.

Counts: U1=2, U2=4, U3=0. Findings=6. Suppressed would-be U1=0.

Output hash confirmation: report-body-sha256=63fbc3e180641d4022f332dbc31e451c924430cdb38422c0f7f62f211903feda
