# Vision Concerns & Resolutions

This document tracks product-vision concerns that blocked sprint execution
and records the resolutions applied. It serves as an audit trail so future
agents and the PO can see *what* was decided, *why*, and *when*.

---

## VC-18 — Three sprints of unresolved concerns blocking #6 and #7

**Filed:** Issue #18  
**Sprints affected:** 2, 3, 4, 5  
**Status:** Resolved (safe defaults applied)

### Problem

Issues #6 (interest-state boundaries) and #7 (rules-constants tests) were
repeatedly attempted and failed because their acceptance criteria contained
items that had no rules basis or depended on non-code issues.

### Concerns & Resolutions

#### 1. Lukewarm enum value in #6

| | |
|---|---|
| **Concern** | #6 AC lists a `Lukewarm` enum value, but rules v3.4 §6 defines no range for it. The six ranges in the rules map to: Unmatched, Bored, Interested, VeryIntoIt, AlmostThere, DateSecured — no Lukewarm. |
| **Options** | (a) PO defines a Lukewarm range, or (b) remove Lukewarm from the AC. |
| **Resolution** | **Safe default applied: remove Lukewarm.** No rules basis exists. PO can add it later if desired. |

#### 2. Success scale tests in #7

| | |
|---|---|
| **Concern** | #7 AC §5 requires success-scale tests, but at the time of filing no `SuccessScale` class existed. |
| **Current state** | `SuccessScale` was implemented in commit `2bb0db2` (issue #7, sprint 4). The concern is now moot — the code exists and can be tested. |
| **Resolution** | **No change needed.** `SuccessScale.GetInterestDelta()` is implemented and the §5 tests can proceed. |

#### 3. #7 dependency on #4

| | |
|---|---|
| **Concern** | #7 lists `Depends on: #1, #2, #4`. Issue #4 is a vision-concern (no code PR) that will never produce a merge. This creates a permanent dependency-block. |
| **Resolution** | **Safe default applied: remove #4 from #7's dependency list.** #1 and #2 are already merged. #4 is a process issue, not a code prerequisite. |

---

## VC-4 — Issue #1 breaking test suite

**Filed:** Issue #4  
**Status:** Resolved (moot — both #1 and #2 merged)

### Problem

Merging #1 (defence table + base DC change) without #2 (test updates) would
leave `main` with a broken test suite.

### Resolution

Both #1 and #2 were merged together (PR #13 combined them). The concern is
no longer relevant. Issue #4 can be closed.

---

## Policy: Safe Defaults

When a vision concern blocks implementation for ≥ 3 sprint attempts and the
PO has not responded, the orchestrator or technical writer applies
**conservative safe defaults** — removing scope rather than inventing it.

All safe-default applications are recorded in this document and in the
affected issue bodies. The PO can override any default at any time by
updating the issue and reopening the implementation ticket.
