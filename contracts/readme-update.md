# Contract: README Update (Issue #5 concern)

## Purpose
After v3.4 sync, the README will contain stale information. This should be updated as part of the sprint.

## File
`README.md`

## Changes Required

### 1. Roll formula section
```
BEFORE: DC = 10 + opponent's defending stat modifier
AFTER:  DC = 13 + opponent's defending stat modifier
```

### 2. Defence pairings (implicit in description)
The README doesn't list all pairings explicitly, but the roll formula section implies base DC = 10. Just update the number.

### 3. Interest range
If the README mentions interest max (it currently doesn't explicitly), update to 25.

## Owner
This can be done by any issue's implementer as a drive-by fix, or as a separate commit in the architecture PR (#3).
