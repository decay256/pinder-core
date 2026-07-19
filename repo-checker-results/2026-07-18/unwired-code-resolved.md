### Finding 1: Anthropic improvement pass is never called
**Status**: Resolved
**Resolution**: Repository configuration and tool schema evidence showed the improvement pass is intended. AnthropicTransport now invokes AnthropicResponseImprover for configured initial-generation phases, and the session runner passes the loaded GameDefinition so ImprovementPrompt performs the second tool-backed call. Rewrite phases are explicitly excluded to prevent recursive overlay or delivery rewrites. The integrated pinder-core commits are 11dbc29 and 218406e.
**Verification**: Seven focused Anthropic transport tests passed, covering configured and absent improvement prompts plus single-call behavior for delivery, horniness, shadow, and trap rewrite phases.

### Finding 2: Timing profile JSON loader is test-only while production ignores `data/timing/response-profiles.json`
**Status**: Resolved
**Resolution**: Added a timing repository contract and timing_profile_id to character definitions, wired the real timing JSON into session-runner and narrative-harness character loading, and made CharacterAssembler apply the selected base timing before item and anatomy modifiers. Starter character data and schema were updated, and unknown profile IDs fail fast. The integrated pinder-core commit is 6c09173 and the core version is 0.2.9.
**Verification**: The two new timing integration regressions and the version-bump guard passed on the integration branch. The worker's Linux timing suite passed eight tests, and its rules version tests passed seven tests.

### Finding 3: BiasedDiceRoller is obsolete and unused
**Status**: Resolved
**Resolution**: Removed the unused BiasedDiceRoller implementation and updated the mechanics documentation to describe the active roll-resolution path. Added regression coverage for natural 1 and natural 20 behavior under DC bias. The integrated pinder-core commit is a4c65d4.
**Verification**: The core test project compiles locally; the worker passed 15 roll-resolution and six DC-bias tests on Linux.

### Finding 5: SetupStatusResponse is an unused API model
**Status**: Resolved
**Resolution**: Removed the obsolete response model and added a registration guard proving the removed setup-status endpoints remain absent from the API surface. The integrated pinder-web commit is e9e36132.
**Verification**: The GameApi and GameApi test projects compile, and the worker passed both endpoint-registration regression tests.

### Finding 4: IFailurePool has no implementation or consumer
**Status**: Resolved
**Resolution**: Confirmed the interface was superseded by IConsequenceCatalog and the delivery instruction/overlay path, removed the dead public contract, and documented the live extension points. The integrated pinder-core commit is 150ee2d.
**Verification**: The solution compiled and four focused public-surface/architecture tests passed with major runtime roll-forward.

### Finding 6: Legacy token usage DTO is replaced by the wired token usage response
**Status**: Resolved
**Resolution**: Removed SessionTokenUsageDto and added a contract guard confirming TokenUsageResponseDto remains the active API model. The integrated pinder-web commit is 83e1dcbc.
**Verification**: GameApi and frontend production builds passed; the new guard passed. The broader endpoint test remains blocked locally by the missing native .NET 8 runtime and .NET 10 ASP.NET incompatibility.

### Finding 7: MECHANIC_ROWS duplicates stat wiring but is not consumed
**Status**: Resolved
**Resolution**: Made MECHANIC_ROWS the canonical stat-mechanics table and derived ATTACKER_TO_DEFENDER_MAP from it, eliminating the second hand-maintained mapping. The integrated pinder-web commit is b6c0fe12.
**Verification**: Nine focused mechanics and StatDisplayTable tests passed, and the production frontend build succeeded.

### Finding 8: Operation player title localization helper is never rendered
**Status**: Resolved
**Resolution**: Wired operationPlayerTitle into the GameScreen retry/error state as its localized heading, preserving the existing summary and retry action. The integrated pinder-web commit is 988e0d9b.
**Verification**: The focused wiring test passed and the production frontend build succeeded.

### Finding 9: Admin prompt source-file map is exported but unused
**Status**: Resolved
**Resolution**: Confirmed saves are intentionally routed by active admin tab while source-file metadata belongs to API/tracer paths, removed TAB_TO_SOURCE_FILE, and guarded the active save contract. The integrated pinder-web commit is ee038472.
**Verification**: Thirteen prompt-editor tests passed and the production frontend build succeeded.
