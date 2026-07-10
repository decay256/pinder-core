using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IStakeGenerator"/> built on <see cref="ILlmTransport"/>
    /// (non-streaming) and optionally <see cref="IStreamingLlmTransport"/>
    /// (streaming overload).
    /// Generates a tight 5-7 single-line riff-able stake fragment list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// #826 (setup-trim phase 3): the previous novella-style 6-point
    /// 2-3-paragraphs-each character bible has been replaced with a tight
    /// 5-7 plain-text single-line fragment list. The output is injected
    /// verbatim into every turn's system prompt via
    /// <c>Player.AppendToSystemPrompt</c>, so each fragment costs per-turn
    /// tokens; the slim shape cuts ~1000 input tokens per character per
    /// turn relative to the legacy prompt.
    /// </para>
    /// <para>
    /// Output contract (issue pinder-web #136, refined by #949): the
    /// model emits a 15-item markdown bullet list — one <c>- </c>-
    /// prefixed bullet per stem-completion. No headings, bold, italics,
    /// nested bullets, numbered lists, blockquotes, or code fences.
    /// <c>Pinder.GameApi</c> runs a <c>MarkdownSanitizer</c> as defence-
    /// in-depth; per #949 that sanitizer preserves the <c>- </c> bullets
    /// unchanged so the stake renders as a bullet list in the SPA.
    /// </para>
    /// <para>
    /// Streaming: when an <see cref="IStreamingLlmTransport"/> is supplied,
    /// <see cref="StreamStakeAsync"/> yields raw fragments as they arrive
    /// and propagates transport failures as
    /// <see cref="LlmTransportException"/> (deliberate departure from the
    /// non-streaming overload, which swallows).
    /// </para>
    /// </remarks>
    public sealed class LlmStakeGenerator : IStakeGenerator
    {
        private readonly ILlmTransport _transport;
        private readonly IStreamingLlmTransport? _streamingTransport;
        private readonly Options _options;
        private readonly PromptCatalog _catalog;

        public LlmStakeGenerator(ILlmTransport transport, Options? options = null)
            : this(transport, streamingTransport: null, options, catalog: null)
        {
        }

        public LlmStakeGenerator(
            ILlmTransport transport,
            IStreamingLlmTransport? streamingTransport,
            Options? options = null)
            : this(transport, streamingTransport, options, catalog: null)
        {
        }

        /// <summary>
        /// Issue #843: catalog-aware constructor. When
        /// <paramref name="catalog"/> is non-null and contains a
        /// <c>"stake"</c> entry, system + user templates are read from
        /// it.
        /// </summary>
        public LlmStakeGenerator(
            ILlmTransport transport,
            IStreamingLlmTransport? streamingTransport,
            Options? options,
            PromptCatalog? catalog)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _streamingTransport = streamingTransport;
            _options = options ?? new Options();
            _catalog = PromptCatalog.ResolveCatalogOrThrow(catalog);
            _catalog.RequireCompleteEntry(
                "stake",
                "prompt-catalog: missing required key 'stake'. The yaml file is incomplete or missing.");
        }

        /// <summary>
        /// Effective system prompt for the stake call — the catalog entry.
        /// </summary>
        private string SystemPrompt
        {
            get
            {
                var entry = _catalog.TryGet("stake");
                if (entry != null && !string.IsNullOrWhiteSpace(entry.SystemPrompt))
                    return entry.SystemPrompt!;
                throw new InvalidOperationException("prompt-catalog: key 'stake' has no system_prompt. Check the yaml file.");
            }
        }

        /// <summary>
        /// Generates the stake psychological fragments asynchronously.
        /// This generation is OPTIONAL/degradable; if a transport failure or empty output occurs,
        /// it will trigger the <see cref="Options.OnDegraded"/> callback and return <see cref="string.Empty"/>,
        /// whereas the streaming <see cref="StreamStakeAsync"/> is required and will throw.
        /// </summary>
        public async Task<string> GenerateAsync(
            string characterName,
            string assembledSystemPrompt,
            CancellationToken cancellationToken = default)
        {
            ValidateInputs(characterName, assembledSystemPrompt);
            string userMessage = BuildUserMessage(assembledSystemPrompt, _catalog);

            var entry = _catalog.Get("stake");
            return await LlmOptionalTextGeneration.RunAsync(
                    "stake",
                    _transport,
                    SystemPrompt,
                    userMessage,
                    entry,
                    LlmPhase.PsychologicalStake,
                    _options.Temperature,
                    GeneratorDefaultConfigs.Stake.Temperature,
                    _options.MaxTokens,
                    GeneratorDefaultConfigs.Stake.MaxTokens,
                    _options.OnDegraded,
                    LlmOptionalTextGeneration.CancellationBehavior.ReturnEmpty)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> StreamStakeAsync(
            string characterName,
            string assembledSystemPrompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ValidateInputs(characterName, assembledSystemPrompt);
            if (_streamingTransport == null)
            {
                throw new InvalidOperationException(
                    "Streaming overload requires an IStreamingLlmTransport. " +
                    "Construct LlmStakeGenerator with the (ILlmTransport, IStreamingLlmTransport, Options?) overload.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            string userMessage = BuildUserMessage(assembledSystemPrompt, _catalog);

            var entry = _catalog.Get("stake");
            double temp = _options.Temperature != GeneratorDefaultConfigs.Stake.Temperature
                ? _options.Temperature
                : entry.Temperature!.Value;
            int maxTok = _options.MaxTokens != GeneratorDefaultConfigs.Stake.MaxTokens
                ? _options.MaxTokens
                : entry.MaxTokens!.Value;

            IAsyncEnumerator<string> enumerator;
            try
            {
                enumerator = _streamingTransport.SendStreamAsync(
                        SystemPrompt, userMessage,
                        temp, maxTok,
                        cancellationToken,
                        phase: LlmPhase.PsychologicalStake)
                    .GetAsyncEnumerator(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (LlmTransportException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new LlmTransportException(
                    "Failed to open streaming stake generation: " + ex.Message, ex);
            }

            try
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (LlmTransportException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new LlmTransportException(
                            "Streaming stake generation failed mid-stream: " + ex.Message, ex);
                    }

                    if (!moved) break;

                    yield return enumerator.Current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
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
            var resolvedCatalog = catalog ?? PromptTemplates.Catalog
                ?? throw new InvalidOperationException("PromptTemplates.Catalog is not wired. Call PromptWiring.Wire() at startup.");
            var entry = resolvedCatalog.TryGet("stake")
                ?? throw new InvalidOperationException("prompt-catalog: missing required key 'stake'. The yaml file is incomplete or missing.");

            return PromptCatalog.Substitute(
                entry.UserTemplate!,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "character_profile", assembledSystemPrompt },
                });
        }

        internal static List<string> ParseCanonicalStakeBullets(string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
                throw new FormatException("Stake response was empty.");

            var lines = llmResponse
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            if (lines.Count == 0)
                throw new FormatException("Stake response contained no non-empty lines.");

            var result = new List<string>(capacity: lines.Count);
            foreach (var line in lines)
            {
                if (!line.StartsWith("- ", StringComparison.Ordinal))
                    throw new FormatException("Canonical stake response must contain only '- ' markdown bullet lines.");

                var body = line.Substring(2).Trim();
                if (body.Length == 0)
                    throw new FormatException("Canonical stake response contained an empty bullet.");

                result.Add(body);
            }

            return result;
        }

        /// <summary>Tunable knobs for <see cref="LlmStakeGenerator"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.9 (matches the legacy helper).</summary>
            public double Temperature { get; set; } = GeneratorDefaultConfigs.Stake.Temperature;

            /// <summary>
            /// Max output tokens. Default 300 (#826: hard ceiling for the
            /// 5-7 single-line-fragment shape; target output is ~250 tokens
            /// per character, so 300 leaves a small safety margin).
            /// </summary>
            public int MaxTokens { get; set; } = GeneratorDefaultConfigs.Stake.MaxTokens;

            /// <summary>
            /// Opt-in callback triggered when generation is degraded (e.g. transport failure or empty output).
            /// </summary>
            public Action<SetupGenerationResult>? OnDegraded { get; set; }
        }
    }
}
