# Issue 1333: Interest-Band Consumer Matrix

This document preserves the consumer matrix captured before #1334/#1335
changed behavior. The original matrix is historical characterization, not a
statement that every discrepancy remains active.

## Current State After #1334, #1335, and #1348

- #1334 propagates resolver-first typed pre-roll and final post-delivery
  relationship states. `DateeContext.InterestAfter` and
  `InterestAfterState` now represent the final value after shadow and
  horniness mutations, so the stale intermediate DATEE prompt discrepancy
  described below is resolved.
- #1335 replaces numeric DATEE narrative/resistance prompt bands with
  semantic YAML keys selected by canonical `InterestState`. Interest 15 uses
  `Interested`; interest 16 uses `VeryIntoIt`. The former interest-15
  contradiction described below is resolved.
- #1348 separates prior completed visible history from the current delivered
  message and commits the player/DATEE pair only after DATEE generation
  succeeds. This does not change band boundaries, but it is the current DATEE
  prompt-history contract for consumers in this matrix.

The remaining bespoke thresholds (for example horniness warmth 18 and player
strategy heuristics) remain deliberately separate from canonical relationship
bands unless a later issue changes them.

## Historical Baseline: Three Interest Values

| Name | Meaning | Current source |
| --- | --- | --- |
| Pre-roll interest/state | Value at the start of a turn before the selected option is resolved. | `GameSessionState.Interest` during `TurnOrchestrator.StartTurnAsync`; exposed through `DialogueContext.CurrentInterest` and `TurnStart.State`. |
| Post-roll, pre-delivery interest/state | Value after the option roll applies base/risk/combo/active-trap deltas, before shadow correction and horniness interest penalty. | `RollStageResult.InterestAfter` / `RollStageResult.StateAfter` in `RollResolutionStage`. |
| Final post-delivery interest/state | Value after delivery-time shadow correction and horniness interest penalty are applied. | `GameSessionState.Interest` after `TurnOrchestrator.ResolveTurnAsync`; exposed through `TurnResult.StateAfter`. |

At the time of #1333, the discrepancy was that `DateeResponseStage` received
both final `CurrentInterest` and stale intermediate `InterestAfter`. #1334
resolved this as described in the current-state section above.

## Canonical Boundaries

Canonical fallback bands in `InterestMeter.GetState` and Rules YAML are:

| Interest | Canonical state |
| --- | --- |
| 0 | `Unmatched` |
| 1-4 | `Bored` |
| 5-9 | `Lukewarm` |
| 10-15 | `Interested` |
| 16-20 | `VeryIntoIt` |
| 21-24 | `AlmostThere` |
| 25 | `DateSecured` |

`GameDefinition` implements `IRuleResolver` but deliberately returns `null` for `GetInterestState`, so production `GameDefinition` uses fallback bands through callers that permit fallback. `RuleBookResolver` can resolve the same canonical bands from `rules/extracted/rules-v3-enriched.yaml` and does not allow default fallback when data is missing.

## Historical Consumer Matrix

