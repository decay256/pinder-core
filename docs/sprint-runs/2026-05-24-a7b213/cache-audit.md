# Cache audit — sprint `2026-05-24-a7b213`

Generated 2026-05-26 16:57 UTC by `scripts/cache-audit.sh`.

## Honest caveat

This audit is a **proxy** for "caching is happening," not "caching is paying
off." The OpenClaw Stats line currently surfaces only the aggregate
`prompt/cache` total — read vs. write split is not exposed.

Anthropic charges **1.25× prompt cost** on cache *writes* and **0.1×** on
cache *reads*. A run with a high `prompt_cache_total` dominated by writes
is more expensive than no cache at all. This audit cannot tell those
two scenarios apart. Treat the numbers as a coarse signal: low ratios
are definitely wrong; high ratios are *probably* fine but unverifiable
until the per-direction gap is closed.

Tracked separately: a GitHub issue against `openclaw/openclaw` asking
for `cache_read_tokens` / `cache_write_tokens` in the Stats line.

## Flag thresholds (seed-uncalibrated)

- **ratio < 0.2** for ≥3 attempts → cache likely broken or thrashing (🔴)
- **ratio 0.2 – 0.4** → low cache hit; prefix may be drifting (🟡)
- **ratio > 0.5** → healthy (🟢)
- **ratio 0.4 – 0.5** → borderline (⚪)

Thresholds revisit after 3 sprints of data per
TRIGGER-CONSERVATISM-UNTIL-CALIBRATED.

## Summary

Total attempts in sprint: **44**

## Per-role × per-rung breakdown

| Role | Rung | Attempts | With cache data | Mean ratio | Min | Max | Recovery records | Status |
|------|------|----------|-----------------|------------|-----|-----|------------------|--------|
| backend-engineer | 1 | 22 | 7 | 0.681714 | 0.484 | 0.903 | 10 | 🟢 healthy |
| code-reviewer | 2 | 21 | 10 | 2400.65 | 1130.508 | 4575.000 | 8 | 🟢 healthy |
| frontend-engineer | 1 | 1 | 1 | 0.837 | 0.837 | 0.837 | 0 | 🟢 healthy |

## What to do with red flags

- **🔴 cache likely broken or thrashing:** check that `scripts/cache-prefix-check.sh`
  ran on every spawn for this role × rung (look for `cache_prefix_check`
  entries in `agent.log`). If lint passed but ratios are still low,
  the provider/runtime may be silently invalidating cache — file a
  runtime issue.
- **🟡 low cache hit:** likely the prefix is canonical but some
  per-ticket content is leaking into what should be the stable prefix.
  Review the role's prompt template for accidental dynamic content
  near the top.
- **🟢 healthy:** no action. Continue calibration.

## Cache-prefix lint coverage

Cache-prefix lint check entries in agent.log for this sprint:
**36** `cache_prefix_check` log entries written for this sprint.

Distinct prefix hashes observed: **26**.

⚠️  More than 5 distinct prefix hashes in one sprint suggests the prompt template is producing non-deterministic content in what should be the stable prefix. Investigate.

---

*Audit script: `scripts/cache-audit.sh` • lesson: CACHE-PREFIX-STABILITY*
