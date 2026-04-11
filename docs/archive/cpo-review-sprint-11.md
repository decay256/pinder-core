# CPO Strategic Review — Sprint 11

## Alignment: ⚠️
This sprint effectively supports the product vision by focusing on developer UX and playtest legibility. Exposing data (like Matchup Analysis, HTTP traffic dumps, and combo logic) will accelerate iteration on the game rules and LLM persona tuning. However, the architect's implementation strategy has deviated significantly from several key issue requirements, grouping nuanced game logic into overly simplistic bins and missing essential debug outputs. 

## Data Flow Traces
### MatchupAnalyzer
- Session run initialized → MatchupAnalyzer instantiated → AnalyzeAsync(player, opponent) → Sends zero-shot MessagesRequest to Anthropic → Formats text output
- Required fields: Player Stats, Player Bio, Opponent Stats, Opponent Bio
- Flow is correct: `CharacterProfile` provides all necessary stats and bios to the prompt.

### DebugLoggingHandler
- `--debug` flag provided → Program injects `DebugLoggingHandler` into `AnthropicLlmAdapter` → intercepts `SendAsync`
- Required fields: Request body, response body, usage metadata
- ⚠️ Missing: The `usage` block from Anthropic responses (containing `cache_creation_input_tokens`, `cache_read_input_tokens`) must be aggregated per-call into `session-summary.json`. The architect's plan ignores token extraction entirely.

## Unstated Requirements
- When the UI displays "Intended vs Delivered" diffs, the user expects visually obvious formatting (strikethrough/italics) for the additions so they don't have to mentally diff two paragraphs.
- A "Matchup Analysis" that evaluates character stats will require accurate descriptions of those stats and potential combos; if the LLM hallucinating mechanics becomes an issue, the prompt may need some of the rule definitions injected.

## Domain Invariants
- The prompt caching mechanism must be monitored closely for costs; caching token accumulation data is a hard invariant for any debug logging feature.
- Success margins must strictly reflect the game rules. A Nat 20 is functionally distinct from a standard success and must have a unique LLM prompt directive.

## Gaps
- **Missing**: Delivery instruction tiers. Issue #530 demands 5 tiers (1-4, 5-9, 10-14, 15+, Nat20) but the architect planned 3 tiers (1-4, 5-9, 10+), obliterating the impact of exceptional rolls.
- **Missing**: Combo description display locations. The Architect planned to show the combo description *only* when the combo triggers, missing the crucial requirement to show it inline within the options list to help the player decide.
- **Missing**: Delivered message diff formatting. Issue #486 explicitly mandates `~~strikethrough~~` for fail additions and `*italic*` for success additions. The Architect's strategy only prints the before/after text without highlighting changes.
- **Missing**: The `session-summary.json` file aggregating cache token usage was completely dropped from the debug logging implementation.

## Recommendations
1. Revise the `DebugLoggingHandler` to intercept the `usage` JSON block from Anthropic responses and append it to an accumulating `session-summary.json`.
2. Expand the `GetSuccessDeliveryInstruction` method and prompt templates to support all 5 margin tiers as defined in #530.
3. Update `Program.cs` to print the combo description within the option list output, not just upon combo resolution.
4. Add basic text diff highlighting logic to `Program.cs` for Issue #486, wrapping additions in markdown format based on the roll tier.