| Consumer | Active? | Timing | Current thresholds/states | Resolver/config shape | Classification | Impact and follow-up |
| --- | --- | --- | --- | --- | --- | --- |
| Rules YAML `rules/extracted/rules-v3-enriched.yaml` section 6 interest-state rules | Active in Rules tests and any host that wires `RuleBookResolver` | Generic lookup; may be used wherever a resolver is passed | Canonical `0`, `1-4`, `5-9`, `10-15`, `16-20`, `21-24`, `25` | Data-driven through `RuleBookResolver.GetInterestState` | Canonical band source | Keep as reference for #1334 typed state propagation. |
| `InterestMeter.GetState` | Active fallback | Any direct meter state query | Canonical fallback bands | No resolver; hardcoded fallback | Canonical fallback source | #1334 should centralize typed state values without inventing `InterestBand`. |
| `GameDefinition.GetInterestState` | Active resolver object, unresolved interest path | Any `GameDefinition` used as `IRuleResolver` | Always returns `null` | `AllowDefaultFallback == true` | Production fallback policy | Not a bug for this sprint; note that `GameDefinition` owns creative config, not conversation rule tables. |
| `TurnOrchestratorHelpers.ResolveInterestState` | Active | Pre-roll and final snapshot when orchestrator uses it | Resolver first, then `InterestMeter.GetState` | Honors `AllowDefaultFallback` | Canonical resolver boundary | #1334 should use this style for typed state propagation. |
| `TurnOrchestrator.StartTurnAsync` ghost check | Active | Pre-roll | `Bored` state triggers d4 ghost chance | Uses `ResolveInterestState` | Canonical state consumer | Correct resolver-first path. |
| `TurnOrchestrator.StartTurnAsync` advantage/disadvantage | Active | Pre-roll | `VeryIntoIt`/`AlmostThere` advantage; `Bored` disadvantage | Bypasses resolver via `InterestMeter.GrantsAdvantage/GrantsDisadvantage` | Resolver bypass with canonical thresholds | Architecture debt. Do not fold into #1335 unless #1334 needs complete custom resolver behavior. |
| `DialogueContext.CurrentInterest` | Active | Pre-roll | Raw integer only | Built by `StartTurnAsync` | Pre-roll API value | #1334 should pass typed pre-roll state beside the integer if prompts need semantic state. |
| `SessionDocumentBuilder.GetInterestLabel` for options prompt | Active | Pre-roll | Hardcoded canonical labels at `1`, `5`, `10`, `16`, `21`; `0` unmatched | Prompt code, not prompt YAML | Active prompt label, canonical but code-local | #1335 should decide whether this consumes typed pre-roll state or remains an integer formatter. |
| `RollResolutionStage.StateBefore` | Active | Pre-roll captured during resolution | Resolver-first canonical state | Uses `ResolveInterestState` | Canonical typed value | Good source for pre-roll state in #1334. |
| `RollResolutionStage.InterestAfter` / `StateAfter` | Active | Post-roll, pre-delivery | Resolver-first canonical state after base/risk/combo/active-trap interest | Uses `ResolveInterestState` | Intermediate typed value | #1334 must name this intermediate state explicitly; it is not final. |
| Shadow correction in `DeliveryStage` and `TurnOrchestrator.ResolveTurnAsync` | Active | Post-roll mutation before DATEE response | Caps positive delta to max `+1` when paired-shadow overlay applies | Not band-derived | Bespoke post-roll mutation | Characterization test pins stale `DateeContext.InterestAfter` after this mutation. |
| Horniness interest penalty in `DeliveryStage` and `TurnOrchestrator.ResolveTurnAsync` | Active | Post-roll mutation after shadow correction | Halves positive post-shadow delta with floor | Not band-derived | Bespoke post-roll mutation | Characterization test pins final interest can differ from intermediate interest. |
| `DateeResponseStage` `DateeContext.CurrentInterest` | Active | Final post-delivery | Final integer after shadow/horniness mutations | No typed state passed | Final value only | #1334 should add final typed state if prompts need it. |
| `DateeResponseStage` `DateeContext.InterestBefore` | Active | Pre-roll | Roll-stage pre-roll integer | No typed state passed | Pre-roll value only | Keep as separate field; do not recompute from final delta. |
| `DateeResponseStage` `DateeContext.InterestAfter` | Active | Currently post-roll, pre-delivery, despite the field name | Stale intermediate integer when shadow/horniness mutate later | No typed state passed | Misnamed/stale intermediate value | Blocking discrepancy for #1335; #1334 must clarify field names or add final fields before prompt reconciliation. |
| `DateeResponseStage` response delay | Active | Final post-delivery | Uses `state.Interest.Current` | No resolver; timing consumes integer | Bespoke timing consumer | Not a band consumer unless `TimingProfile` internally derives behavior. |
| `TurnResult.StateAfter` | Active API/result surface | Final post-delivery | Final snapshot with resolver-first state via orchestrator helper | Uses resolver in turn path | Canonical final output | This is the value API/frontends should trust for final state. |
| `TurnResult.NarrativeBeat` | Active, static | Post-roll, pre-delivery | Compares `rollStage.StateBefore` and `rollStage.StateAfter` | Uses stale intermediate roll-stage states | Stale intermediate consumer | Current active gameplay uses a static string; #1335 should either update timing or replace with configured prompt behavior. |
| `ILlmAdapter.GetInterestChangeBeatAsync` and `InterestChangeContext` | Implemented, no active production caller found | Would be post-change beat | `after > 15 && before <= 15`, `after < 8 && before >= 8`, terminal states | Prompt templates in YAML; method called by adapter only if host invokes it | Unused LLM path with bespoke thresholds | Do not revive in #1333/#1334. If #1335 uses it, update via explicit ticket and tests. |
| `SessionDocumentBuilder.BuildInterestChangeBeatPrompt` | Unused in active turn path | Would be transition beat | Above-15 warming, below-8 cooling, terminal overrides | YAML prompt catalog | Unused prompt path | Keep separate from DATEE emotional director unless deliberately revived. |
| `SessionDocumentBuilder.GetInterestNarrative` | Active DATEE prompt | Uses `DateeContext.InterestAfter`, currently intermediate | `1-4`, `5-9`, `10-14`, `15-20`, `21-24`, `25`, with `0` hardcoded unmatched | Prompt-template keys in `data/prompts/templates.yaml` | Active prompt prose band, not canonical mechanics | Interest 15 contradiction: mechanics say `Interested`; DATEE narrative says the higher 15-20 prose. #1335 owns reconciliation. |
| `SessionDocumentBuilder.GetResistanceBlock` | Active DATEE prompt | Uses `DateeContext.InterestAfter`, currently intermediate | `>=25`, `>=21`, `>=15`, `>=10`, `>=5`, `>=1`, else active disengagement | Prompt-template keys in `data/prompts/templates.yaml` | Active prompt resistance band, not canonical mechanics | Same 15 split as DATEE narrative; #1335 must decide if this remains bespoke or follows typed state. |
| DATEE failure reaction guidance | Active DATEE prompt | Current selected delivery tier | Failure tier only; no interest bands | Prompt-template keys in YAML | Bespoke delivery outcome consumer | Keep separate from interest reconciliation. |
| DATEE horniness reaction guidance | Active DATEE prompt | Currently uses `DateeContext.InterestAfter` | Warmth threshold `18`; below/above prompt keys; tier intensity by failure tier | Prompt-template keys in YAML plus code threshold | Bespoke threshold | Do not convert to canonical band without design decision; 18 is not the `VeryIntoIt` boundary. |
| Shadow taint prompt blocks | Active options and DATEE prompts | Current shadow values | Individual thresholds `>5` or `>6` | Prompt-template keys in YAML | Shadow-specific thresholds | Not an interest-band consumer. |
| `GameSession.CreateSnapshot()` public helper | Active test/debug/host helper | Current/final at call time | Direct `_interest.GetState()` | Resolver bypass | Resolver bypass with canonical fallback | Separate cleanup if custom resolver support must be complete. |
| `GameSessionRulesEvaluator` | Active helper/tests, not current orchestrator main path | Pre-roll/end-state helper | Resolver first for ghost; direct meter for terminal `0/25` | Partial resolver use | Mixed helper | Record as architecture debt; avoid duplicating new state logic here. |
| `GameStateSnapshot.GhostProbabilityPerTurn` | Active output surface | Snapshot time | `Bored` state -> `0.25`, otherwise `0` | Depends on snapshot state passed in | Derived from typed state | Good downstream-safe pattern: hosts do not need thresholds. |
| Scoring player agent context/tests | Active automated player strategy | Pre-turn snapshot | Uses supplied `InterestState`; near-win numeric bias `[19,24]`; bored bias by state | No resolver; consumes DTO/context | Mixed canonical state plus bespoke strategy thresholds | `19` and `24` are strategy heuristics, not band boundaries. |
| `session-runner/OutcomeProjector` | Active session-runner | Final/cutoff snapshot | Switches on `InterestState`; extra `interest >= 12` heuristic | No resolver; consumes snapshot state | Mixed canonical state plus bespoke projection heuristic | Do not use as prompt-band source. |
| `data/game-definition.yaml` GM prose | Active system prompt | Authorial prompt context | Canonical behavior table `1-4`, `5-9`, `10-15`, `16-20`, `21-24`, `25`; extra prose at `10+`, below `10`, etc. | Config, admin-editable in web | Active prose guidance | #1335 may need prompt reconciliation, but not by editing #1333. |
| `tests/Pinder.LlmAdapters.Tests/Fixtures/Issue1153/*` goldens | Active tests | Prompt output snapshots | Golden prompt text includes current configured prose and labels | Test fixtures | Regression pins | If #1335 changes prompt bands, update goldens intentionally. |
| Core API DTOs in `pinder-web` (`GameStateDtos`, `TurnDtos`) | Active downstream | Final state/result surfaces | Maps Core `GameStateSnapshot.State` to string; carries integers and deltas | No frontend derivation in Core; web maps Core values | API surface | #1334/#1335 should keep frontend consuming Core-supplied state, not deriving bands. |
| Replay/share DTOs in `pinder-web` | Active downstream | Final persisted/audit values | Owner mapper reads audit `interest.after`; public replay may leave legacy `interest_state` empty | Web-side mapping | API/frontend consumer | Follow-up belongs in web if state labels need richer replay data. |
| Admin content compilation in `pinder-web`/backend | Active config editing | Config load/save, not runtime turn banding | Can edit game-definition/progression/trap penalty values; no current interest-band table editing found | Admin config system | Non-consumer of runtime bands | Do not add frontend/admin band derivation from Core #1333. |

