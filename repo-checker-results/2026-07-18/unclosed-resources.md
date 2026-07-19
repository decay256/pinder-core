> Scope: full repositories. PRIMARY `pinder-core` at `e96a75f4`; DEPENDENT `pinder-web` at `1a7acb38`.

### Finding 1: DirectoryCharacterStore Owns A Semaphore But Has No Dispose Path
**File**: `pinder-core/src/Pinder.SessionSetup/DirectoryCharacterStore.cs:45`
**Issue**: `private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);` is owned by `DirectoryCharacterStore`, but the class declaration is only `public sealed class DirectoryCharacterStore : ICharacterStore` and exposes no `Dispose`/`DisposeAsync` path. The store is also used by the dependent GameApi as the local `ICharacterStore`, so DI cannot deterministically release the gate when the store is retired.
**Impact**: Repeated short-lived stores in tools/tests and the long-lived GameApi local character store leave semaphore resources to GC/process shutdown instead of deterministic cleanup. If the semaphore ever materializes a wait handle, this becomes a real handle leak; even without that, it is an ownership contract gap for a shared store abstraction.
**Urgency**: U2 - de-escalated from the topic default U1 because the leaked object is a managed concurrency gate and not an immediately exhausted socket/file handle, but it is still a concrete lifecycle bug in a shared production dependency.
**Fixer-Agent Action Plan**: Make `DirectoryCharacterStore` implement `IDisposable`, dispose `_gate`, and guard public methods against use after dispose. Add focused tests that dispose a store twice safely and that `CharacterStoreFactory`/GameApi DI can dispose a local store through the `ICharacterStore` singleton.

### Finding 2: Media Proxy Never Disposes Outbound HttpResponseMessage Instances
**File**: `pinder-web/src/Pinder.GameApi/Controllers/MediaController.cs:88`
**Issue**: `var response = await client.PostAsync($"{baseUri}/assets", content, ct).ConfigureAwait(false);` and `var response = await client.GetAsync($"{baseUri}/assets/{assetId:D}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);` are not wrapped in `using`/`try-finally`. The upload path reads the response body and returns without disposing `response`; the download path returns `File(stream, contentType)` while the owning `HttpResponseMessage` remains undisposed on success, 404, and non-success exits.
**Impact**: Public media upload/download traffic can retain response content objects and pooled HTTP connections longer than intended. Under repeated media requests, this can starve the `IHttpClientFactory` handler pool or leave upstream asset-store sockets open until GC rather than releasing them when the controller action completes.
**Urgency**: U1 - topic default; public HTTP proxy endpoints leak response lifecycle ownership on both upload and streaming download paths.
**Fixer-Agent Action Plan**: Wrap upload responses in `using var response`. For downloads, either buffer/copy the upstream stream into an owned stream before disposing the response, or return a custom `FileCallbackResult`/stream wrapper that disposes both the content stream and `HttpResponseMessage` after ASP.NET finishes writing. Add tests with a fake handler that counts disposed responses for success, 404, non-success, and streaming success.

### Finding 3: Rehydrate Race Cleanup Fires ActiveSession.DisposeAsync Without Awaiting It
**File**: `pinder-web/src/Pinder.GameApi/Services/SessionStore.Persistence.cs:87`
**Issue**: `_ = active.DisposeAsync().AsTask();` starts cleanup for a throwaway rehydrated `ActiveSession` after a cache race, but the task is ignored. `ActiveSession.DisposeAsync` releases LLM adapters/transports, semaphores, and cancellation tokens after an initial `await Task.Yield()`, so the method returns before cleanup is complete or observed.
**Impact**: A rehydrate race can temporarily or permanently leak the discarded session's LLM transport/HTTP client resources if the fire-and-forget cleanup faults or is abandoned during shutdown. The caller also returns the cached session before the loser has deterministically released its resources.
**Urgency**: U2 - de-escalated from the topic default U1 because this requires a concurrent rehydrate race, but the leaked resources include production LLM transport ownership and async cleanup is explicitly nondeterministic.
**Fixer-Agent Action Plan**: Replace the fire-and-forget call with `await active.DisposeAsync().ConfigureAwait(false);` before returning the existing cached session, and add a race-focused unit test using a disposable fake `ActiveSession` dependency or instrumented LLM transport to assert the loser is disposed before `RehydrateFromDbAsync` returns.

### Finding 4: TurnAuditWriter Accumulates Per-Session Semaphores Forever
**File**: `pinder-web/src/Pinder.GameApi/Services/TurnAuditWriter.cs:76`
**Issue**: `private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();` stores locks created by `var sem = _sessionLocks.GetOrAdd(session.Id, _ => new SemaphoreSlim(1, 1));` at line 280, but `TurnAuditWriter` is a singleton and never removes or disposes locks after a session ends, is marked broken, or ages out of retention.
**Impact**: Every audited session ID permanently adds a `SemaphoreSlim` to the singleton writer for the lifetime of the GameApi process. Long-running production instances with many sessions accumulate memory and potential wait-handle resources even after session cleanup and audit retention have removed the actual session data.
**Urgency**: U2 - de-escalated from the topic default U1 because the leak grows over session cardinality rather than immediately hanging a request, but it is unbounded in a singleton production service.
**Fixer-Agent Action Plan**: Add a disposal/removal lifecycle for per-session locks, for example remove and dispose the lock when a session is marked broken and expose a cleanup method called from session/audit retention for ended sessions. Alternatively replace the dictionary with keyed lock infrastructure that evicts idle locks safely. Add tests that write for multiple sessions, invoke cleanup, and assert removed semaphores are disposed without racing in-flight writes.

Approved-exception suppressions: none. The currently acceptable exception bullets are user-facing error leakage patterns, and no would-be U1 unclosed-resource finding matched them.

Counts: U1=1, U2=3, U3=0, total=4, suppressed_would_be_U1=0.
Output hash confirmation: body_sha256_excluding_this_line=3725d5949b5e4ebef36102c0cfe290135321ff6ad0cf828ecac770e97ea0737b; mirrored_files_identical=true
