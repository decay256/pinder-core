using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Drives the dynamic two-LLM (or LLM + scripted) conversation and emits the
    /// annotated transcript. ZERO rule paths: each character turn is
    /// build-prompt -&gt; SendAsync -&gt; record.
    ///
    /// The == CONVERSATION ARC == slot is populated each turn with the static
    /// narrative prompt loaded from data/prompts/narrative.yaml, followed by
    /// the rendered confession menu. Both are computed once before the loop.
    /// </summary>
    public sealed class HarnessRunner
    {
        /// <summary>Speaker key used for the pursuer side in the transcript.</summary>
        public const string PursuerSpeaker = "Pursuer";

        private readonly ILlmTransport _transport;
        private readonly LoadedCharacter _character;
        private readonly ConfessionMenu _menu;
        private readonly GameDefinition _baseDef;
        private readonly HarnessOptions _opts;
        private readonly IPursuerActor _pursuer;
        private readonly string _narrativePrompt;

        public HarnessRunner(ILlmTransport transport, LoadedCharacter character,
            ConfessionMenu menu, GameDefinition baseDef,
            HarnessOptions opts, IPursuerActor pursuer)
        {
            _transport = transport;
            _character = character;
            _menu = menu;
            _baseDef = baseDef;
            _opts = opts;
            _pursuer = pursuer ?? throw new ArgumentNullException(nameof(pursuer));

            // Load the static narrative prompt once (data/prompts/narrative.yaml).
            _narrativePrompt = NarrativePromptLoader.Load(AppContext.BaseDirectory);
        }

        public async Task<HarnessRunResult> RunAsync()
        {
            var doc = new StringBuilder();
            var transcript = new List<(string Speaker, string Text)>();
            var usedConfessions = new SortedSet<int>();

            // ── Header ────────────────────────────────────────────────────
            doc.AppendLine($"# Narrative Harness Transcript — {_character.Name}");
            doc.AppendLine();
            doc.AppendLine($"- **Opponent character:** {_opts.CharacterSlug} ({_character.Name}) — replies via SessionSystemPromptBuilder.BuildOpponent (arc-injected)");
            doc.AppendLine($"- **Pursuer side:** {_pursuer.HeaderLabel}");
            doc.AppendLine($"- **Turns:** {_opts.Turns}");
            doc.AppendLine($"- **Model:** claude-opus-4-8 (real Anthropic adapter)");
            doc.AppendLine($"- **Rule paths:** NONE — SessionSystemPromptBuilder → AnthropicTransport only (no Pinder.Rules).");
            doc.AppendLine($"- **Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z");
            doc.AppendLine();

            // ── Confession menu (visibly derived) ─────────────────────────
            doc.AppendLine("## Ingested confession menu");
            doc.AppendLine();
            doc.AppendLine(_menu.RenderMarkdown().TrimEnd());
            doc.AppendLine();
            // Also dump to stdout so the derivation is inspectable live.
            Console.WriteLine(_menu.RenderMarkdown());

            doc.AppendLine("## Conversation");
            doc.AppendLine();

            // ── Arc text: static narrative prompt + confession menu ────────
            // Computed once; injected verbatim into == CONVERSATION ARC == every turn.
            string arcText = _narrativePrompt + "\n\n" + _menu.RenderMarkdown();

            // Pursuer opens with its own opening line (character / scripted / generic).
            string pursuerLine = await _pursuer.OpeningLineAsync()
                ?? "hey — your profile actually made me laugh out loud, which never happens. so what's the most ridiculous thing that's happened to you this week?";

            for (int turn = 1; turn <= _opts.Turns; turn++)
            {
                // ── PURSUER turn (record the line we're sending in) ───────
                transcript.Add((PursuerSpeaker, pursuerLine));

                // ── CHARACTER turn via REAL builder + REAL transport ──────
                GameDefinition turnDef = GameDefinitionArcInjector.WithArc(_baseDef, arcText);
                string systemPrompt = SessionSystemPromptBuilder.BuildOpponent(
                    _character.AssembledSystemPrompt, turnDef);

                string userMessage = BuildCharacterUserMessage(transcript, pursuerLine);

                string reply;
                try
                {
                    reply = await _transport.SendAsync(
                        systemPrompt, userMessage, temperature: 0.9, maxTokens: 512,
                        phase: $"harness-turn-{turn}");
                }
                catch (Exception ex)
                {
                    reply = $"[transport error: {ex.Message}]";
                }
                reply = (reply ?? "").Trim();
                transcript.Add((_character.Name, reply));

                // ── Heuristic post-hoc confession detection ───────────────
                var hits = ConfessionMatcher.Detect(reply, _menu.Entries);
                foreach (var h in hits) usedConfessions.Add(h.Entry.Index);

                // ── Annotate this turn ────────────────────────────────────
                AppendTurnAnnotation(doc, turn, pursuerLine, reply, hits, usedConfessions);

                if (turn >= _opts.Turns) break;

                // ── Next pursuer line: character / scripted / generic LLM ──
                string? next = await _pursuer.NextLineAsync(transcript, turn);
                if (next == null) break; // pursuer has nothing more (e.g. scripted ran out)
                pursuerLine = next;
            }

            // ── Footer summary ────────────────────────────────────────────
            doc.AppendLine("## Used-set summary");
            doc.AppendLine();
            if (usedConfessions.Count == 0)
            {
                doc.AppendLine("_No confessions detected as drawn-on (heuristic). The character stayed surface — a valid outcome._");
            }
            else
            {
                doc.AppendLine($"Confessions the character appears to have drawn on (heuristic detection, not ground truth): "
                    + string.Join(", ", usedConfessions.Select(i => "#" + i)));
                doc.AppendLine();
                foreach (int idx in usedConfessions)
                {
                    var e = _menu.Entries.First(x => x.Index == idx);
                    doc.AppendLine($"- **#{idx}** ({e.Depth}): {e.Text}");
                }
            }
            doc.AppendLine();

            // ── Collect raw sessions from a RecordingLlmTransport ────────────
            IReadOnlyList<RawLlmSession> rawSessions;
            if (_transport is RecordingLlmTransport rec)
                rawSessions = rec.Sessions;
            else
                rawSessions = new RawLlmSession[0];

            return new HarnessRunResult(doc.ToString(), rawSessions);
        }

        private void AppendTurnAnnotation(StringBuilder doc, int turn,
            string pursuerLine, string reply, IReadOnlyList<ConfessionHit> hits, SortedSet<int> usedSet)
        {
            doc.AppendLine($"### Turn {turn} / {_opts.Turns}");
            doc.AppendLine();
            doc.AppendLine($"**Pursuer:** {pursuerLine}");
            doc.AppendLine();
            doc.AppendLine($"**{_character.Name}:** {reply}");
            doc.AppendLine();
            doc.AppendLine("<details><summary>annotation</summary>");
            doc.AppendLine();
            if (hits.Count > 0)
            {
                var top = hits.Take(3).Select(h =>
                    $"#{h.Entry.Index} ({h.Entry.Depth}, overlap {h.Overlap}: {string.Join("/", h.MatchedTokens.Take(5))})");
                doc.AppendLine($"- **Detected confession(s) [HEURISTIC]:** {string.Join("; ", top)}");
            }
            else
            {
                doc.AppendLine("- **Detected confession(s) [HEURISTIC]:** none (stayed surface / deflected)");
            }
            doc.AppendLine($"- **Used-set so far:** {(usedSet.Count == 0 ? "—" : string.Join(", ", usedSet.Select(i => "#" + i)))}");
            doc.AppendLine();
            doc.AppendLine("</details>");
            doc.AppendLine();

            // Live stdout trace.
            Console.WriteLine($"── Turn {turn}/{_opts.Turns} ──");
            Console.WriteLine($"Pursuer: {pursuerLine}");
            Console.WriteLine($"{_character.Name}: {reply}");
            Console.WriteLine($"  detected: {(hits.Count == 0 ? "none" : string.Join(", ", hits.Take(3).Select(h => "#" + h.Entry.Index)))}");
            Console.WriteLine();
        }

        /// <summary>
        /// User-message turn the character (opponent) model answers: the running
        /// conversation rendered as a chat log, ending on the pursuer's latest
        /// line. This is the only "state" — no engine state, no rolls. The
        /// pursuer side mirrors this in <see cref="CharacterPursuerActor"/>.
        /// </summary>
        internal string BuildCharacterUserMessage(List<(string Speaker, string Text)> transcript, string latestPursuer)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are texting on a dating app. This is the conversation so far. "
                + "Reply ONLY as yourself, in one natural text message (1-4 sentences). "
                + "Do not narrate or use stage directions.");
            sb.AppendLine();
            foreach (var (speaker, text) in transcript)
                sb.AppendLine($"{speaker}: {text}");
            sb.AppendLine();
            sb.AppendLine($"{_character.Name}:");
            return sb.ToString();
        }
    }
}