## Historical Threshold Classification Notes

| Value | Current uses | Classification |
| --- | --- | --- |
| `8` | Interest-change beat cooling threshold (`after < 8 && before >= 8`); Dread T3 default starting interest can become `8`. | Bespoke prompt/mechanic thresholds, not canonical band boundaries. |
| `15` | Canonical mechanics include `15` in `Interested`; DATEE prompt narrative/resistance starts higher-warmth prose at `15`; Denial reduction uses `interest >= 15`. | Known contradiction for prompt reconciliation; also a deliberate shadow-reduction mechanic. |
| `18` | Horniness warmth threshold; shadow T3 threshold value; Despair reduction uses `interest > 18`. | Bespoke thresholds; do not derive from `VeryIntoIt` without design. |
| `19` | Scoring player agent near-win bias lower bound. | Strategy heuristic, not a band boundary. |
| `20` | Canonical upper bound for `VeryIntoIt`; Overthinking reduction uses `interest >= 20`. | Canonical boundary in one place, deliberate mechanic threshold in another. |

## Historical Follow-Up Constraints for #1334 and #1335

- #1334 should propagate named typed states for pre-roll, post-roll/pre-delivery, and final post-delivery values. It should not create a new `InterestBand` type, promote prompt YAML into rules, or make frontends derive bands.
- #1334 should either rename or supplement `DateeContext.InterestAfter` so the DATEE prompt can distinguish intermediate and final values after shadow/horniness mutations.
- #1335 should reconcile prompt prose bands with canonical state only where the consumer is actually band-derived. It must preserve explicitly named bespoke thresholds like horniness warmth `18`, near-win `[19,24]`, and shadow-reduction mechanics.
- Resolver bypass cleanup is real but separate unless a later ticket requires complete custom resolver behavior for the emotional director.

## Historical Characterization Evidence

- `Issue1333_InterestBandConsumerCharacterizationTests` pins canonical Core boundaries and live stale `DateeContext.InterestAfter` after shadow plus horniness post-roll mutations.
- `Issue1333_InterestBandRuleCharacterizationTests` pins Rules YAML/`RuleBookResolver` boundary parity.
- `EngineInjectionBlockTests.Issue1333` pins the active LlmAdapters contradiction at interest `15`: options label remains canonical `Interested`, while DATEE prompt narrative/resistance uses the `15-20` prompt band.
