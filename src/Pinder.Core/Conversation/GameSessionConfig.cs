using System;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Optional configuration carrier for GameSession. All properties are nullable —
    /// null means "use the default behavior".
    /// </summary>
    public sealed class GameSessionConfig
    {
        /// <summary>Simulated game clock for time-based mechanics.</summary>
        public IGameClock? Clock { get; }

        /// <summary>Mutable shadow tracker for the player character.</summary>
        public SessionShadowTracker? PlayerShadows { get; }

        /// <summary>Mutable shadow tracker for the opponent character.</summary>
        public SessionShadowTracker? OpponentShadows { get; }

        /// <summary>Override the default starting interest value (normally 10).</summary>
        public int? StartingInterest { get; }

        /// <summary>Previous conversation opener for callback bonus calculation (per #162 resolution).</summary>
        public string? PreviousOpener { get; }

        /// <summary>
        /// Optional rule resolver for data-driven game constants.
        /// When non-null, GameSession uses this for §5/§6/§7/§15 lookups.
        /// When null or when a lookup returns null, hardcoded fallback is used.
        /// </summary>
        public IRuleResolver? Rules { get; }

        /// <summary>
        /// Global DC adjustment applied to every roll. Positive = harder (DC raised),
        /// negative = easier (DC lowered). Does not affect Nat 1 / Nat 20 detection.
        /// </summary>
        public int GlobalDcBias { get; }

        /// <summary>
        /// Optional RNG for the steering roll. When null, a new System.Random is used.
        /// Inject a seeded Random for deterministic test scenarios.
        /// </summary>
        public Random? SteeringRng { get; }

        /// <summary>
        /// Optional delivery instructions for horniness overlay tier lookups.
        /// When null, horniness overlay is skipped (no silent fallback — caller
        /// should supply instructions if horniness mechanic is desired).
        /// </summary>
        public object? StatDeliveryInstructions { get; }

        /// <summary>
        /// Optional dice roller override. When non-null, GameSession uses this instead of
        /// creating a new SystemRandomDiceRoller. Inject a seeded roller for deterministic
        /// test scenarios — ensures roll outcomes are reproducible across capture and replay.
        /// </summary>
        public IDiceRoller? DiceRoller { get; }

        /// <summary>
        /// Optional RNG used by <c>OptionFilterEngine.DrawRandomStats</c> to shuffle the
        /// stat pool each turn. Distinct from <see cref="SteeringRng"/> so that tests
        /// which inject a tightly-queued <see cref="Random"/> for steering rolls are
        /// not perturbed by unrelated stat-draw calls.
        /// When null, <c>OptionFilterEngine</c> falls back to a fresh <see cref="Random"/>
        /// (legacy non-deterministic behaviour). Inject a seeded RNG for test fixture
        /// reproducibility (issue #130).
        /// </summary>
        public Random? StatDrawRng { get; }

        /// <summary>
        /// Optional callback fired when a text-transform layer (Horniness /
        /// Shadow / Trap overlay) ran an LLM call but produced byte-identical
        /// output (#314). The callback receives <c>(turn, layer, beforeHash,
        /// afterHash)</c> so the host can emit a structured log line
        /// distinguishing "layer ran but no-op" from "layer didn't run at all".
        /// When null (default), no callback fires — same shape as today.
        ///
        /// Note: this is deliberately a callback rather than an ILogger so
        /// pinder-core stays free of <c>Microsoft.Extensions.Logging</c>
        /// dependencies. Hosts can wire structlog / ILogger / a custom sink
        /// at the call site.
        /// </summary>
        public Action<TextLayerNoopEvent>? OnTextLayerNoop { get; }

        public GameSessionConfig(
            IGameClock? clock = null,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? opponentShadows = null,
            int? startingInterest = null,
            string? previousOpener = null,
            IRuleResolver? rules = null,
            int globalDcBias = 0,
            Random? steeringRng = null,
            object? statDeliveryInstructions = null,
            IDiceRoller? diceRoller = null,
            Random? statDrawRng = null,
            Action<TextLayerNoopEvent>? onTextLayerNoop = null)
        {
            Clock = clock;
            PlayerShadows = playerShadows;
            OpponentShadows = opponentShadows;
            StartingInterest = startingInterest;
            PreviousOpener = previousOpener;
            Rules = rules;
            GlobalDcBias = globalDcBias;
            SteeringRng = steeringRng;
            StatDeliveryInstructions = statDeliveryInstructions;
            DiceRoller = diceRoller;
            StatDrawRng = statDrawRng;
            OnTextLayerNoop = onTextLayerNoop;
        }
    }
}
