# Architecture Review: Sprint 11 — Session Runner UX

## 1. Architecture Overview

Pinder.Core is a stateless C# (.NET Standard 2.0) RPG engine. `session-runner` is a .NET 8 console application that acts as the host, orchestrating turns by feeding inputs into `GameSession` and evaluating AI outputs via `AnthropicLlmAdapter`. The current architecture remains strictly stateless and focused on executing independent turns. No structural changes to the core game engine or the fundamental flow of LLM adapters are introduced in this sprint. 

This sprint is focused entirely on the UX and developer tooling in `session-runner/` (playtest formatting, LLM matchup analysis, HTTP debug dumps) and targeted prompt improvements in `Pinder.LlmAdapters` (success delivery scaling).

**Components being extended:**
- `session-runner/PlaytestFormatter`: Gains combo sequence explanation logic.
- `session-runner/Program`: Gains matchup analysis wiring, debug logging interceptor, intended vs delivered UI diffs, bio formatting fixes, and risk color restoration.
- `Pinder.LlmAdapters/PromptTemplates` & `SessionDocumentBuilder`: Success delivery instructions are split out into margin-specific subsets to enforce adherence, preventing the LLM from hallucinating between delivery tiers.

**Implicit assumptions:**
- `AnthropicClient` and its DTOs (`MessagesRequest`, etc.) are `public`, allowing `session-runner` to leverage them for zero-shot LLM queries (like the matchup analysis).
- Modifying `HttpClient` inside `session-runner` (via a `DelegatingHandler`) is the correct architectural way to intercept and log LLM traffic without mutating the `Pinder.LlmAdapters` layer.
- `TurnResult.ComboTriggered` returns a string name (e.g. "The Reveal") which the UI must map to a descriptive sequence, rather than polluting the core rules engine with UI presentation logic.

---

## 2. Separation of Concerns Map

- **PlaytestFormatter** (`session-runner`)
  - Responsibility: 
    - Format scoring tables
    - Map combo names to descriptive sequences
  - Interface: 
    - `FormatScoreTable()`
    - `FormatReasoningBlock()`
    - `GetComboSequenceDescription(string comboName)`
  - Must NOT know: 
    - HTTP requests
    - LLM adapters

- **MatchupAnalyzer** (`session-runner`)
  - Responsibility: 
    - Execute a zero-shot prompt summarizing player vs opponent stats
  - Interface: 
    - `AnalyzeAsync(CharacterProfile player, CharacterProfile opponent)`
  - Must NOT know: 
    - GameSession state
    - Turn loop orchestration

- **DebugLoggingHandler** (`session-runner`)
  - Responsibility: 
    - Intercept HTTP traffic and dump raw JSON requests/responses to disk
  - Interface: 
    - `DelegatingHandler.SendAsync` override
  - Must NOT know: 
    - Pinder game logic
    - Character definitions

- **PromptTemplates** (`Pinder.LlmAdapters`)
  - Responsibility: 
    - Provide exact instruction text for the LLM
  - Interface: 
    - `GetSuccessDeliveryInstruction(int margin)`
  - Must NOT know: 
    - HTTP transport
    - Host-level UX

---

## 3. Interface Definitions

### Issue #526: Combo Sequence Explanations
**Component**: `session-runner/PlaytestFormatter.cs`
**Contract**: Add a `public static string GetComboSequenceDescription(string comboName)` method that returns hardcoded explanations of the stat sequences (e.g., "You played Charm last turn, then Honesty this turn — the sequence earns +1 bonus interest."). In `Program.cs`, when a combo triggers, output this mapped description immediately after the combo announcement blockquote.

### Issue #527: Bio as Bold Italic Paragraph
**Component**: `session-runner/Program.cs`
**Contract**: Remove the `Bio` row from the markdown Characters table. Instead, inject the bio text immediately before the table using the exact format: `***{Player} bio:*** *{Bio text}*`.

