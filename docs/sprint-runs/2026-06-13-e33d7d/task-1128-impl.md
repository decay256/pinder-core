You are a technical writer subagent updating ONE documentation ticket (#1128) end-to-end in an isolated git worktree, then opening a docs PR. This is DOCS-ONLY.

## Workspace setup (isolated worktree)

Run EXACTLY this, do NOT touch /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/work-1128 origin/main
cd /tmp/work-1128
git submodule update --init 2>/dev/null || true
git checkout -b docs/1128-unity-integration-version-bump
```

All edits happen inside /tmp/work-1128. This is a PURE-DOCS ticket — do NOT touch any `.cs` or `.yaml` file.

## Role spec

Read and follow /root/projects/eigentakt/agents/technical-writer.md. Your PR body MUST include a DoD section confirming docs-only diff and resolved cross-links.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1128/LESSONS_LEARNED.md. Key ones:
- DOCS-FOLLOW-CODE: every cadence/schema/event-name/field-name in the doc MUST be present in the code at the same HEAD the doc merges into, else the doc is fiction. Spot-check the rename mapping table against actual `src/` identifiers.
- The doc's stamped contract version MUST equal the code's `ApiContract.ApiContractVersion` constant (currently = 1).

## AGENTS.md (project rules)

Honor the project AGENTS.md:
- Docs-only, pinder-core only. This ticket does NOT modify Unity (GitLab Diego_Quarantine/p-game is READ-ONLY) — it DESCRIBES what Unity must change, owned by Martin (cross-repo). No code, no server validation.
- No `dotnet` build/test gate for a pure-docs PR, but DoD MUST confirm (a) no `.cs`/`.yaml` touched and (b) cross-links resolve (no dead relative links).

## Scope — #1128 Unity integration doc, version-bumped (DOCS-ONLY, pinder-core)

REWRITE / version-bump IN PLACE — `docs/unity-integration.md` already exists (~33 KB). Do NOT create a second file.

Cover / update:
1. **old→new API field name mapping table** for Unity-side updates: OPPONENT→DATEE, PLAYER→PLAYER AVATAR. Present as a mapping table so Martin can apply it client-side. **Spot-check the table against the real renamed identifiers in `src/`** (e.g. `BuildPlayerAvatar`, DATEE tags, `PlayerAvatarCard`/`DateeCard`, the `player_avatar_role_description` yaml key just renamed in #1133). The mapping MUST match code after #1121/#1122 (merged) and #1133 (merged).
2. **The two-session GM model** (avatar session + datee session, GM-as-puppeteer acting ONE character, bleed isolation).
3. **The GM output-format contract** Unity/pinder-core must parse (the canonical Emit/Parse contract introduced in #1124 — `GmOutputContract`/`GmTurnOutput`, the suggested line + optional `[SIGNALS]` tags). Describe the wire shape Unity parses.
4. **The `apiVersion` handshake (#1127):** document that the request carries `apiVersion`, the version number, and the mismatch error shape. **Stamp the doc with contract version = 1** (this MUST equal `ApiContract.ApiContractVersion`; the code lives at `src/Pinder.Core/Contracts/ApiContract.cs`, constant `ApiContractVersion = 1`). Document the mismatch error: code string `api_version_mismatch`, body fields `code`, `message`, `received`, `supported`. Note that Unity must SEND `apiVersion` (Unity-side change owned by Martin) and that server-side validation is a pinder-web follow-up.

## Cross-links
- Cross-link from `docs/ARCHITECTURE.md` (exists) to `docs/unity-integration.md`.
- Cross-link `docs/unity-integration.md` to the prompt-graph doc at path **`docs/prompt-graph.md`** (the natural location). NOTE: `prompt-graph.md` does NOT exist yet — #1130 owns creating it at exactly `docs/prompt-graph.md`. Add the link to `docs/prompt-graph.md` now; #1130 will create that file. Use a relative link.
- **Important about cross-links:** since `docs/prompt-graph.md` won't exist until #1130 lands, your "no dead relative links" DoD check should NOTE this one forward-link as intentional/pending-#1130 rather than failing on it. All OTHER relative links must resolve.

## Acceptance
- `docs/unity-integration.md` rewritten in place, version-stamped (version == 1 == #1127's ApiContractVersion).
- Field-rename mapping complete and matches the code identifiers in `src/` (spot-checked).
- `apiVersion` section matches #1127's constant + error body shape (code `api_version_mismatch`; fields `code,message,received,supported`).
- Cross-linked from `docs/ARCHITECTURE.md`; forward-link to `docs/prompt-graph.md` (pending #1130) present.
- DoD confirms: no `.cs`/`.yaml` files touched (docs-only diff), and all relative links resolve EXCEPT the intentional forward-link to `docs/prompt-graph.md`.

## PR
- Push branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1128` on its own line, plus a DoD section (docs-only diff confirmation + cross-link check) and a short note of what sections you rewrote.
- Do NOT merge. Report the PR URL + commit SHA + the list of doc files touched.
- Append to /tmp/work-1128/agent.log a `started` and `completed` (with PR URL + SHA) JSONL line.

Report back: PR URL, commit SHA, list of doc files touched (confirm no .cs/.yaml), the stamped version number, confirmation the rename table matches code, and the cross-link status.
