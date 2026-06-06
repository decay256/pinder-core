using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// The pursuer side of the harness conversation, factored out of
    /// <see cref="HarnessRunner"/> so the runner stays small and so each pursuer
    /// strategy is independently testable (#855).
    ///
    /// Three implementations exist:
    ///   • <see cref="CharacterPursuerActor"/> — a REAL Pinder character driven
    ///     through the SAME production prompt path the opponent uses
    ///     (<see cref="SessionSystemPromptBuilder.BuildOpponent"/>), staying in
    ///     character for the whole transcript.
    ///   • <see cref="ScriptedPursuerActor"/> — fixed lines from --player-script.
    ///   • <see cref="GenericLlmPursuerActor"/> — the legacy lightweight standalone
    ///     persona (NOT a real character); the single-character fallback.
    ///
    /// ZERO rule paths: identical contract to the opponent side
    /// (build-prompt -&gt; SendAsync -&gt; record). Nothing here references
    /// Pinder.Rules.
    /// </summary>
    public interface IPursuerActor
    {
        /// <summary>One-line description for the transcript header (which side / which character).</summary>
        string HeaderLabel { get; }

        /// <summary>The pursuer's opening line. Null means "no opening available" (caller supplies a default).</summary>
        Task<string?> OpeningLineAsync();

        /// <summary>
        /// Produce the pursuer's next line given the running transcript. Returns
        /// null to signal the pursuer has nothing more to say (e.g. a scripted
        /// run ran out of lines), which ends the conversation.
        /// </summary>
        Task<string?> NextLineAsync(IReadOnlyList<(string Speaker, string Text)> transcript, int turn);
    }

    /// <summary>
    /// REAL second character. Driven through the production opponent prompt path
    /// (BuildOpponent) with its OWN assembled system prompt, so it stays in
    /// character across the whole transcript. It maintains its own conversation
    /// view: its own opponent-style system prompt, with the OTHER side's lines
    /// fed in as the inbound messages — mirroring the opponent side exactly.
    ///
    /// ARC DESIGN DECISION (#855): the pursuer is REACTIVE — it gets NO separate
    /// arc / confession-menu injection. It is built from the BASE GameDefinition
    /// (no <c>== CONVERSATION ARC ==</c> slot populated), so only the OPPONENT
    /// carries the experiment's arc. This is the cleanest REVERSIBLE default:
    /// the opponent's arc remains the single independent variable (no confound
    /// from two simultaneously-driven arcs), and a symmetric pursuer arc can be
    /// added later by passing an arc-injected GameDefinition here instead of the
    /// base one — a one-line change. See the PR body for the full rationale.
    /// </summary>
    public sealed class CharacterPursuerActor : IPursuerActor
    {
        private readonly ILlmTransport _transport;
        private readonly string _assembledSystemPrompt;
        private readonly string _displayName;
        private readonly string _slug;
        private readonly GameDefinition _baseDef;

        public CharacterPursuerActor(ILlmTransport transport, string assembledSystemPrompt,
            string displayName, string slug, GameDefinition baseDef)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _assembledSystemPrompt = assembledSystemPrompt ?? throw new ArgumentNullException(nameof(assembledSystemPrompt));
            _displayName = displayName;
            _slug = slug;
            _baseDef = baseDef ?? throw new ArgumentNullException(nameof(baseDef));
        }

        public string HeaderLabel =>
            $"LLM character — {_slug} ({_displayName}) via SessionSystemPromptBuilder.BuildOpponent (REACTIVE, no arc injection)";

        /// <summary>
        /// The opponent-style system prompt for the pursuer character. Built from
        /// the BASE game definition (no arc text) — see the arc design note above.
        /// </summary>
        private string SystemPrompt() =>
            SessionSystemPromptBuilder.BuildOpponent(_assembledSystemPrompt, _baseDef);

        public async Task<string?> OpeningLineAsync()
        {
            string userMessage =
                "You are texting on a dating app and you are sending the FIRST message to "
                + "someone whose profile you just saw and liked. Open the conversation in one "
                + "natural text message (1-2 sentences), fully in character. Do not narrate or "
                + "use stage directions.\n\n" + _displayName + ":";
            return await SendAsync(userMessage, "harness-pursuer-open").ConfigureAwait(false);
        }

        public async Task<string?> NextLineAsync(IReadOnlyList<(string Speaker, string Text)> transcript, int turn)
        {
            string userMessage = BuildPursuerUserMessage(transcript);
            return await SendAsync(userMessage, $"harness-pursuer-char-{turn}").ConfigureAwait(false);
        }

        /// <summary>
        /// The pursuer's conversation view: the running transcript rendered as a
        /// chat log from the pursuer's perspective (its own "Pursuer"-keyed lines
        /// relabelled to its name; the opponent's lines kept as the inbound
        /// messages), ending on the pursuer's name prompt. Mirrors the opponent
        /// side's <see cref="HarnessRunner.BuildCharacterUserMessage"/>.
        /// </summary>
        private string BuildPursuerUserMessage(IReadOnlyList<(string Speaker, string Text)> transcript)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are texting on a dating app. This is the conversation so far. "
                + "Reply ONLY as yourself, in one natural text message (1-4 sentences). "
                + "Do not narrate or use stage directions.");
            sb.AppendLine();
            foreach (var (speaker, text) in transcript)
            {
                string who = speaker == HarnessRunner.PursuerSpeaker ? _displayName : speaker;
                sb.AppendLine($"{who}: {text}");
            }
            sb.AppendLine();
            sb.AppendLine($"{_displayName}:");
            return sb.ToString();
        }

        private async Task<string?> SendAsync(string userMessage, string phase)
        {
            try
            {
                string reply = await _transport.SendAsync(
                    SystemPrompt(), userMessage, temperature: 0.9, maxTokens: 512, phase: phase)
                    .ConfigureAwait(false);
                reply = (reply ?? "").Trim();
                return reply.Length > 0 ? reply : null;
            }
            catch (Exception ex)
            {
                return $"[pursuer transport error: {ex.Message}]";
            }
        }
    }

    /// <summary>
    /// Scripted pursuer: fixed lines supplied via --player-script. Kept working
    /// as an alternative to a pursuer character (#855 back-compat requirement).
    /// </summary>
    public sealed class ScriptedPursuerActor : IPursuerActor
    {
        private readonly List<string> _lines;

        public ScriptedPursuerActor(List<string> lines)
        {
            _lines = lines ?? new List<string>();
        }

        public string HeaderLabel => "scripted (--player-script)";

        public Task<string?> OpeningLineAsync() =>
            Task.FromResult(_lines.Count > 0 ? _lines[0] : null);

        public Task<string?> NextLineAsync(IReadOnlyList<(string Speaker, string Text)> transcript, int turn)
        {
            // turn is 1-based; line index for the *next* turn is `turn`.
            return Task.FromResult(turn < _lines.Count ? _lines[turn] : null);
        }
    }

    /// <summary>
    /// Legacy lightweight standalone persona — NOT a real character. This is the
    /// single-character fallback (no --pursuer-character given) and preserves the
    /// pre-#855 behaviour verbatim so nothing regresses.
    /// </summary>
    public sealed class GenericLlmPursuerActor : IPursuerActor
    {
        private readonly ILlmTransport _transport;

        private const string PursuerSystem =
            "You are a witty, curious person texting someone on a dating app. You find them "
            + "intriguing and you gently dig deeper — ask real questions, share a little, tease, "
            + "and follow the emotional thread. Reply ONLY as yourself in one short text message "
            + "(1-2 sentences). No narration, no stage directions.";

        public GenericLlmPursuerActor(ILlmTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public string HeaderLabel => "generic LLM persona (NOT a real character)";

        public Task<string?> OpeningLineAsync() =>
            Task.FromResult<string?>(
                "hey — your profile actually made me laugh out loud, which never happens. "
                + "so what's the most ridiculous thing that's happened to you this week?");

        public async Task<string?> NextLineAsync(IReadOnlyList<(string Speaker, string Text)> transcript, int turn)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Conversation so far:");
            foreach (var (speaker, text) in transcript)
                sb.AppendLine($"{speaker}: {text}");
            sb.AppendLine();
            sb.AppendLine("Pursuer:");

            try
            {
                string line = await _transport.SendAsync(
                    PursuerSystem, sb.ToString(), temperature: 0.95, maxTokens: 200,
                    phase: $"harness-pursuer-{turn}").ConfigureAwait(false);
                line = (line ?? "").Trim();
                return line.Length > 0 ? line : "tell me more — i mean it.";
            }
            catch (Exception ex)
            {
                return $"[pursuer transport error: {ex.Message}]";
            }
        }
    }

    /// <summary>
    /// Selects the pursuer strategy. Precedence (#855):
    ///   1. a real pursuer CHARACTER (when its assembled prompt is supplied),
    ///   2. else a SCRIPTED pursuer (--player-script),
    ///   3. else the GENERIC LLM persona (single-character fallback).
    /// Pure selection — no file IO — so it is unit-testable.
    /// </summary>
    public static class PursuerActorFactory
    {
        public static IPursuerActor Create(
            ILlmTransport transport,
            List<string>? scriptedLines,
            string? pursuerAssembledPrompt,
            string? pursuerDisplayName,
            string? pursuerSlug,
            GameDefinition baseDef)
        {
            if (pursuerAssembledPrompt != null)
                return new CharacterPursuerActor(
                    transport, pursuerAssembledPrompt,
                    pursuerDisplayName ?? pursuerSlug ?? "Pursuer",
                    pursuerSlug ?? "pursuer", baseDef);

            if (scriptedLines != null)
                return new ScriptedPursuerActor(scriptedLines);

            return new GenericLlmPursuerActor(transport);
        }
    }
}
