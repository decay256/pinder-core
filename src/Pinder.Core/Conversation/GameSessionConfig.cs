using System;
using Pinder.Core.I18n;
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

        /// <summary>Mutable shadow tracker for the datee character.</summary>
        public SessionShadowTracker? DateeShadows { get; }

        /// <summary>Override the default starting interest value (normally 10).</summary>
        public int? StartingInterest { get; }

        /// <summary>Previous conversation opener for callback bonus calculation (per #162 resolution).</summary>
        public string? PreviousOpener { get; }

        /// <summary>
        /// Optional rule resolver for data-driven game constants.
        /// When non-null, GameSession uses this for §5/§6/§7/§15 lookups.
        /// When null, callers that need rules must register a resolver at the host boundary.
        /// </summary>
        public IRuleResolver? Rules { get; }

        /// <summary>
        /// Global DC adjustment applied to every main option roll. Positive = easier (DC lowered),
        /// negative = harder (DC raised). Does not affect Nat 1 / Nat 20 detection.
        /// </summary>
        public int GlobalDcBias { get; }

        /// <summary>
        /// Shadow DC adjustment applied to shadow checks. Positive = easier (DC lowered),
        /// negative = harder (DC raised). Fully independent of global DC bias.
        /// </summary>
        public int ShadowDcBias { get; }

        /// <summary>
        /// Horniness DC adjustment applied to horniness checks. Positive = easier (DC lowered),
        /// negative = harder (DC raised). Fully independent of global DC bias.
        /// </summary>
        public int HorninessDcBias { get; }

        /// <summary>
        /// Optional RNG for the steering roll. When null, a new System.Random is used.
        /// Inject a seeded Random for deterministic test scenarios.
        /// </summary>
        public Random? SteeringRng { get; }

        /// <summary>
        /// Optional delivery instructions for horniness overlay tier lookups.
        /// When null, horniness overlay is skipped (no silent fallback — caller
        /// should supply instructions if horniness mechanic is desired).
        /// Typed as <see cref="IStatDeliveryInstructionProvider"/> (implemented by
        /// the adapter-layer StatDeliveryInstructions class) rather than
        /// <c>object?</c>, so the engine calls these members at compile time
        /// instead of via reflection (#709 audit fix).
        /// </summary>
        public IStatDeliveryInstructionProvider? StatDeliveryInstructions { get; }

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

        /// <summary>
        /// Optional callback fired when shadow filtering changes the option/stat pool (#1218).
        /// When null, no callback is fired.
        /// </summary>
        public Action<ShadowFilterTraceEvent>? OnShadowFilterTrace { get; }

        /// <summary>
        /// Optional callback fired when a rule resolution occurs (#1219).
        /// When null, no callback is fired.
        /// </summary>
        public Action<RuleResolutionTraceEvent>? OnRuleResolution { get; }

        /// <summary>
        /// Optional callback fired for operational diagnostics such as transient
        /// LLM failures that the engine can recover from. When null, no
        /// callback is fired. Hosts can bridge this to ILogger, Unity logs,
        /// structured telemetry, or test-controlled capture.
        /// </summary>
        public Action<OperationalDiagnosticEvent>? OnDiagnostic { get; }

        /// <summary>
        /// Consequence catalogue for engine-side population of
        /// Consequence fields on roll/shadow/horniness result DTOs (#976).
        /// When null, engines leave <c>Consequence</c> null.
        /// </summary>
        public IConsequenceCatalog? ConsequenceCatalog { get; }

        /// <summary>Max dialogue options configured in GameDefinition. Null means default 3.</summary>
        public int? MaxDialogueOptions { get; }

        /// <summary>Max delivery words configured in GameDefinition. Null means default 80.</summary>
        public int? MaxDeliveryWords { get; }

        /// <summary>Whether archetype content is injected into prompt surfaces.</summary>
        public bool ArchetypesEnabled { get; }

        /// <summary>Multiplier applied to positive interest gains when a trap is active.</summary>
        public double ActiveTrapInterestPenalty { get; }

        /// <summary>Optional override for Hunger For Intimacy.</summary>
        public int HungerForIntimacy { get; }

        /// <summary>Optional override for Terror Of Rejection.</summary>
        public int TerrorOfRejection { get; }

        public GameSessionConfig(
            IGameClock? clock = null,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? dateeShadows = null,
            int? startingInterest = null,
            string? previousOpener = null,
            IRuleResolver? rules = null,
            int globalDcBias = 0,
            int shadowDcBias = 0,
            int horninessDcBias = 0,
            Random? steeringRng = null,
            IStatDeliveryInstructionProvider? statDeliveryInstructions = null,
            IDiceRoller? diceRoller = null,
            Random? statDrawRng = null,
            Action<TextLayerNoopEvent>? onTextLayerNoop = null,
            Action<ShadowFilterTraceEvent>? onShadowFilterTrace = null,
            IConsequenceCatalog? consequenceCatalog = null,
            int? maxDialogueOptions = null,
            int? maxDeliveryWords = null,
            bool archetypesEnabled = false,
            Action<RuleResolutionTraceEvent>? onRuleResolution = null,
            double activeTrapInterestPenalty = -0.25,
            int hungerForIntimacy = 0,
            int terrorOfRejection = 0,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null)
        {
            Clock = clock;
            PlayerShadows = playerShadows;
            DateeShadows = dateeShadows;
            StartingInterest = startingInterest;
            PreviousOpener = previousOpener;
            Rules = rules;
            GlobalDcBias = globalDcBias;
            ShadowDcBias = shadowDcBias;
            HorninessDcBias = horninessDcBias;
            SteeringRng = steeringRng;
            StatDeliveryInstructions = statDeliveryInstructions;
            DiceRoller = diceRoller;
            StatDrawRng = statDrawRng;
            OnTextLayerNoop = onTextLayerNoop;
            OnShadowFilterTrace = onShadowFilterTrace;
            OnRuleResolution = onRuleResolution;
            OnDiagnostic = onDiagnostic;
            ConsequenceCatalog = consequenceCatalog;
            MaxDialogueOptions = maxDialogueOptions;
            MaxDeliveryWords = maxDeliveryWords;
            ArchetypesEnabled = archetypesEnabled;
            ActiveTrapInterestPenalty = activeTrapInterestPenalty;
            HungerForIntimacy = hungerForIntimacy;
            TerrorOfRejection = terrorOfRejection;
        }
    }
}
