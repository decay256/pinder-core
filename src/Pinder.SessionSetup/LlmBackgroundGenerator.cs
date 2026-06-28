using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IBackgroundGenerator"/> built on <see cref="ILlmTransport"/>.
    /// Generates a cohesive narrative background story (3-5 sentence prose) from
    /// assembled background fragments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #820: mirrors <see cref="LlmStakeGenerator"/>. Output is plain
    /// third-person past-tense prose — no markdown, no bullets, no formatting.
    /// The result is stored on-disk in the character definition and surfaced
    /// on the Character Sheet alongside bio and stake.
    /// </para>
    /// <para>
    /// Catalog-aware: when a <see cref="PromptCatalog"/> is supplied and
    /// contains a <c>"background"</c> entry, system + user templates are read
    /// from it; otherwise fallback constants are used.
    /// </para>
    /// </remarks>
    public sealed class LlmBackgroundGenerator : IBackgroundGenerator
    {
        // Fallback constants when no PromptCatalog is supplied.
        // These must stay byte-identical to data/prompts/background.yaml
        // until the catalog is wired everywhere.
        internal const string DefaultSystemPrompt =
            "You are a narrative synthesis engine for a comedy hookup-app simulator where the protagonists are sentient penises. " +
            "Read the character profile below and weave the character's background fragments into a single cohesive narrative paragraph (3-5 sentences). " +
            "Write in third person past tense. Make it read like a brief character biography: straightforward, specific, and grounded in the details given. " +
            "Do not add new facts, embellishments, or emotional commentary not present in the source material. " +
            "Output plain prose only — no markdown formatting, no bullet points, no section headers, no bold/italics. " +
            "Keep the absurd-yet-matter-of-fact tone: the character's world is ridiculous but they take it completely seriously.";

        private readonly ILlmTransport _transport;
        private readonly Options _options;
        private readonly PromptCatalog? _catalog;

        public LlmBackgroundGenerator(ILlmTransport transport, Options? options = null)
            : this(transport, options, catalog: null)
        {
        }

        /// <summary>
        /// Catalog-aware constructor. When <paramref name="catalog"/> is
        /// non-null and contains a <c>"background"</c> entry, system + user
        /// templates are read from it; otherwise the embedded const defaults
        /// are used.
        /// </summary>
        public LlmBackgroundGenerator(
            ILlmTransport transport,
            Options? options,
            PromptCatalog? catalog)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
            _catalog = catalog;
        }

        /// <summary>
        /// Effective system prompt for the background call — the catalog
        /// entry if one is registered, otherwise the const default.
        /// </summary>
        private string SystemPrompt
        {
            get
            {
                var entry = _catalog?.TryGet("background");
                if (entry != null && !string.IsNullOrWhiteSpace(entry.SystemPrompt))
                    return entry.SystemPrompt!;
                return DefaultSystemPrompt;
            }
        }

        /// <summary>
        /// Generates the narrative background story asynchronously.
        /// This generation is OPTIONAL/degradable; if a transport failure or empty output occurs,
        /// it will trigger the <see cref="Options.OnDegraded"/> callback and return <see cref="string.Empty"/>.
        /// </summary>
        public async Task<string> GenerateAsync(
            string characterName,
            string assembledSystemPrompt,
            CancellationToken cancellationToken = default)
        {
            ValidateInputs(characterName, assembledSystemPrompt);
            string userMessage = BuildUserMessage(assembledSystemPrompt, _catalog);

            try
            {
                string response = await _transport
                    .SendAsync(SystemPrompt, userMessage, _options.Temperature, _options.MaxTokens, phase: LlmPhase.Synthesis)
                    .ConfigureAwait(false);
                string trimmed = (response ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    _options.OnDegraded?.Invoke(SetupGenerationResult.DegradedFailure("background", "empty_output"));
                }
                return trimmed;
            }
            catch (OperationCanceledException)
            {
                // Do not fire OnDegraded on cancellation; preserve existing behavior of returning empty string.
                return string.Empty;
            }
            catch
            {
                _options.OnDegraded?.Invoke(SetupGenerationResult.DegradedFailure("background", "transport_error"));
                // Mirror stake generator: transport failure → empty string.
                return string.Empty;
            }
        }

        // ── shared helpers ───────────────────────────────────────────────

        private static void ValidateInputs(string characterName, string assembledSystemPrompt)
        {
            if (string.IsNullOrWhiteSpace(characterName))
                throw new ArgumentException("characterName must not be null or whitespace.", nameof(characterName));
            if (assembledSystemPrompt == null)
                throw new ArgumentNullException(nameof(assembledSystemPrompt));
        }

        internal static string BuildUserMessage(
            string assembledSystemPrompt,
            PromptCatalog? catalog)
        {
            // Prefer the catalog template; fall back to const if absent.
            var entry = catalog?.TryGet("background");
            if (entry != null && !string.IsNullOrWhiteSpace(entry.UserTemplate))
            {
                return PromptCatalog.Substitute(
                    entry.UserTemplate!,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "character_profile", assembledSystemPrompt },
                    });
            }
            return BuildUserMessageFromConstFallback(assembledSystemPrompt);
        }

        /// <summary>
        /// Fallback user-message body when catalog is absent.
        /// </summary>
        internal static string BuildUserMessageFromConstFallback(string assembledSystemPrompt)
        {
            return
                $@"Read this character profile and synthesize the background fragments into a single narrative paragraph (3-5 sentences, third person past tense).
Use only the facts given. No embellishments. Plain prose only.

CHARACTER PROFILE:
{assembledSystemPrompt}";
        }

        /// <summary>Tunable knobs for <see cref="LlmBackgroundGenerator"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.8 (slightly lower than stake for more coherent prose).</summary>
            public double Temperature { get; set; } = 0.8;

            /// <summary>
            /// Max output tokens. Default 350 (3-5 sentences of narrative
            /// prose; allows ~250-300 tokens with a small safety margin).
            /// </summary>
            public int MaxTokens { get; set; } = 350;

            /// <summary>
            /// Opt-in callback triggered when generation is degraded (e.g. transport failure or empty output).
            /// </summary>
            public Action<SetupGenerationResult>? OnDegraded { get; set; }
        }
    }
}
