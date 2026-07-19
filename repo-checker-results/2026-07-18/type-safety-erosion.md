# type-safety-erosion

> Scope: full multi-repo audit of `A:\Data\ClaudeCodex\pinder-core` and `A:\Data\ClaudeCodex\pinder-web`.

### Finding 1: Frontend TypeScript build omits strict type checking
**File**: `pinder-web/frontend/tsconfig.app.json:2`
**Issue**: The frontend app compiler options start at `"compilerOptions"` but enable only lint-like checks (`noUnusedLocals`, `noUnusedParameters`, `erasableSyntaxOnly`, `noFallthroughCasesInSwitch`) and never enable `"strict": true`, `"noImplicitAny": true`, or `"useUnknownInCatchVariables": true`. The matching ESLint config uses `tseslint.configs.recommended` at `pinder-web/frontend/eslint.config.js:49` but does not enable type-checked rules or a `no-explicit-any` rule.
**Impact**: This leaves the React surface permissive enough for implicit any, explicit any, and unsafe catch variables to survive in production code. The concrete instances below are symptoms, but this root setting allows more to accumulate without failing `npm run build` or `npm run lint`.
**Urgency**: U3 - topic default; this is systemic type-hygiene erosion, but the current evidence is maintainability risk rather than a proven production failure.
**Fixer-Agent Action Plan**: Enable strict TypeScript options in `tsconfig.app.json` and `tsconfig.node.json`, switch ESLint to a type-checked TypeScript config, add explicit rules for `no-explicit-any` and ban-ts-comment suppressions, then fix or quarantine the resulting errors with typed DTOs and narrow helpers.

### Finding 2: Character save API accepts unchecked `any` payloads
**File**: `pinder-web/frontend/src/api/characterClient.ts:57`
**Issue**: The character write client exposes `export async function saveCharacter(slug: string, payload: any): Promise<CharacterDetail>` and sends `JSON.stringify(payload)` directly to `/api/characters/{slug}`. The main caller builds a mutable object and passes it through at `pinder-web/frontend/src/pages/CreationBench.tsx:568`, while related API normalization also casts nested `row.bands as AnatomyBandDefinitionRow[]` at `pinder-web/frontend/src/api/characterClient.ts:81` without validating each band.
**Impact**: The admin/content authoring boundary can drift from the C#/Python character DTOs without compile-time failure; misspelled or wrongly shaped fields are not caught until backend validation or, worse, accepted as extra character data. That makes future schema changes harder to trust.
**Urgency**: U3 - topic default; it is a public-ish content editing boundary, but backend validation still provides a later guard.
**Fixer-Agent Action Plan**: Define a `SaveCharacterRequest` type (or generated schema-derived type), type the CreationBench payload builder against it, replace the `any` parameter with that type, and validate nested anatomy/item rows with typed normalization functions before returning them.

### Finding 3: Collapsed event headers assert wire payload shape with `as any`
**File**: `pinder-web/frontend/src/components/eventbox/collapsedHeader.ts:199`
**Issue**: `deriveCollapsedHeader(kind: CollapsedHeaderKind, payload: unknown)` dispatches by kind, then uses unchecked assertions such as `payload as { roll: OptionRollSummary; shadowCheck: ShadowCheckSummary | null }` and five `payload as any` casts at lines 212-217. The only dispatch test exercises the happy-path `option_roll` object at `pinder-web/frontend/src/components/eventbox/collapsedHeader.test.ts:180`; it does not validate malformed or mismatched payloads.
**Impact**: This is exactly the frontend boundary where heterogeneous turn-event payloads become UI labels. A new event kind or backend field drift can render misleading mechanics labels or throw at runtime while TypeScript still reports the dispatch as valid.
**Urgency**: U3 - topic default; visible UI correctness can drift, but the issue is currently localized to event-box title derivation.
**Fixer-Agent Action Plan**: Replace `payload: unknown` plus casts with a discriminated payload map keyed by `CollapsedHeaderKind`, add per-kind type guards or parser functions, and extend the tests with negative/mismatched payload cases.

### Finding 4: XP breakdown type drift is preserved with `as any`
**File**: `pinder-web/frontend/src/components/TurnResultDisplay.tsx:153`
**Issue**: `TurnResult.xp_breakdown` is typed as `{ label: string; amount: number }[]` at `pinder-web/frontend/src/types/gameplay.ts:371`, but rendering still reads a legacy `value` field via `(item as any).value`. The regression test encodes the same drift by casting the fixture `xp_breakdown` to `as any` at `pinder-web/frontend/src/components/TurnResultDisplay.1034.test.tsx:141`.
**Impact**: The UI now treats two different XP breakdown wire contracts as acceptable while the declared `TurnResult` type documents only one. Future changes can silently depend on the legacy field because both production code and tests bypass the type contract.
**Urgency**: U3 - topic default; this is a legacy compatibility leak, not a current data-loss path.
**Fixer-Agent Action Plan**: Either add an explicit legacy DTO/parser that maps `{ value }` to `{ amount }` before rendering, or remove the fallback and update the test fixture to the canonical `amount` contract.

