# Pinder.SessionSetup

Pre-session helpers that run after a `GameSession` is created but before the
first turn:

- **`IStakeGenerator` / `LlmStakeGenerator`** — novelist-style
  "psychological stake" character bible per character, appended to the
  character's assembled system prompt.
- **`IOutfitDescriber` / `LlmOutfitDescriber`** — short visual scene
  description used as the turn-0 scene-setting entry. Runs in parallel
  with stake generation.
- **`CharacterDefinitionLoader`** — reads the shared character JSONs from
  disk.

> **Setup-trim phase 1 (#827):** The `IMatchupAnalyzer` /
> `LlmMatchupAnalyzer` and `IMatchupSummarizer` / `LlmMatchupSummarizer`
> stages were removed. Display-only output that was never threaded into
> any subsequent prompt — its cost/value tradeoff did not justify keeping
> it. Historical audit/cost rows still reference the old `matchup_analysis`
> / `matchup_summary` `LlmPhase` values as untyped strings; those constants
> have been deleted from `LlmPhase` and must not be re-introduced.

All LLM I/O goes through `ILlmTransport` (provider-agnostic). The web tier
(`Pinder.GameApi`) builds these helpers around a per-session transport and
calls them from `ActiveSession.SetupAsync`.

## Output Contracts

### Outfit description — plain prose

**The outfit description MUST be emitted as plain prose. No markdown
markers.**

Forbidden:

- Headings (`#`, `##`, `###`)
- Bold / italic (`**…**`, `__…__`, `*…*`, `_…_`)
- Bullet lists (`- `, `* `, `+ `)
- Numbered lists (`1. `, `2. ` …)
- Fenced code (```` ``` ````) and inline code (backticks)
- Blockquotes (`> `)

### Psychological stake — markdown bullet list (#949)

**The per-character psychological stake MUST be emitted as a 15-item
markdown bullet list, one `- `-prefixed bullet per stem-completion.**
From #949 onward, the SPA renders the stake as a bullet list and the
`MarkdownSanitizer` preserves `- ` line prefixes unchanged.

Still forbidden for the stake field:

- Headings (`#`, `##`, `###`)
- Bold / italic emphasis
- Nested bullets
- Numbered lists
- Fenced code, inline code, blockquotes

Paragraph breaks (blank lines between paragraphs) and inline punctuation are
preserved.

### Why

The pinder-web frontend renders the stake field directly with
`whitespace-pre-wrap` into a plain `<p>` element. Markdown markers are not
parsed — they appear verbatim to the user (literal asterisks, hashes, etc.).
See the historical `pinder-web/docs/modules/session-setup.md` for context.

### Defense in depth

1. **Prompt-level constraint.** The outfit prompt forbids all markdown
   (issue
   [pinder-web#136](https://github.com/decay256/pinder-web/issues/136));
   the stake prompt requests a `- `-prefixed bullet list and forbids
   every other marker (#949).
2. **Backend sanitizer (web tier).** `Pinder.GameApi` runs the LLM output
   through a `MarkdownSanitizer` before storing it on the session. Per
   #949 the sanitizer preserves `- ` line prefixes so the stake bullets
   survive, while still stripping headings, emphasis, code fences, and
   blockquotes.
3. **This README.** A reminder for any future engine-side change to either
   helper.

If you add a new helper in this folder that surfaces LLM output to the
setup card, it inherits the same contract. Match the existing pattern:
forbid markdown in the prompt, and trust the web tier to sanitize.

## Streaming Overloads

`IStakeGenerator` ships two overloads:

| Overload                              | Transport(s) required                                | On LLM failure        |
|---------------------------------------|------------------------------------------------------|-----------------------|
| `GenerateAsync`                       | `ILlmTransport` only                                | **Swallows** — returns `""` |
| `StreamStakeAsync`                    | `ILlmTransport` **and** `IStreamingLlmTransport`   | **Throws** `LlmTransportException` (or propagates `OperationCanceledException`) |

### Contract

```csharp
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
  attribute the failure to a specific stage (`stake_llm_failed`) and
  emit a stable `error` SSE event — something the swallow-and-return-null
  shape can't express.
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

The web tier translates `IAsyncEnumerable<string>` into the SSE event
sequence emitted on `GET /sessions/{id}/setup-status/stream`:

- `StreamStakeAsync` first yield  → `stage_start`
- each yielded fragment           → `delta`
- enumerator completes            → `stage_done` (with the sanitized full text)
- all stages complete             → `complete`
- `LlmTransportException` mid-stream → `error` (with the matching stable
  code: `stake_llm_failed` for either stake stage)

The engine library is intentionally
unaware of SSE — these overloads stay a clean `IAsyncEnumerable<string>`
contract that any consumer (web tier, CLI, tests) can drive.

## Reference

- Tracking issue: [pinder-web#138](https://github.com/decay256/pinder-web/issues/138)
- Markdown contract issue: [pinder-web#136](https://github.com/decay256/pinder-web/issues/136)
- Streaming protocol issues: [pinder-web#148](https://github.com/decay256/pinder-web/issues/148) (epic), #151–#156
- Setup-trim phase 1 (matchup removal): [pinder-core#827](https://github.com/decay256/pinder-core/issues/827)
