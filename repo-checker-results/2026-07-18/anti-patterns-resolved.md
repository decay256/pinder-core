### Finding 1: Message-only fallback silently drops validated GM signals
**Status**: Resolved
**Resolution**: Added a strict validated-signal parse path in GmOutputContract that emits an operational diagnostic and throws LlmContractException instead of collapsing validated signal-bearing output to message-only. The production datee response path uses the strict parser after ValidateSignalsStrict accepts a signals block, while legacy best-effort callers retain the explicitly lenient path. The integrated pinder-core commit is a5d244f.
**Verification**: Focused GmOutputContract tests passed 19/19 on the integration branch and on Linux; assembly version tests passed 3/3.

### Finding 2: Database URL option typos are swallowed during production startup
**Status**: Resolved
**Resolution**: DatabaseUrlNormalizer now collects Npgsql-rejected query options and fails startup with a sanitized error naming only unsupported or invalid option keys. Program startup handles that configuration exception without echoing the database URL or its credential-bearing values. The integrated pinder-web commit is cb0b1abe.
**Verification**: Focused DatabaseUrlNormalizer and startup fail-fast tests passed 35/35 on the integration branch.

### Finding 3: Request logging hides JWT extraction failures
**Status**: Resolved
**Resolution**: Replaced the broad swallowed exception path with a structured auth-extraction result. Expected JWT failures bind safe auth_failure_kind and user_sub_extract_error markers while preserving request IDs and never logging token content; unexpected verifier failures are no longer swallowed. The integrated pinder-web commit is 1dd3ec36.
**Verification**: The complete logging-correlation test file passed 20 tests on the integration branch and on Linux.

### Finding 4: Session-number lock cleanup swallows filesystem failures
**Status**: Resolved
**Resolution**: SessionFileCounter now reports stale-lock inspection and deletion failures through a testable warning sink, defaulting to standard error, while preserving retry behavior for actual lock contention. A filesystem seam permits deterministic failure testing. The integrated pinder-core commit is 3943b9e.
**Verification**: Forty focused SessionFileCounter tests passed on the integration branch and on Linux, including stale-lock and release-delete failures.

### Finding 5: Player response delay buckets are embedded as unexplained numeric thresholds
**Status**: Resolved
**Resolution**: Replaced duplicated threshold and penalty literals with named policy constants reused by bucket selection and the one-to-six-hour trigger. The integrated pinder-core commit is 29c8db1.
**Verification**: All 109 player response delay evaluator/spec tests passed with major runtime roll-forward, and the test project compiled.

### Finding 6: XP success labels retain hidden fallback DC thresholds
**Status**: Resolved
**Resolution**: Added the typed SuccessDcLabelThresholds contract to IRuleResolver, implemented explicit GameDefinition and RuleBookResolver resolution, and removed SessionXpRecorder reflection plus the hidden 16/20 defaults. RuleBookResolver now resolves success XP against configured upper-bound ranges and reads explicit, fully described failure-pool tier entries. The integrated pinder-core commits are 112e9be, 218406e, and 7962415; pinder-web pins the final contract in c7a465bb.
**Verification**: The solution compiled; 33 GameDefinition progression tests and 65 populated RuleBookResolver tests passed, including non-boundary DCs and all configured failure-pool tiers.

### Finding 7: Visual asset canvas concentrates multiple render-loop responsibilities in one component file
**Status**: Resolved
**Resolution**: Split camera controls, context restoration, material application, and model lifecycle behavior into focused visual-asset modules while retaining the canvas component as their composition boundary. The integrated pinder-web commits are b3a309f3 and d79b4b6e; commit 61ca5b69 updates the WebGL deployment gate to the current anatomy endpoint and UI contract.
**Verification**: Twenty focused visual-asset tests and the strict frontend build passed. Three Playwright WebGL scenarios also passed, including nonblank pixel checks, keyboard camera movement, and context-loss restoration.
