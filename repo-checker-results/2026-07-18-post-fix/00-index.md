# Post-Fix Repo Check

Date: 2026-07-18

Scope: full `pinder-core` and `pinder-web` repositories, limited to the seven categories fixed in the preceding sprint.

- `pinder-core`: `7962415f750de354b53d4f9b953eaa3e37b3575b`
- `pinder-web`: `c7a465bb67fb86ee6b5ab1105955b7c0717eddca`

| Topic | Findings | U1 | U2 | U3 | Report |
| --- | ---: | ---: | ---: | ---: | --- |
| DRY violations | 7 | 0 | 0 | 7 | [dry-violations.md](dry-violations.md) |
| Unwired code | 5 | 0 | 3 | 2 | [unwired-code.md](unwired-code.md) |
| Backend/frontend gaps | 2 | 0 | 2 | 0 | [backend-frontend-gaps.md](backend-frontend-gaps.md) |
| Separation-of-concerns violations | 8 | 0 | 1 | 7 | [soc-violations.md](soc-violations.md) |
| Anti-patterns | 7 | 4 | 1 | 2 | [anti-patterns.md](anti-patterns.md) |
| Silent fallbacks | 8 | 7 | 1 | 0 | [silent-fallbacks.md](silent-fallbacks.md) |
| Secrets exposure | 0 | 0 | 0 | 0 | [secrets-exposure.md](secrets-exposure.md) |
| **Total** | **37** | **11** | **8** | **18** | |

## Review Notes

- Each topic was inspected sequentially by a dedicated repo-checker worker across both repositories.
- The final cross-topic review found no duplicated findings.
- Every finding includes file, issue, impact, urgency, and fixer-agent action-plan fields.
- No concrete hardcoded secret or credential exposure was found.
- Four approved exception classes were suppressed in `silent-fallbacks.md` and `secrets-exposure.md`; each is recorded there with its would-be U1 urgency.
- This is a focused follow-up audit, not a rerun of the other repo-checker categories.
