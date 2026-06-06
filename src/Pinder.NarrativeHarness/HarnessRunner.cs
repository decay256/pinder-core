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
    /// </summary>
    public sealed class HarnessRunner
    {
        private readonly ILlmTransport _transport;
        private readonly LoadedCharacter _character;
        private readonly ConfessionMenu _menu;
        private readonly GameDefinition _baseDef;
        private readonly IArcStrategy _strategy;
        private readonly HarnessOptions _opts;
        private readonly List<string>? _scripted;

        public HarnessRunner(ILlmTransport transport, LoadedCharacter character,
            ConfessionMenu menu, GameDefinition baseDef, IArcStrategy strategy,
            HarnessOptions opts, List<string>? scripted)
        {
            _transport = transport;
            _character = character;
            _menu = menu;
            _baseDef = baseDef;
            _strategy = strategy;
            _opts = opts;
            _scripted = scripted;
        }

        public async Task<string> RunAsync()
        {
            var doc = new StringBuilder();
            var transcript = new List<(string Speaker, string Text)>();
            var usedConfessions = new SortedSet<int>();

            // ── Header ────────────────────────────────────────────────────
            doc.AppendLine($"# Narrative Harness Transcript — {_character.Name}");
            doc.AppendLine();
            doc.AppendLine($"- **Character:** {_opts.CharacterSlug} ({_character.Name})");
            doc.AppendLine($"- **Arc shape:** {_strategy.Name}");
            doc.AppendLine($"- **Polarity:** {(_opts.PolarityOn ? "on" : "off")}");
            doc.AppendLine($"- **Turns:** {_opts.Turns}");
            doc.AppendLine($"- **Pursuer:** {(_scripted != null ? "scripted (--player-script)" : "LLM")}");
            doc.AppendLine($"- **Model:** claude-opus-4-8 (real Anthropic adapter)");
            doc.AppendLine($"- **Rule paths:** NONE — SessionSystemPromptBuilder → AnthropicTransport only (no Pinder.Rules).");
            doc.AppendLine($"- **Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z");
            doc.AppendLine();

            doc.AppendLine("## Arc plan");
            doc.AppendLine();
            doc.AppendLine("```");
            doc.AppendLine(_strategy.DescribePlan().TrimEnd());
            doc.AppendLine("```");
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

            // Pursuer opens with a neutral hook.
            string pursuerLine = _scripted != null && _scripted.Count > 0
                ? _scripted[0]
                : "hey — your profile actually made me laugh out loud, which never happens. so what's the most ridiculous thing that's happened to you this week?";

            for (int turn = 1; turn <= _opts.Turns; turn++)
            {
                var ctx = new ArcTurnContext(turn, _opts.Turns, _opts.PolarityOn);
                ArcDirective directive = _strategy.DirectiveFor(ctx);

                // ── PURSUER turn (record the line we're sending in) ───────
                transcript.Add(("Pursuer", pursuerLine));

                // ── CHARACTER turn via REAL builder + REAL transport ──────
                GameDefinition turnDef = GameDefinitionArcInjector.WithArc(_baseDef, directive.ArcText);
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
                AppendTurnAnnotation(doc, turn, directive, pursuerLine, reply, hits, usedConfessions);

                if (turn >= _opts.Turns) break;

                // ── Next pursuer line: scripted or LLM ────────────────────
                if (_scripted != null)
                {
                    if (turn < _scripted.Count) pursuerLine = _scripted[turn];
                    else break; // ran out of scripted lines
                }
                else
                {
                    pursuerLine = await GeneratePursuerLineAsync(transcript, turn);
                }
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

            return doc.ToString();
        }

        private void AppendTurnAnnotation(StringBuilder doc, int turn, ArcDirective directive,
            string pursuerLine, string reply, IReadOnlyList<ConfessionHit> hits, SortedSet<int> usedSet)
        {
            doc.AppendLine($"### Turn {turn} / {_opts.Turns}");
            doc.AppendLine();
            doc.AppendLine($"**Pursuer:** {pursuerLine}");
            doc.AppendLine();
            doc.AppendLine($"**{_character.Name}:** {reply}");
            doc.AppendLine();
            doc.AppendLine("<details><summary>injected arc directive + annotation</summary>");
            doc.AppendLine();
            doc.AppendLine($"- **Beat/window:** {directive.BeatLabel}");
            doc.AppendLine($"- **Soft bias:** {directive.SoftBias}");
            doc.AppendLine($"- **Register guidance:** {directive.Register}");
            doc.AppendLine($"- **Polarity:** {(_opts.PolarityOn ? "on" : "off")}");
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
            doc.AppendLine("Arc text injected into `== CONVERSATION ARC ==` this turn:");
            doc.AppendLine();
            doc.AppendLine("```");
            doc.AppendLine(directive.ArcText.TrimEnd());
            doc.AppendLine("```");
            doc.AppendLine();
            doc.AppendLine("</details>");
            doc.AppendLine();

            // Live stdout trace.
            Console.WriteLine($"── Turn {turn}/{_opts.Turns} [{directive.BeatLabel}] ──");
            Console.WriteLine($"Pursuer: {pursuerLine}");
            Console.WriteLine($"{_character.Name}: {reply}");
            Console.WriteLine($"  detected: {(hits.Count == 0 ? "none" : string.Join(", ", hits.Take(3).Select(h => "#" + h.Entry.Index)))}");
            Console.WriteLine();
        }

        /// <summary>
        /// User-message turn the character model answers: the running conversation
        /// rendered as a chat log, ending on the pursuer's latest line. This is
        /// the only "state" — no engine state, no rolls.
        /// </summary>
        private string BuildCharacterUserMessage(List<(string Speaker, string Text)> transcript, string latestPursuer)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are texting on a dating app. This is the conversation so far. "
                + "Reply ONLY as yourself, in one natural text message (1-4 sentences). "
                + "Do not narrate or use stage directions.");
            sb.AppendLine();
            // Replay everything except the just-added latest pursuer line, which
            // we present as the prompt to answer.
            var history = transcript.Take(transcript.Count).ToList();
            foreach (var (speaker, text) in history)
                sb.AppendLine($"{speaker}: {text}");
            sb.AppendLine();
            sb.AppendLine($"{_character.Name}:");
            return sb.ToString();
        }

        /// <summary>
        /// Simulated pursuer: a lightweight standalone persona (NOT the real
        /// builder — only the CHARACTER side must go through the production
        /// builder). Keeps the conversation dynamic.
        /// </summary>
        private async Task<string> GeneratePursuerLineAsync(List<(string Speaker, string Text)> transcript, int turn)
        {
            string pursuerSystem =
                "You are a witty, curious person texting someone on a dating app. You find them "
                + "intriguing and you gently dig deeper — ask real questions, share a little, tease, "
                + "and follow the emotional thread. Reply ONLY as yourself in one short text message "
                + "(1-2 sentences). No narration, no stage directions.";

            var sb = new StringBuilder();
            sb.AppendLine("Conversation so far:");
            foreach (var (speaker, text) in transcript)
                sb.AppendLine($"{speaker}: {text}");
            sb.AppendLine();
            sb.AppendLine("Pursuer:");

            try
            {
                string line = await _transport.SendAsync(
                    pursuerSystem, sb.ToString(), temperature: 0.95, maxTokens: 200,
                    phase: $"harness-pursuer-{turn}");
                line = (line ?? "").Trim();
                return line.Length > 0 ? line : "tell me more — i mean it.";
            }
            catch (Exception ex)
            {
                return $"[pursuer transport error: {ex.Message}]";
            }
        }
    }
}
