using System;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Captures one raw LLM exchange as driven by the Narrative Harness.
    /// Populated by <see cref="RecordingLlmTransport"/> — one entry per
    /// successful <c>SendAsync</c> call.
    ///
    /// <para>
    /// <b>Speaker / Turn derivation from phase string:</b><br/>
    ///   • <c>harness-turn-{n}</c>    → Speaker = "Character", Turn = n<br/>
    ///   • <c>harness-pursuer-open</c> → Speaker = "Pursuer",   Turn = 0<br/>
    ///   • <c>harness-pursuer-char-{n}</c> → Speaker = "Pursuer", Turn = n<br/>
    ///   • <c>harness-pursuer-{n}</c>  → Speaker = "Pursuer",   Turn = n<br/>
    ///   • anything else              → Speaker = "Unknown",   Turn = null<br/>
    /// </para>
    ///
    /// <para>
    /// netstandard2.0 / LangVersion 8.0 — intentionally a normal sealed class
    /// (not a C# 9 <c>record</c>) to remain compatible with the project's
    /// LangVersion constraint.
    /// </para>
    /// </summary>
    public sealed class RawLlmSession
    {
        /// <summary>
        /// Conversation turn number.  Null for phases that cannot be mapped to a
        /// specific turn (e.g. unknown / future phase strings).
        /// </summary>
        public int? Turn { get; }

        /// <summary>"Character", "Pursuer", or "Unknown".</summary>
        public string Speaker { get; }

        /// <summary>
        /// Optional model label supplied to <see cref="RecordingLlmTransport"/>
        /// at construction time.  Null when no label was provided.
        /// </summary>
        public string? Model { get; }

        /// <summary>The system prompt sent to the LLM.</summary>
        public string SystemPrompt { get; }

        /// <summary>The user message sent to the LLM.</summary>
        public string UserMessage { get; }

        /// <summary>Sampling temperature used for this call.</summary>
        public double Temperature { get; }

        /// <summary>Max-tokens limit used for this call.</summary>
        public int MaxTokens { get; }

        /// <summary>The raw text response returned by the LLM.</summary>
        public string RawResponse { get; }

        public RawLlmSession(
            int? turn,
            string speaker,
            string? model,
            string systemPrompt,
            string userMessage,
            double temperature,
            int maxTokens,
            string rawResponse)
        {
            Turn        = turn;
            Speaker     = speaker       ?? throw new ArgumentNullException(nameof(speaker));
            Model       = model;
            SystemPrompt = systemPrompt ?? throw new ArgumentNullException(nameof(systemPrompt));
            UserMessage  = userMessage  ?? throw new ArgumentNullException(nameof(userMessage));
            Temperature  = temperature;
            MaxTokens    = maxTokens;
            RawResponse  = rawResponse  ?? throw new ArgumentNullException(nameof(rawResponse));
        }

        // ── Phase-string parsing (internal helper, exercised by tests) ────────

        public static (string Speaker, int? Turn) ParsePhase(string? phase)
        {
            if (phase == null)
                return ("Unknown", null);

            // harness-turn-{n}  →  Character, turn n
            const string charPrefix = "harness-turn-";
            if (phase.StartsWith(charPrefix, StringComparison.Ordinal))
            {
                string tail = phase.Substring(charPrefix.Length);
                if (int.TryParse(tail, out int n))
                    return ("Character", n);
                return ("Character", null);
            }

            // harness-pursuer-open  →  Pursuer, turn 0
            if (string.Equals(phase, "harness-pursuer-open", StringComparison.Ordinal))
                return ("Pursuer", 0);

            // harness-pursuer-char-{n}  →  Pursuer, turn n
            const string pursuerCharPrefix = "harness-pursuer-char-";
            if (phase.StartsWith(pursuerCharPrefix, StringComparison.Ordinal))
            {
                string tail = phase.Substring(pursuerCharPrefix.Length);
                if (int.TryParse(tail, out int n))
                    return ("Pursuer", n);
                return ("Pursuer", null);
            }

            // harness-pursuer-{n}  →  Pursuer, turn n
            const string pursuerPrefix = "harness-pursuer-";
            if (phase.StartsWith(pursuerPrefix, StringComparison.Ordinal))
            {
                string tail = phase.Substring(pursuerPrefix.Length);
                if (int.TryParse(tail, out int n))
                    return ("Pursuer", n);
                return ("Pursuer", null);
            }

            return ("Unknown", null);
        }
    }
}
