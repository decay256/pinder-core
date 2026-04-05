# CPO Strategic Review — Sprint: Session Runner Bug Fixes

## Alignment: ✅ CLEAN

This sprint is a pure tactical bug-fix pass on the `session-runner/` console app. All 5 issues address real, observable defects (empty bios, hardcoded headers, overwritten files, wrong levels, inflated EV scores) that degrade playtest quality. Playtest quality is the feedback loop that validates the entire RPG engine — fixing it is high-leverage even though the changes are small. No architectural changes, no new abstractions, no dependency changes. This is exactly the right work at prototype maturity: make the existing tooling actually work correctly before building more features on top of it.

## Insufficient Requirements Check

All 5 issues pass the prototype threshold (>50 chars of meaningful content, root cause identified, AC present):

| Issue | Status | Notes |
|-------|--------|-------|
| #513 (ParseBio) | ✅ Sufficient | Root cause, fix strategy, 3 AC items |
| #514 (DC header) | ✅ Sufficient | Root cause with line number, fix strategy, 3 AC items |
| #515 (SessionFileCounter) | ✅ Sufficient | Root cause analysis, fix strategy, 3 AC items |
| #516 (ParseLevel) | ✅ Sufficient | Root cause, fix strategy, 3 AC items |
| #517 (ScoringAgent EV) | ✅ Sufficient | Root cause with session evidence, fix strategy, 3 AC items |

## Data Flow Traces

### #513 — ParseBio
- Prompt file → `CharacterLoader.Parse()` → `ParseBio()` → `CharacterProfile.Bio` → playtest header output
- Required fields: bio text string
- ⚠️ Currently broken: `ParseBio` searches for `"` delimiters that don't exist → returns empty string
- Fix is contained: change delimiter logic to take everything after `- Bio:`

### #516 — ParseLevel + JSON data
- Two paths: (1) JSON → `CharacterDefinitionLoader` → `CharacterProfile.Level`; (2) Prompt file → `CharacterLoader` → `ParseLevel()` → `CharacterProfile.Level`
- `Program.cs LoadCharacter()` tries JSON path first → stale `"level": 4` in JSON wins over correct prompt file value
- Required fields: level integer
- Fix touches data files AND parsing — both paths must agree

### #517 — ScoringPlayerAgent EV
- `TurnStart.Options[]` → `ScoringPlayerAgent.Score()` → per-option EV → pick highest → `PlayerDecision`
- Required fields: success probability, combo bonus, tell bonus, fail cost per tier
- ⚠️ Combo/tell bonuses applied at full value regardless of success probability → inflated EV on low-success options

## Unstated Requirements

- **Playtest output must be trustworthy**: If the session header shows wrong level/empty bio, and the DC table shows wrong names, testers lose confidence in all output — including the gameplay itself. These aren't cosmetic bugs; they erode the feedback loop.
- **Session files must be append-only**: If session-006 overwrites previous session-006, playtest history is lost. The user expects a monotonically increasing archive.

## Domain Invariants

- Character data loaded from any path (JSON or prompt file) must produce the same `CharacterProfile` values
- Session file numbering must be strictly monotonic (no gaps, no overwrites)
- ScoringPlayerAgent EV must be monotonically related to actual success probability (higher success % → higher EV, all else equal)

## Gaps

- **Missing (minor)**: No issue addresses the discrepancy between JSON and prompt file paths holistically. #516 fixes the JSON data values, but the two-path loading strategy remains a source of future drift. Acceptable at prototype — flag for MVP.
- **Unnecessary**: Nothing — all 5 issues are warranted.
- **Assumption**: #516 assumes updating JSON `"level"` values is sufficient. If `CharacterDefinitionLoader` derives level from the assembler pipeline differently than `CharacterLoader` parses it, they could diverge again. The architect's diagnosis (stale JSON data, not parsing bug) should be verified by the implementer.

## Requirements Compliance Check

- No FR/NFR/DC violations. All changes are in `session-runner/` which has no design constraints from `docs/requirements.md`.
- Zero-dependency invariant for Pinder.Core: ✅ Not touched.
- Backward compatibility (all existing tests pass): ✅ No Pinder.Core changes that could break tests.

## Recommendations

1. **Proceed as-is.** All 5 issues are well-scoped, isolated, and high-value for playtest reliability.
2. **Implementer for #516 should verify both loading paths agree** after the JSON data fix — run both `CharacterDefinitionLoader` and `CharacterLoader` for Velvet and compare output.

## Verdict

No concerns warrant filing as GitHub issues. The sprint is tactical, correctly scoped, and serves the product vision by fixing the playtest feedback loop.

**VERDICT: CLEAN**