### Issue #528: LLM Matchup Analysis
**Component**: `session-runner/MatchupAnalyzer.cs` (New)
**Contract**: 
```csharp
public sealed class MatchupAnalyzer
{
    public MatchupAnalyzer(AnthropicOptions options, HttpClient? httpClient = null);
    public Task<string> AnalyzeAsync(CharacterProfile player, CharacterProfile opponent);
}
```
Constructs a `MessagesRequest` with a system prompt instructing the LLM to write a 2-3 sentence strategic analysis of the match (comparing their stats and bios). In `Program.cs`, await this method and print its output beneath the Characters section under a `### Matchup Analysis` header.

### Issue #529: Risk Colors
**Component**: `session-runner/Program.cs`
**Contract**: Find the inline string assignment (`string riskColor = need <= 5 ? ...`) and replace it with a call to the existing static helper method `RiskLabel(need)`. This correctly restores the `🟢🟡🟠🔴` emoji prefixes.

### Issue #530: Success Delivery Scaling
**Component**: `Pinder.LlmAdapters/PromptTemplates.cs` & `SessionDocumentBuilder.cs`
**Contract**: The monolithic `SuccessDeliveryInstruction` string must be converted to a method `public static string GetSuccessDeliveryInstruction(int beatDcBy)` that returns *only* the specific margin tier instruction (1-4, 5-9, or 10+) along with the base phrasing rules. `SessionDocumentBuilder.BuildDeliveryPrompt` calls this method instead of appending the entire rubric.

### Issue #534: --debug Flag Full LLM Dumps
**Component**: `session-runner/DebugLoggingHandler.cs` (New)
**Contract**: 
```csharp
public sealed class DebugLoggingHandler : DelegatingHandler
{
    public DebugLoggingHandler(HttpMessageHandler innerHandler, string debugDirectory);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
```
When `--debug` is present, `Program.cs` creates a `debug/` directory, instantiates an `HttpClient` wrapped with this handler, and passes it to both `AnthropicLlmAdapter` and `MatchupAnalyzer`. The handler writes all requests to `{counter:D3}-request.json` and responses to `{counter:D3}-response.json`.

### Issue #486: Intended vs Delivered Diff
**Component**: `session-runner/Program.cs`
**Contract**: In the turn printout section where `result.DeliveredMessage` is displayed: check `if (result.DeliveredMessage.Trim() != chosen.IntendedText.Trim() && chosen.IntendedText != "...")`. If there is a diff, prepend the blockquote with `*Intended: "{chosen.IntendedText}"*` followed by a newline and `*Delivered:*`.

---

## 4. Implementation Strategy

1. **Bug Fixes First**: Start with #529 (Risk Colors) and #530 (Success Delivery Scaling). These are isolated, correct obvious flaws, and are easy to verify. 
2. **UX Refinements**: Next implement #526 (Combo Explanation), #527 (Bio Format), and #486 (Diff Display). Since these all touch `Program.cs` output printing, they can be implemented cohesively.
3. **Core Additions**: Implement #528 (Matchup Analysis) by adding the new `MatchupAnalyzer` class. Since it relies on the Anthropic Client, having this isolated keeps `Program.cs` clean.
4. **Tooling / Debug**: Finish with #534 (`DebugLoggingHandler`). Because it wraps `HttpClient`, it needs to intercept traffic from both the core game loop and the new Matchup Analysis call. Implementing it last ensures all LLM calls are captured.

**Tradeoffs:**
We are explicitly choosing to hardcode Combo Sequence descriptions in the UI (`PlaytestFormatter`) rather than pushing string descriptions down into `Pinder.Core.Conversation.ComboResult`. This respects the prototype phase constraint of leaving core module APIs untouched, isolating presentation concerns entirely within the playtest runner.

## 5. Sprint Plan Changes

None required. The current issues are atomic, fully independent of core gameplay logic, and correctly scoped.

**VERDICT: PROCEED**
