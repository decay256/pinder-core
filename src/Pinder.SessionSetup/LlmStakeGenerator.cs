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
    /// Output contract (issue pinder-web #136): each yielded fragment is
    /// plain text — no markdown headings, bold, italics, bullet or
    /// numbered lists, blockquotes, or code fences. The transport sees
    /// one fragment per line. Both the system and user prompts forbid
    /// markdown explicitly. <c>Pinder.GameApi</c> runs a
    /// <c>MarkdownSanitizer</c> as defence-in-depth before storing the
    /// result.
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
        // #843 Phase 1: SystemPrompt + UserTemplate fall back to these
        // const defaults when no PromptCatalog is supplied. Phase 5 of
        // the migration removes these fallbacks once every call-site
        // has been wired with the catalog and the CI grep gate is in
        // place. Strings here MUST stay byte-identical to the
        // corresponding entries in <c>data/prompts/stake.yaml</c>;
        // <see cref="Pinder.Core.Tests.Issue843_PromptCatalogPhase1Tests"/>
        // pins that invariant.
        internal const string DefaultSystemPrompt =
            "You are a sentence-completion engine for a comedy hookup-app simulator where the protagonists are sentient penises. " +
            "Read the character profile and produce exactly 15 lines — one for each numbered stem below — written in the character's first-person voice. " +
            "Each line completes the stem with one specific, concrete, slightly absurd, embodied answer that the character believes completely and would never realise is funny. " +
            "Plain text only. One stem-completion per line. Include the stem prefix. ~10-15 words per line. No markdown, no dashes, no numbering, no headings. " +
            "The character treats absurd things as completely real. Specific over generic. Embodied over emotional-meta. Undignified is a feature.";

        private readonly ILlmTransport _transport;
        private readonly IStreamingLlmTransport? _streamingTransport;
        private readonly Options _options;
        private readonly PromptCatalog? _catalog;

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
        /// it; otherwise the embedded const defaults are used.
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
            _catalog = catalog;
        }

        /// <summary>
        /// Effective system prompt for the stake call — the catalog
        /// entry if one is registered, otherwise the const default.
        /// </summary>
        private string SystemPrompt
        {
            get
            {
                var entry = _catalog?.TryGet("stake");
                if (entry != null && !string.IsNullOrWhiteSpace(entry.SystemPrompt))
                    return entry.SystemPrompt!;
                return DefaultSystemPrompt;
            }
        }

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
                return (response ?? string.Empty).Trim();
            }
            catch
            {
                // Parity with legacy helper: transport failure → empty string.
                return string.Empty;
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
            // #843 Phase 1: prefer the catalog template; fall back to the
            // embedded const if the catalog is absent or doesn't carry a
            // stake entry. Phase 5 deletes the const fallback.
            var entry = catalog?.TryGet("stake");
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
        /// Pre-#843 user-message body. Kept as a private fallback during
        /// the migration; removed in Phase 5 alongside the
        /// <see cref="DefaultSystemPrompt"/> const.
        /// </summary>
        internal static string BuildUserMessageFromConstFallback(string assembledSystemPrompt)
        {
            // #834: pass the full assembled system prompt — never silently truncate LLM input.
            // Stake generation is once-per-character-per-session and the result is cached into the
            // assembled system prompt prefix, so cost impact of full-profile input is one-time, not
            // per-turn. See LESSONS_LEARNED LLM-INPUT-SILENT-TRUNCATION-IS-ALWAYS-A-BUG.
            //
            // #868: replaced abstract 5-7 fragments with 15-stem first-person sentence completion.
            string promptSlice = assembledSystemPrompt;

            return
                $@"Read this character profile and complete each of the 15 stems below in the character's first-person voice. One line per stem, in order, ~10-15 words each. Specific, concrete, embodied. The character is real, their feelings are genuine, their answers are ridiculous. They never know they're funny.

1. The most humiliating thing that happened to me this week was when…
2. The thing about my body I'm convinced everyone notices but actually no one does is…
3. My last sexual accident or mishap was when I…
4. The kink I've never said out loud to anyone is…
5. The substance I leaned on harder than I should have last month was … and I used it to …
6. The most embarrassing impulse purchase on my last bank statement is…
7. If you opened my browser history at 3am last Tuesday you'd find…
8. The last lie I told on a dating profile or in a chat was…
9. The most undignified thing my body did in public recently was…
10. The thing I do alone in my apartment that I'd be humiliated to be filmed doing is…
11. The single object in my bedroom I could not explain to a stranger is…
12. The last time I cried and where it happened was…
13. My last named ex was [name] and the specific reason it ended was…
14. The lowest professional moment of the last year was when I…
15. The thing I genuinely believe will happen to me in the next two years that everyone else would call delusional is…

CHARACTER PROFILE:
{promptSlice}";
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
        }
    }
}
