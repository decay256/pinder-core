using System;
using System.Collections.Generic;
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
            _catalog = catalog ?? PromptTemplates.Catalog
                ?? throw new InvalidOperationException("PromptTemplates.Catalog is not wired. Call PromptWiring.Wire() at startup.");

            // Enforce that the catalog contains the required key
            var entry = _catalog.TryGet("stake")
                ?? throw new InvalidOperationException("prompt-catalog: missing required key 'stake'. The yaml file is incomplete or missing.");
            if (string.IsNullOrWhiteSpace(entry.SystemPrompt))
                throw new InvalidOperationException("prompt-catalog: key 'stake' has no system_prompt. Check the yaml file.");
            if (string.IsNullOrWhiteSpace(entry.UserTemplate))
                throw new InvalidOperationException("prompt-catalog: key 'stake' has no user_template. Check the yaml file.");
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

            try
            {
                string response = await _transport
                    .SendAsync(SystemPrompt, userMessage, _options.Temperature, _options.MaxTokens, phase: LlmPhase.PsychologicalStake)
                    .ConfigureAwait(false);
                string trimmed = (response ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    _options.OnDegraded?.Invoke(SetupGenerationResult.DegradedFailure("stake", "empty_output"));
                }
                return trimmed;
            }
            catch (OperationCanceledException)
            {
                // Do not fire OnDegraded on cancellation; preserve existing behavior of returning empty string.
                return string.Empty;
            }
            catch (Exception ex)
            {
                if (_options.OnDegraded != null)
                {
                    _options.OnDegraded.Invoke(SetupGenerationResult.DegradedFailure("stake", "transport_error"));
                    return string.Empty;
                }
                throw;
            }
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

            IAsyncEnumerator<string> enumerator;
            try
            {
                enumerator = _streamingTransport.SendStreamAsync(
                        SystemPrompt, userMessage,
                        _options.Temperature, _options.MaxTokens,
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
            if (string.IsNullOrWhiteSpace(entry.UserTemplate))
                throw new InvalidOperationException("prompt-catalog: key 'stake' has no user_template. Check the yaml file.");

            return PromptCatalog.Substitute(
                entry.UserTemplate!,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "character_profile", assembledSystemPrompt },
                });
        }

        /// <summary>Tunable knobs for <see cref="LlmStakeGenerator"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.9 (matches the legacy helper).</summary>
            public double Temperature { get; set; } = 0.9;

            /// <summary>
            /// Max output tokens. Default 300 (#826: hard ceiling for the
            /// 5-7 single-line-fragment shape; target output is ~250 tokens
            /// per character, so 300 leaves a small safety margin).
            /// </summary>
            public int MaxTokens { get; set; } = 300;

            /// <summary>
            /// Opt-in callback triggered when generation is degraded (e.g. transport failure or empty output).
            /// </summary>
            public Action<SetupGenerationResult>? OnDegraded { get; set; }
        }
    }
}
