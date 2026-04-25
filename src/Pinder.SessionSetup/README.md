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

## Streaming Overloads

Both `IMatchupAnalyzer` and `IStakeGenerator` ship two overloads:

| Overload                              | Transport(s) required                                | On LLM failure        |
|---------------------------------------|------------------------------------------------------|-----------------------|
| `AnalyzeMatchupAsync` / `GenerateAsync` | `ILlmTransport` only                                | **Swallows** — returns `null` (matchup) or `""` (stake) |
| `StreamMatchupAsync` / `StreamStakeAsync` | `ILlmTransport` **and** `IStreamingLlmTransport`   | **Throws** `LlmTransportException` (or propagates `OperationCanceledException`) |

### Contract

```csharp
IAsyncEnumerable<string> StreamMatchupAsync(
    CharacterProfile player,
    CharacterProfile opponent,
    CancellationToken cancellationToken = default);

IAsyncEnumerable<string> StreamStakeAsync(
    string characterDisplayName,
    string assembledSystemPrompt,
    CancellationToken cancellationToken = default);
```

Each yielded `string` is a **raw text fragment** as it arrived from the
upstream LLM — typically one Anthropic `content_block_delta` or one
OpenAI-compatible `choices[0].delta.content` chunk. Empty fragments are
filtered out by the transport before yielding, but partial words are
**not** — callers must concatenate fragments themselves and apply any
sanitization at stream close.

### Throw-on-failure semantics

The streaming overloads deliberately **do not** mirror the swallow
behaviour of the non-streaming overloads:

- A transport / network / SSE-parse error during the stream surfaces as
  `LlmTransportException` (out of `MoveNextAsync`, in the consumer's
  `await foreach`). This lets the web-tier `ActiveSession.SetupAsync`
  attribute the failure to a specific stage (`matchup_llm_failed` /
  `stake_llm_failed`) and emit a stable `error` SSE event — something the
  swallow-and-return-null shape can't express.
- `OperationCanceledException` is propagated unchanged so that callers
  can wire client disconnect into the cancellation token.
- Constructing the streaming overload without an `IStreamingLlmTransport`
  throws `InvalidOperationException` immediately on first iteration.

### Plain-text contract

The streaming overloads honour the **same plain-text output contract**
documented above. Markdown markers must be forbidden in the prompt and
stripped on the consuming side (`MarkdownSanitizer` in the web tier runs
on the concatenated buffer at stage close). Streaming does **not**
relax the contract — the model is still expected to emit plain prose;
the sanitizer is a defense-in-depth net for stray markers.

### Mapping to the SSE wire format

The web tier translates these `IAsyncEnumerable<string>` overloads into
the SSE event sequence emitted on
`GET /sessions/{id}/setup-status/stream`:

- `StreamMatchupAsync` / `StreamStakeAsync` first yield  → `stage_start`
- each yielded fragment                                  → `delta`
- enumerator completes                                   → `stage_done`
  (with the sanitized full text)
- all three stages complete                              → `complete`
- `LlmTransportException` mid-stream                     → `error` (with
  the matching stable code: `matchup_llm_failed` for the matchup stage,
  `stake_llm_failed` for either stake stage)

See `pinder-web/docs/modules/session-setup.md §7` for the full event
schema and reconnect semantics. The engine library is intentionally
unaware of SSE — these overloads stay a clean `IAsyncEnumerable<string>`
contract that any consumer (web tier, CLI, tests) can drive.

## Reference

- pinder-web doc: [`docs/modules/session-setup.md`](https://github.com/decay256/pinder-web/blob/main/docs/modules/session-setup.md)
- Tracking issue: [pinder-web#138](https://github.com/decay256/pinder-web/issues/138)
- Markdown contract issue: [pinder-web#136](https://github.com/decay256/pinder-web/issues/136)
- Streaming protocol issues: [pinder-web#148](https://github.com/decay256/pinder-web/issues/148) (epic), #151–#156
