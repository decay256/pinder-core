# model-id-drift resolutions

### Finding 1: Adapter phase calls bypass the central temperature catalog
**Status**: Deferred (U2 - ticketed: pinder-core#1325 / pinder-web#1177)
**Resolution**: Deferred to pinder-core#1325 / pinder-web#1177 for follow-up after the release gate.
**Verification**: Confirmed by the final changed-code audit; no release-gate code change was required.

### Finding 2: Admin speculation copies phase temperatures instead of using shared constants
**Status**: Deferred (U3 - recorded)
**Resolution**: Recorded for review; this maintainability item is not a release blocker.
**Verification**: Confirmed by the final changed-code audit; no release-gate code change was required.

### Finding 3: Character generation hides a hardcoded 2048-token floor from LLM config
**Status**: Deferred (U2 - ticketed: pinder-core#1325 / pinder-web#1177)
**Resolution**: Deferred to pinder-core#1325 / pinder-web#1177 for follow-up after the release gate.
**Verification**: Confirmed by the final changed-code audit; no release-gate code change was required.

### Finding 4: Concrete transports duplicate optional temperature and token defaults
**Status**: Deferred (U3 - recorded)
**Resolution**: Recorded for review; this maintainability item is not a release blocker.
**Verification**: Confirmed by the final changed-code audit; no release-gate code change was required.

