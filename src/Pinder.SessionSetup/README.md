# Pinder.SessionSetup

Pre-session helpers that run after a `GameSession` is created but before the
first turn:

- **`IMatchupAnalyzer` / `LlmMatchupAnalyzer`** — narrative read of the
  player vs opponent stat block.
- **`IStakeGenerator` / `LlmStakeGenerator`** — novelist-style
  "psychological stake" character bible per character, appended to the
  character's assembled system prompt.
- **`CharacterDefinitionLoader`** — reads the shared character JSONs from
  disk.

All LLM I/O goes through `ILlmTransport` (provider-agnostic). The web tier
(`Pinder.GameApi`) builds these analyzers around a per-session transport and
calls them from `ActiveSession.SetupAsync`.

## Plain-Text Output Contract

**Both the matchup analysis and the per-character psychological stakes MUST
be emitted as plain prose. No markdown markers.**

Forbidden:

- Headings (`#`, `##`, `###`)
- Bold / italic (`**…**`, `__…__`, `*…*`, `_…_`)
- Bullet lists (`- `, `* `, `+ `)
- Numbered lists (`1. `, `2. ` …)
- Fenced code (```` ``` ````) and inline code (backticks)
- Blockquotes (`> `)

Paragraph breaks (blank lines between paragraphs) and inline punctuation are
preserved.

### Why

The pinder-web frontend renders both fields directly with
`whitespace-pre-wrap` into a plain `<p>` element. Markdown markers are not
parsed — they appear verbatim to the user (literal asterisks, hashes, etc.).
See `pinder-web/docs/modules/session-setup.md` for the full rendering
context.

### Defense in depth

1. **Prompt-level constraint.** The system + user prompts in
   `LlmMatchupAnalyzer` and `LlmStakeGenerator` explicitly forbid markdown
   formatting (issue
   [pinder-web#136](https://github.com/decay256/pinder-web/issues/136)).
2. **Backend sanitizer (web tier).** `Pinder.GameApi` runs the LLM output
   through a `MarkdownSanitizer` before storing it on the session, catching
   stray markers when the model ignores the prompt.
3. **This README.** A reminder for any future engine-side change to either
   analyzer.

If you add a new analyzer in this folder that surfaces LLM output to the
setup card, it inherits the same contract. Match the existing pattern:
forbid markdown in the prompt, and trust the web tier to sanitize.

## Reference

- pinder-web doc: [`docs/modules/session-setup.md`](https://github.com/decay256/pinder-web/blob/main/docs/modules/session-setup.md)
- Tracking issue: [pinder-web#138](https://github.com/decay256/pinder-web/issues/138)
- Markdown contract issue: [pinder-web#136](https://github.com/decay256/pinder-web/issues/136)
