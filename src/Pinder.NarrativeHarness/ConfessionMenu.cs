using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Reusable depth band for a single confession. Drives the register the
    /// character is nudged into when it reaches for that confession:
    /// far/light material -> guarded/playful register; near/deep material ->
    /// raw/short register. Issue #842 consumes this same enum.
    /// </summary>
    public enum ConfessionDepth
    {
        /// <summary>Mild embarrassment, surface anecdote. Far from the wound.</summary>
        Light = 0,
        /// <summary>Real exposure but still narratable. Mid distance.</summary>
        Tender = 1,
        /// <summary>Shame / core vulnerability. Near the wound. Raw register.</summary>
        Raw = 2,
    }

    /// <summary>
    /// One parsed confession from a character's 15-line
    /// <c>PsychologicalStake</c> bullet list, enriched with a derived depth
    /// score and a one-line theme. This is the structured, pre-summarized
    /// "menu entry" that gets ingested into the CONVERSATION ARC slot so the
    /// character model can self-select opportunistically.
    /// </summary>
    public sealed class ConfessionEntry
    {
        /// <summary>1-based index within the character's stake list.</summary>
        public int Index { get; }

        /// <summary>The raw confession sentence (bullet text, dash stripped).</summary>
        public string Text { get; }

        /// <summary>One-line theme summary derived from the confession text.</summary>
        public string Theme { get; }

        /// <summary>Named/dated anchors detected in the line (people, dates, places).</summary>
        public IReadOnlyList<string> Anchors { get; }

        /// <summary>Heuristic numeric depth score (higher = nearer the wound).</summary>
        public int DepthScore { get; }

        /// <summary>Depth band derived from <see cref="DepthScore"/>.</summary>
        public ConfessionDepth Depth { get; }

        public ConfessionEntry(int index, string text, string theme,
            IReadOnlyList<string> anchors, int depthScore, ConfessionDepth depth)
        {
            Index = index;
            Text = text;
            Theme = theme;
            Anchors = anchors;
            DepthScore = depthScore;
            Depth = depth;
        }
    }

    /// <summary>
    /// REUSABLE menu generator (issue #843 / consumed by #842). Parses a
    /// character's stored <c>PsychologicalStake</c> confessional + optional
    /// <c>BackgroundStory</c> into a structured, depth-scored, themed
    /// "confession menu". The whole point of the ingestion hypothesis is that
    /// THIS menu (theme + depth, never raw "tension rises" abstractions) is
    /// what gets injected, and the character model reaches for whichever
    /// confession fits the moment.
    ///
    /// Nothing here touches Pinder.Rules / Roll / Shadow / Horniness code.
    /// It is a pure text transform over the character's own confessions.
    /// </summary>
    public sealed class ConfessionMenu
    {
        public string CharacterName { get; }
        public string BackgroundStory { get; }
        public IReadOnlyList<ConfessionEntry> Entries { get; }

        private ConfessionMenu(string characterName, string backgroundStory,
            IReadOnlyList<ConfessionEntry> entries)
        {
            CharacterName = characterName;
            BackgroundStory = backgroundStory;
            Entries = entries;
        }

        // ── Depth heuristic vocabulary ────────────────────────────────────
        // The depth score is an explainable, documented heuristic over the
        // confession text. It is NOT ground truth about the character's
        // psyche — it is a cue density estimate so we can soft-bias the model
        // toward lighter material early and rawer material late.
        //
        // Raw (shame / core-vulnerability) cues: words that mark exposure of
        // the self rather than a mere mishap.
        private static readonly string[] RawCues =
        {
            "humiliat", "shame", "ashamed", "cried", "crying", "kink", "never said",
            "never told", "secretly", "addict", "leaned on", "substance", "narcissist",
            "therapist", "afraid", "scared", "terrified", "lie", "lied", "pretend",
            "alone", "humiliated", "convinced everyone", "not actually", "not that impressive",
            "couldn't", "can't stop", "desperate", "worthless", "fraud", "impostor",
        };

        // Tender cues: real exposure, body/sex/relationship material that is
        // embarrassing but still narratable.
        private static readonly string[] TenderCues =
        {
            "body", "stiff", "came", "orgasm", "sexual", "sex", "naked", "undignified",
            "queef", "browser history", "impulse purchase", "dating profile",
            "rehearsing", "filmed", "in public", "bank statement",
        };

        // Light cues: deflection / mild-embarrassment / anecdote markers.
        private static readonly string[] LightCues =
        {
            "embarrassing", "awkward", "silly", "funny", "once", "accidentally",
            "saleswoman", "instructor", "downward dog", "equinox", "pillow",
        };

        /// <summary>
        /// Build the menu from the character's stake + background. The stake is
        /// the canonical 15-bullet markdown confessional persisted on the
        /// CharacterDefinition; background is optional prose context.
        /// </summary>
        public static ConfessionMenu Build(string characterName, string? psychologicalStake, string? backgroundStory)
        {
            var lines = ParseBullets(psychologicalStake ?? "");
            var entries = new List<ConfessionEntry>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i];
                var anchors = ExtractAnchors(text);
                int score = ScoreDepth(text);
                ConfessionDepth depth = BandFor(score);
                string theme = DeriveTheme(text);
                entries.Add(new ConfessionEntry(i + 1, text, theme, anchors, score, depth));
            }
            return new ConfessionMenu(characterName, (backgroundStory ?? "").Trim(), entries);
        }

        /// <summary>
        /// Split a markdown bullet list into the confession sentences. Tolerant
        /// of "- ", "* ", numbered, or bare-line formats.
        /// </summary>
        public static List<string> ParseBullets(string stake)
        {
            var result = new List<string>();
            foreach (var rawLine in stake.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                // Strip leading bullet/number markers.
                line = Regex.Replace(line, @"^[\-\*\u2022]\s+", "");
                line = Regex.Replace(line, @"^\d+[\.\)]\s+", "");
                line = line.Trim();
                if (line.Length == 0) continue;
                result.Add(line);
            }
            return result;
        }

        /// <summary>
        /// Heuristic depth score. Each Raw cue is worth 3, each Tender cue 2,
        /// each Light cue subtracts 1 (deflection lowers the score). A floor of
        /// 0 is applied. Documented + inspectable; printed in the menu dump.
        /// </summary>
        public static int ScoreDepth(string text)
        {
            string t = text.ToLowerInvariant();
            int score = 0;
            foreach (var cue in RawCues) if (t.Contains(cue)) score += 3;
            foreach (var cue in TenderCues) if (t.Contains(cue)) score += 2;
            foreach (var cue in LightCues) if (t.Contains(cue)) score -= 1;
            return Math.Max(0, score);
        }

        /// <summary>Map a depth score to a band. 0-1 Light, 2-3 Tender, 4+ Raw.</summary>
        public static ConfessionDepth BandFor(int score)
        {
            if (score >= 4) return ConfessionDepth.Raw;
            if (score >= 2) return ConfessionDepth.Tender;
            return ConfessionDepth.Light;
        }

        /// <summary>
        /// Extract named/dated anchors (capitalized proper nouns, weekdays,
        /// years, times, money) so the menu shows concrete material, never an
        /// abstraction. Best-effort and explicitly heuristic.
        /// </summary>
        public static IReadOnlyList<string> ExtractAnchors(string text)
        {
            var anchors = new List<string>();
            void AddAll(string pattern)
            {
                foreach (Match m in Regex.Matches(text, pattern))
                {
                    string v = m.Value.Trim();
                    if (v.Length > 0 && !anchors.Contains(v)) anchors.Add(v);
                }
            }
            // Weekdays / months.
            AddAll(@"\b(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b");
            AddAll(@"\b(January|February|March|April|May|June|July|August|September|October|November|December)\b");
            // Years and clock times.
            AddAll(@"\b(19|20)\d{2}\b");
            AddAll(@"\b\d{1,2}(:\d{2})?\s?(am|pm|AM|PM)\b");
            AddAll(@"\b\d{1,2}(am|pm)\b");
            // Proper-noun names (two-capitalized-words or a single capitalized
            // word that is not a sentence start). Cheap heuristic.
            foreach (Match m in Regex.Matches(text, @"\b[A-Z][a-z]+(?:\s+(?:from|at)\s+[a-z]+)?\b"))
            {
                string v = m.Value.Trim();
                // Skip the very first word of the sentence (usually "The"/"My"/"If").
                if (text.TrimStart().StartsWith(v)) continue;
                if (v.Length < 3) continue;
                if (!anchors.Contains(v)) anchors.Add(v);
            }
            return anchors;
        }

        /// <summary>
        /// One-line theme summary. Heuristic: pick the dominant cue family and
        /// the first concrete anchor so reviewers see the confession boiled
        /// down without losing its specificity.
        /// </summary>
        public static string DeriveTheme(string text)
        {
            string t = text.ToLowerInvariant();
            string family;
            if (RawCues.Any(c => t.Contains(c))) family = "shame/vulnerability";
            else if (TenderCues.Any(c => t.Contains(c))) family = "body/intimacy";
            else family = "mild embarrassment";

            // Grab a short content stub: first clause after a leading framing.
            string stub = text;
            int isIdx = text.IndexOf(" is ", StringComparison.OrdinalIgnoreCase);
            int wasIdx = text.IndexOf(" was ", StringComparison.OrdinalIgnoreCase);
            int cut = new[] { isIdx, wasIdx }.Where(x => x > 0).DefaultIfEmpty(-1).Min();
            if (cut > 0 && cut + 4 < text.Length)
                stub = text.Substring(cut + 4).Trim();
            if (stub.Length > 70) stub = stub.Substring(0, 70).TrimEnd() + "…";
            return $"[{family}] {stub}";
        }

        /// <summary>
        /// Render the menu as inspectable markdown for the transcript header and
        /// stdout. Shows every confession with its derived depth + theme so the
        /// derivation from this character's stake is fully auditable.
        /// </summary>
        public string RenderMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"### Confession menu for {CharacterName} ({Entries.Count} confessions)");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(BackgroundStory))
            {
                sb.AppendLine($"**Background:** {BackgroundStory}");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("**Background:** _(none stored — menu derives from psychological stake only)_");
                sb.AppendLine();
            }
            sb.AppendLine("| # | Depth | Score | Theme | Anchors |");
            sb.AppendLine("|---|-------|-------|-------|---------|");
            foreach (var e in Entries)
            {
                string anchors = e.Anchors.Count > 0 ? string.Join(", ", e.Anchors) : "—";
                string theme = e.Theme.Replace("|", "\\|");
                sb.AppendLine($"| {e.Index} | {e.Depth} | {e.DepthScore} | {theme.Replace("\n", " ")} | {anchors.Replace("|", "\\|")} |");
            }
            sb.AppendLine();
            sb.AppendLine("_Depth is a documented heuristic (Raw cue=+3, Tender cue=+2, Light cue=−1; "
                + "0-1=Light, 2-3=Tender, 4+=Raw). It is a cue-density estimate to soft-bias the model, "
                + "not ground truth._");
            return sb.ToString();
        }

        /// <summary>
        /// Render the full ingestible confession block injected into the
        /// CONVERSATION ARC slot: every confession verbatim + its depth band
        /// (which the register guidance keys off). This is the pre-summarized
        /// menu the character ingests.
        /// </summary>
        public string RenderIngestibleBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Below is your private inventory of " + Entries.Count +
                " confessions — real, specific, named and dated things about yourself. "
                + "You are NOT required to disclose any of them. Reach for whichever one "
                + "genuinely fits the moment, if any. Each is tagged with how close to the "
                + "bone it sits:");
            sb.AppendLine();
            foreach (var e in Entries)
            {
                string register = RegisterFor(e.Depth);
                sb.AppendLine($"- ({e.Depth}/{register}) {e.Text}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Register DERIVED from confession depth (issue requirement): far/light
        /// = guarded/playful; near/deep = raw/short. Reused by the arc strategy.
        /// </summary>
        public static string RegisterFor(ConfessionDepth depth) => depth switch
        {
            ConfessionDepth.Light => "guarded, playful, deflect with wit, keep it light",
            ConfessionDepth.Tender => "warmer, a little exposed, fewer jokes, let it land",
            ConfessionDepth.Raw => "raw and short, drop the performance, no jokes, almost reluctant",
            _ => "natural",
        };
    }
}
