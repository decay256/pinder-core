using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Per-turn context the strategy uses to produce the arc directive.
    /// </summary>
    public sealed class ArcTurnContext
    {
        public int TurnNumber { get; }      // 1-based
        public int TotalTurns { get; }
        public bool PolarityOn { get; }

        public ArcTurnContext(int turnNumber, int totalTurns, bool polarityOn)
        {
            TurnNumber = turnNumber;
            TotalTurns = totalTurns;
            PolarityOn = polarityOn;
        }

        /// <summary>Phase fraction through the conversation, 0.0 .. 1.0.</summary>
        public double Phase => TotalTurns <= 1 ? 1.0 : (double)(TurnNumber - 1) / (TotalTurns - 1);
    }

    /// <summary>
    /// What a strategy emits for one turn: the text injected into the
    /// CONVERSATION ARC slot, plus annotation fields for the transcript.
    /// </summary>
    public sealed class ArcDirective
    {
        /// <summary>Text injected into <c>conversationArcProgression</c>.</summary>
        public string ArcText { get; }

        /// <summary>Human-readable soft-bias label for the annotation.</summary>
        public string SoftBias { get; }

        /// <summary>Register guidance label for the annotation.</summary>
        public string Register { get; }

        /// <summary>Active beat / phase label for the annotation.</summary>
        public string BeatLabel { get; }

        public ArcDirective(string arcText, string softBias, string register, string beatLabel)
        {
            ArcText = arcText;
            SoftBias = softBias;
            Register = register;
            BeatLabel = beatLabel;
        }
    }

    /// <summary>
    /// The experiment surface. The arc-shape is a strategy, NOT hardcoded, so
    /// #842 can A/B the winning shape. Implementations: ingestion (primary
    /// hypothesis) and romcom (control).
    /// </summary>
    public interface IArcStrategy
    {
        /// <summary>Strategy id (e.g. "ingestion", "romcom").</summary>
        string Name { get; }

        /// <summary>One-time header describing the plan (printed before turns).</summary>
        string DescribePlan();

        /// <summary>Produce the arc directive for a given turn.</summary>
        ArcDirective DirectiveFor(ArcTurnContext ctx);
    }

    /// <summary>
    /// PRIMARY HYPOTHESIS. Injects ALL confessions (pre-summarized: text + depth)
    /// into the CONVERSATION ARC slot every turn, plus a SOFT bias (early -&gt;
    /// lighter material, late -&gt; deeper) and register-from-depth guidance.
    /// There is NO per-turn selector LLM call: the character model itself
    /// reaches opportunistically. Rom-com curve is soft bias only, never a
    /// schedule.
    /// </summary>
    public sealed class IngestionArcStrategy : IArcStrategy
    {
        private readonly ConfessionMenu _menu;
        public string Name => "ingestion";

        public IngestionArcStrategy(ConfessionMenu menu) => _menu = menu;

        public string DescribePlan()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Arc shape: **ingestion** (primary hypothesis).");
            sb.AppendLine("All confessions injected every turn; the model self-selects opportunistically.");
            sb.AppendLine("Soft bias: early turns nudge toward Light material, late turns toward Raw.");
            sb.AppendLine("Register derives from the depth of whatever the model reaches for.");
            return sb.ToString();
        }

        public ArcDirective DirectiveFor(ArcTurnContext ctx)
        {
            double phase = ctx.Phase;
            // Soft bias: which depth band is "in season" right now. Never a
            // schedule — the model may derail and that is observable.
            ConfessionDepth seasoned =
                phase < 0.34 ? ConfessionDepth.Light :
                phase < 0.67 ? ConfessionDepth.Tender :
                               ConfessionDepth.Raw;

            string softBias =
                $"It is roughly {Math.Round(phase * 100)}% of the way through this conversation. "
                + $"Without forcing it, you are now more willing to reach for **{seasoned}** material "
                + "if a moment invites it. Earlier you stayed lighter; you do not have to escalate, "
                + "but heavier truths are no longer off-limits.";

            string register =
                $"If you do reach for {seasoned} material, your register should be: "
                + ConfessionMenu.RegisterFor(seasoned) + ".";

            string polarity = ctx.PolarityOn
                ? "\nLet the emotional temperature SHIFT this phase — if it was warming, allow a flicker "
                  + "of doubt or self-protection; if it was guarded, allow a small thaw. Do not announce the shift."
                : "";

            string arcText =
                "== Opportunistic confession arc ==\n\n"
                + _menu.RenderIngestibleBlock() + "\n"
                + "Guidance:\n- " + softBias + "\n- " + register + polarity + "\n"
                + "- Never dump the list. At most touch one confession, and only if it fits. "
                + "Deflecting or staying surface is fine and realistic.";

            return new ArcDirective(
                arcText,
                softBias: $"{seasoned}-seasoned (phase {Math.Round(phase * 100)}%)",
                register: ConfessionMenu.RegisterFor(seasoned),
                beatLabel: $"ingestion · {seasoned} window");
        }
    }

    /// <summary>
    /// A/B CONTROL. Imposes a rom-com 7-beat spine, proportionally spread over
    /// the turn count. Each beat carries a directive injected into the same
    /// slot. With polarity on, the beat's authored direction-of-change is
    /// injected; with polarity off, the beat is stated without an enforced
    /// direction.
    /// </summary>
    public sealed class RomComArcStrategy : IArcStrategy
    {
        public string Name => "romcom";

        // Classic 7-beat rom-com spine. Each beat: label, directive, polarity.
        private static readonly (string Label, string Directive, string Polarity)[] Beats =
        {
            ("Meet-cute",      "Open with spark and friction. Charm, but keep a wall up.",                 "warmth rising"),
            ("Banter",         "Trade quick wit. Test each other. Enjoy the volley.",                       "playful → intrigued"),
            ("First crack",    "Let one small genuine thing slip past the performance.",                    "guard → exposure"),
            ("Pull-back",      "Get spooked by the closeness. Retreat a half-step, deflect.",               "warmth → doubt"),
            ("Real talk",      "Drop a real, costly truth. Be briefly unguarded.",                          "doubt → trust"),
            ("Wobble",         "A misread or fear nearly derails it. Tension spikes.",                      "trust → fear"),
            ("Landing",        "Arrive somewhere honest — closer, or cleanly apart.",                       "fear → resolution"),
        };

        public string DescribePlan()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Arc shape: **romcom** (A/B control).");
            sb.AppendLine("Imposed 7-beat spine, proportionally spread over the turn count:");
            for (int i = 0; i < Beats.Length; i++)
                sb.AppendLine($"  {i + 1}. {Beats[i].Label} — {Beats[i].Directive}  (polarity: {Beats[i].Polarity})");
            return sb.ToString();
        }

        public ArcDirective DirectiveFor(ArcTurnContext ctx)
        {
            // Proportional phase mapping: not 1:1. Map phase 0..1 onto beat 0..6.
            int beatIdx = (int)Math.Floor(ctx.Phase * Beats.Length);
            if (beatIdx >= Beats.Length) beatIdx = Beats.Length - 1;
            var beat = Beats[beatIdx];

            string polarityLine = ctx.PolarityOn
                ? $"\nDirection of change this beat: **{beat.Polarity}** — let that shift drive the emotional move."
                : "";

            string arcText =
                $"== Rom-com beat {beatIdx + 1}/{Beats.Length}: {beat.Label} ==\n\n"
                + beat.Directive
                + polarityLine
                + "\n\nThis is a soft target, not a cage. If the moment pulls elsewhere, follow it — "
                + "derailing is allowed and need not be corrected.";

            string softBias = $"beat {beatIdx + 1}/{Beats.Length}: {beat.Label}";
            string register = ctx.PolarityOn ? beat.Polarity : "(no enforced direction)";

            return new ArcDirective(arcText, softBias, register, beatLabel: $"romcom · {beat.Label}");
        }
    }
}
