### Finding 1: SessionsController owns turn-preview gameplay rules
**Status**: Resolved
**Resolution**: Extracted option preview construction into TurnStateProjectionService, including DC, modifiers, tells, triple/weakness state, shadow and horniness risk, effect labels, and trap-disarm semantics. SessionsController now delegates projection and only assembles the route response. The integrated pinder-web commit is 1bd8835d.
**Verification**: Twenty-one focused GameApi projection tests passed on the integration branch.

### Finding 2: UI predicts trap-disarm eligibility from stat strings
**Status**: Resolved
**Resolution**: Added backend-projected disarms_active_trap to the DTO, API contract, and frontend types. The option UI renders the badge from this field rather than normalizing stat strings. The integrated pinder-web commit is 1bd8835d.
**Verification**: Six frontend contract/component tests and the strict frontend build passed, proving the badge follows the DTO even when the stat string suggests the opposite.

### Finding 4: NarrativeTestbed page bypasses the frontend API layer for admin harness calls
**Status**: Resolved
**Resolution**: Added narrativeHarnessClient using adminJsonFetch for harness list, detail, and run creation. NarrativeTestbed and its history view now delegate HTTP behavior to this API layer. The integrated pinder-web commit is cb26d188.
**Verification**: Thirteen focused NarrativeTestbed tests and the strict frontend build passed.

### Finding 5: MediaController owns Eigencore asset-store protocol details
**Status**: Resolved
**Resolution**: Extracted Eigencore upload and fetch protocol handling behind IMediaAssetStore and EigencoreMediaAssetStore with an injected named HttpClient. MediaController now validates route inputs and maps typed service outcomes to responses. The integrated pinder-web commit is c3f00473.
**Verification**: Seven focused GameApi service and controller tests passed, covering metadata, authentication, upstream failures, fetch content, and status mapping.

### Finding 3: Admin narrative harness controller orchestrates harness domain setup directly
**Status**: Resolved
**Resolution**: Extracted narrative harness setup and execution into NarrativeHarnessRunService with explicit dependency seams. The controller now handles HTTP input and response mapping while delegating harness orchestration to the service. The integrated pinder-web commit is c284e5fc.
**Verification**: Thirty-two focused NarrativeHarness service and controller tests passed with the repository's exact .NET 8 SDK.
