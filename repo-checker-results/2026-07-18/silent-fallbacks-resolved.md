### Finding 1: Character index silently drops unreadable, malformed, and duplicate character files
**Status**: Resolved
**Resolution**: DirectoryCharacterStore now validates every character JSON file except character-schema.json before publishing its index. Malformed JSON, missing or invalid character IDs, unreadable files, access failures, and duplicate IDs produce path-specific errors and fail the catalog load instead of silently omitting or overwriting entries. The integrated pinder-core commit is 47a436a.
**Verification**: Focused DirectoryCharacterStore tests passed 32/32 on the integration branch, including malformed, missing-ID, duplicate-ID, and simulated unreadable-file cases.

### Finding 2: Production rule resolver allows missing YAML rule values to fall back to hardcoded defaults
**Status**: Resolved
**Resolution**: Production-loaded GameDefinition and RuleBookResolver instances now disable default fallback. LevelTable fails closed when required XP thresholds or failure-pool tier minima are absent, while explicit test and development resolvers can retain fallback behavior. The integrated pinder-core commit is 91e0de1.
**Verification**: Integration-branch checks passed 31 GameDefinition progression tests and 67 RuleBookResolver/GameDefinition YAML tests. The worker additionally passed the rules pipeline, version-bump checks, and focused LevelTable, SessionXpRecorder, and adapter tests.

### Finding 3: Backend health reports GameApi dependency failure as HTTP 200, so deploy and Docker checks mark degraded as healthy
**Status**: Resolved
**Resolution**: Backend health now returns HTTP 503 when GameApi is unavailable or unhealthy while preserving the degraded JSON body. Docker Compose and deploy health gates parse the response and require status to equal ok, preventing degraded dependencies from passing readiness. The API contract and backend version were updated. The integrated pinder-web commit is f4576a97.
**Verification**: The integration branch passed 41 focused FastAPI, Compose-healthcheck, deploy-gate, and correlation tests. The worker also validated shell syntax and exercised ok versus degraded probe behavior without deploying.

### Finding 4: Setup succeeds after outfit-description generation failure and only stores an empty scene entry
**Status**: Resolved
**Resolution**: Outfit-description failures now record terminal OperationStatus.Degraded with stable code setup.outfit_description_failed instead of clean success. The session remains playable, no empty outfit scene entry is fabricated, and first-turn tracking preserves the degraded terminal result. The integrated pinder-web commit is 550459ac.
**Verification**: Fourteen focused operation/setup tests passed on the integration branch; the worker's targeted Linux suite passed 36 tests and the version guard passed.

