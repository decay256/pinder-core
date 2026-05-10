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
            "You are a writer's-room script consultant for a comedy about online dating. " +
            "Output a tight list of 5-7 single-line fragments. One fragment per line. " +
            "Plain text only. No markdown, no leading dashes, no numbering, no headings. " +
            "Each line ~10-15 words. Specific, vivid, slightly absurd, played straight.";

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
        private static string BuildUserMessageFromConstFallback(string assembledSystemPrompt)
        {
            // #834: pass the full assembled system prompt — never silently truncate LLM input.
            // Stake generation is once-per-character-per-session and the result is cached into the
            // assembled system prompt prefix, so cost impact of full-profile input is one-time, not
            // per-turn. See LESSONS_LEARNED LLM-INPUT-SILENT-TRUNCATION-IS-ALWAYS-A-BUG.
            //
            // #826 (setup-trim phase 3): output is now 5-7 single-line fragments, one per line,
            // plain text. The fragment list is injected into every turn's system prompt, so this
            // shape directly reduces per-turn input tokens by ~1000 per character relative to the
            // legacy 6-point novella prompt.
            string promptSlice = assembledSystemPrompt;

            return
                $@"Read this character profile and write 5-7 single-line riff-able fragments the dialogue model can latch onto. One per line. No prose, no markdown, no leading dashes, no numbering, no headings. ~10-15 words per line.

TONAL INSTRUCTION: This is a comedy game about the absurdity of online dating. Stakes are over the top and slightly ridiculous — but the character treats them as completely, genuinely real. The comedy lives in the gap between how absurd the line sounds stated plainly and how seriously the character feels it. Specific absurdity beats generic feeling. A character who joined because they had a spiritual crisis in an IKEA and now believes their soulmate is someone who understands the existential weight of flat-pack furniture is funnier than a character who is simply 'looking for connection' — and no less emotionally true. The character never knows they're funny.

Cover, in any order, 5-7 of these (pick whichever fit the character; the goal is a tight riff-able set, not exhaustive coverage):
- why they are on the app right now (specific absurd recent moment that preceded this)
- their secret fear about themselves (the belief they're protecting)
- what they actually want (slightly deranged in its specificity)
- what winning emotionally would prove, heal, or demonstrate (specific, unhinged)
- what losing emotionally would conclude about them (specific catastrophe)
- 2-3 backstory specifics: vivid concrete events from the last 2-3 years (a named relationship and the absurd way it ended; a job decision and what they did the week after; a specific moment of realisation in an unlikely location; a place they went alone and what they did there)

Write each fragment as one line, ~10-15 words, vivid and specific. No multi-sentence run-ons. No 'I am' or 'I want' framing — these are riff-able fragments, not first-person statements. Examples of the right shape: 'Quit a 9-year nonprofit job after fainting at a fundraiser and woke up convinced she was a saboteur.' / 'Believes the right partner will know how to fold a fitted sheet without crying.' / 'Last summer drove three hours to a town with no cell service to look at one specific oak tree.'

OUTPUT: 5-7 lines. One fragment per line. Plain text. No markdown formatting of any kind: no headings (#, ##), no bold or italics (**, __, *, _), no bullet or numbered lists (-, *, +, 1., 2.), no blockquotes (>), no inline or fenced code (`, ```). No blank lines between fragments.

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