### Finding 5: Production Python code carries unchecked `type: ignore` suppressions
**File**: `pinder-web/src/pinder-backend/exception_handlers.py:43`
**Issue**: Production backend files suppress type errors instead of typing the boundary: `app.add_exception_handler(... )  # type: ignore[arg-type]` at lines 43 and 45, `_lifespan(...)  # type: ignore[no-untyped-def]` at `pinder-web/src/pinder-backend/main.py:120`, `JsonbType.load_dialect_impl(...)  # type: ignore[no-untyped-def]` at `pinder-web/src/pinder-backend/db/models.py:44`, `return redirect  # type: ignore[return-value]` at `pinder-web/src/pinder-backend/routes/auth.py:157`, and `import sourcemap  # type: ignore[import-untyped]` at `pinder-web/src/pinder-backend/client_error_sourcemap.py:50`.
**Impact**: These suppressions teach the codebase to bypass the type checker at framework edges. The auth redirect and exception-handler cases are request/response boundaries where a precise `Response`/handler type would be cheap and would prevent future mismatches from hiding behind the old ignore.
**Urgency**: U3 - topic default; the suppressions are localized, but they are in production request handling code.
**Fixer-Agent Action Plan**: Add exact return annotations and handler signatures, use FastAPI/Starlette handler protocol types where available, annotate SQLAlchemy dialect parameters/returns, and isolate the untyped sourcemap dependency behind a small typed wrapper or local stub.

### Finding 6: FastAPI session proxy returns raw `Any` JSON through public routes
**File**: `pinder-web/src/pinder-backend/session_services.py:588`
**Issue**: `SessionProxyService.list_sessions(self, user: dict) -> Any`, `get_turn(...) -> Any` at line 678, and `submit_turn(...) -> Any` at line 812 return `resp.json()` directly for successful GameApi responses. The route functions at `pinder-web/src/pinder-backend/routes/sessions.py:186` and `pinder-web/src/pinder-backend/routes/endpoints/session_turns.py:10` expose those values without response models or typed adapters.
**Impact**: The Python API layer is a public boundary between the SPA and GameApi, but the success shapes are not represented as Pydantic models or typed dictionaries. Backend/frontend DTO drift can pass through the proxy until the React side fails or starts compensating with more `any` casts.
**Urgency**: U3 - topic default; this is public-boundary type erosion, with runtime ownership/error guards still present.
**Fixer-Agent Action Plan**: Introduce response models for session summaries, turn results, and submit-turn responses, validate successful `resp.json()` through those models, and return model dumps rather than arbitrary JSON.

### Finding 7: GameApi operation proxy keeps canonical operation payloads as `Any`
**File**: `pinder-web/src/pinder-backend/routes/operation_proxy.py:115`
**Issue**: `OperationProxyResult.content` is declared as `Any`, `operation_proxy_result` accepts `mapper: Callable[[Any], dict[str, Any]]` at line 634, and parses `upstream_payload: Any = resp.json()` at line 640 before mapping. The public mapper `map_public_operation(payload: Any) -> dict[str, Any]` at line 168 performs many runtime probes rather than validating against a typed operation snapshot/event model.
**Impact**: Operations are a cross-service workflow surface with retry/action state, but the proxy cannot statically distinguish a snapshot, detail, event, list, or failure payload. That encourages permissive `dict[str, Any]` propagation and makes it easier for GameApi operation contract changes to bypass Python review.
**Urgency**: U3 - topic default; runtime sanitizers reduce immediate risk, but the boundary remains structurally untyped.
**Fixer-Agent Action Plan**: Define Pydantic models for upstream operation snapshot/event/detail payloads and public/admin projected payloads, parse `resp.json()` into those models, and narrow `OperationProxyResult.content` to a union of concrete response shapes.

### Finding 8: Rules DSL erodes type safety with `Dictionary<string, object>` gameplay contracts
**File**: `pinder-core/src/Pinder.Rules/RuleEntry.cs:15`
**Issue**: Rule conditions and outcomes are public mutable `Dictionary<string, object>?` fields (`Condition` and `Outcome`) rather than typed condition/outcome unions. Loading recursively normalizes arbitrary YAML dictionaries into `Dictionary<string, object>` at `pinder-core/src/Pinder.Rules/RuleBook.cs:123`, and evaluation/dispatch then consumes the objects through string-key switches at `pinder-core/src/Pinder.Rules/ConditionEvaluator.cs:18` and `pinder-core/src/Pinder.Rules/OutcomeDispatcher.cs:18`. Tests explicitly lock in unknown-key acceptance: `ConditionEvaluator_OnlyUnknownKeys_ReturnsTrue` uses `["future_mechanic"] = 42` and expects `true` at `pinder-core/tests/Pinder.Rules.Tests/SpecComplianceTests.ConditionEvaluator.cs:219`.
**Impact**: Core gameplay rules lose compile-time coverage for key names and value shapes. A typo or wrong value type can be represented as a valid rule object and may be ignored or coerced instead of rejected, so gameplay semantics can drift away from the YAML schema without a compiler-visible contract.
**Urgency**: U2 - escalated from U3 because this is core gameplay rule resolution; type erosion here can produce wrong rule behavior rather than only local maintainability noise.
**Fixer-Agent Action Plan**: Introduce typed condition/outcome records or discriminated unions for known rule keys, parse YAML into those types with validation diagnostics, and keep forward-compatible extension data separate from the evaluated rule contract.

Suppression notes: 0 relevant suppressions. The four approved exception bullets concern raw HTTP/exception/error-payload leakage and did not overlap this type-safety topic, so no finding was suppressed under those exceptions.
