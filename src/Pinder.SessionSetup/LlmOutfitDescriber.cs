using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IOutfitDescriber"/> built on
    /// <see cref="ILlmTransport"/>. One LLM call per session.
    /// Issue #333.
    /// </summary>
    /// <remarks>
    /// Uses the canonical <see cref="LlmPhase.OutfitDescription"/> phase
    /// label so snapshot recording and audit decorators tag the exchange
    /// without re-deriving the phase from prompt text.
    /// </remarks>
    public sealed class LlmOutfitDescriber : IOutfitDescriber
    {
        private const string SystemPrompt =
            "You are setting the visual scene for a comedy dating-RPG conversation. " +
            "Given the items both characters are wearing, write a single brief paragraph " +
            "describing what each is wearing and the visual / aesthetic vibe of the encounter. " +
            "Keep it grounded and concrete. " +
            "Respond in plain prose only. Do NOT use markdown formatting of any kind: no " +
            "headings, no bold or italics, no bullet or numbered lists, no blockquotes, no " +
            "inline or fenced code. Two to four sentences total \u2014 not a list, not a fashion review.";

        private readonly ILlmTransport _transport;
        private readonly Options _options;
        private readonly PromptCatalog? _catalog;

        public LlmOutfitDescriber(ILlmTransport transport, Options? options = null, PromptCatalog? catalog = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
            _catalog = catalog;
        }

        /// <summary>
        /// Generates the outfit scene setting asynchronously.
        /// This generation is OPTIONAL/degradable; if a transport failure or empty output occurs,
        /// it will trigger the <see cref="Options.OnDegraded"/> callback and return <see cref="string.Empty"/>.
        /// </summary>
        public async Task<string> GenerateAsync(
            string playerName,
            IReadOnlyList<string> playerItems,
            string dateeName,
            IReadOnlyList<string> dateeItems,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                throw new ArgumentException("playerName must not be null or whitespace.", nameof(playerName));
            if (string.IsNullOrWhiteSpace(dateeName))
                throw new ArgumentException("dateeName must not be null or whitespace.", nameof(dateeName));
            if (playerItems == null) throw new ArgumentNullException(nameof(playerItems));
            if (dateeItems == null) throw new ArgumentNullException(nameof(dateeItems));

            string systemPrompt = SystemPrompt;
            string userMessage;

            var entry = _catalog?.TryGet("outfit");
            if (entry != null)
            {
                if (entry.SystemPrompt != null) systemPrompt = entry.SystemPrompt;
            }

            var playerItemsSb = new StringBuilder();
            if (playerItems.Count == 0) playerItemsSb.AppendLine("- (no items recorded)");
            else
                foreach (var i in playerItems)
                    playerItemsSb.AppendLine($"- {i}");

            var dateeItemsSb = new StringBuilder();
            if (dateeItems.Count == 0) dateeItemsSb.AppendLine("- (no items recorded)");
            else
                foreach (var i in dateeItems)
                    dateeItemsSb.AppendLine($"- {i}");

            var values = new Dictionary<string, string>
            {
                { "playerName", playerName },
                { "playerItems", playerItemsSb.ToString().TrimEnd() },
                { "dateeName", dateeName },
                { "dateeItems", dateeItemsSb.ToString().TrimEnd() }
            };

            if (entry != null && entry.SystemPrompt != null)
            {
                systemPrompt = PromptCatalog.Substitute(systemPrompt, values);
            }

            if (entry != null && entry.UserTemplate != null)
            {
                userMessage = PromptCatalog.Substitute(entry.UserTemplate, values);
            }
            else
            {
                userMessage = BuildUserMessage(playerName, playerItems, dateeName, dateeItems);
            }

            try
            {
                string response = await _transport
                    .SendAsync(systemPrompt, userMessage, _options.Temperature, _options.MaxTokens, phase: LlmPhase.OutfitDescription)
                    .ConfigureAwait(false);
                string trimmed = (response ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    _options.OnDegraded?.Invoke(SetupGenerationResult.DegradedFailure("outfit", "empty_output"));
                }
                return trimmed;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (_options.OnDegraded != null)
                {
                    _options.OnDegraded.Invoke(SetupGenerationResult.DegradedFailure("outfit", "transport_error"));
                    return string.Empty;
                }
                throw;
            }
        }

        private static string BuildUserMessage(
            string playerName, IReadOnlyList<string> playerItems,
            string dateeName, IReadOnlyList<string> dateeItems)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Player ({playerName}) is wearing:");
            if (playerItems.Count == 0) sb.AppendLine("- (no items recorded)");
            else
                foreach (var i in playerItems)
                    sb.AppendLine($"- {i}");
            sb.AppendLine();
            sb.AppendLine($"Datee ({dateeName}) is wearing:");
            if (dateeItems.Count == 0) sb.AppendLine("- (no items recorded)");
            else
                foreach (var i in dateeItems)
                    sb.AppendLine($"- {i}");
            sb.AppendLine();
            sb.AppendLine(
                "Write 2\u20134 sentences in plain prose: what each is wearing and the overall vibe of the encounter. " +
                "Do not list items \u2014 weave them into the description. Mention both characters by name.");
            return sb.ToString();
        }

        /// <summary>Tunable knobs for <see cref="LlmOutfitDescriber"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.8 \u2014 a touch warmer than stake generation.</summary>
            public double Temperature { get; set; } = 0.8;

            /// <summary>Max output tokens. Default 250 (paragraph-sized).</summary>
            public int MaxTokens { get; set; } = 250;

            /// <summary>
            /// Opt-in callback triggered when generation is degraded (e.g. transport failure or empty output).
            /// </summary>
            public Action<SetupGenerationResult>? OnDegraded { get; set; }
        }
    }
}
