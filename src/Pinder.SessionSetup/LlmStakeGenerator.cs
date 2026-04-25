using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IStakeGenerator"/> built on <see cref="ILlmTransport"/>
    /// (non-streaming) and optionally <see cref="IStreamingLlmTransport"/>
    /// (streaming overload).
    /// Generates a novella-style "psychological stake" character bible.
    /// </summary>
    /// <remarks>
    /// Output contract (issue pinder-web #136): the returned stake is plain
    /// prose, paragraph breaks only — no markdown headings, bold, italics,
    /// bullet or numbered lists, blockquotes, or code fences. Both the system
    /// and user prompts forbid markdown explicitly. <c>Pinder.GameApi</c>
    /// runs a <c>MarkdownSanitizer</c> as defence-in-depth before storing
    /// the result.
    ///
    /// Streaming: when an <see cref="IStreamingLlmTransport"/> is supplied,
    /// <see cref="StreamStakeAsync"/> yields raw fragments as they arrive
    /// and propagates transport failures as
    /// <see cref="LlmTransportException"/> (deliberate departure from the
    /// non-streaming overload, which swallows).
    /// </remarks>
    public sealed class LlmStakeGenerator : IStakeGenerator
    {
        private const string SystemPrompt =
            "You are a novelist writing a character bible for a comedy about online dating. " +
            "Respond in plain prose only. Do NOT use markdown formatting of any kind: no " +
            "headings (#, ##), no bold or italics (**, __, *, _), no bullet or numbered " +
            "lists (-, *, +, 1., 2.), no blockquotes (>), and no inline or fenced code " +
            "(`, ```). Separate paragraphs with blank lines.";

        private readonly ILlmTransport _transport;
        private readonly IStreamingLlmTransport? _streamingTransport;
        private readonly Options _options;

        public LlmStakeGenerator(ILlmTransport transport, Options? options = null)
            : this(transport, streamingTransport: null, options)
        {
        }

        public LlmStakeGenerator(
            ILlmTransport transport,
            IStreamingLlmTransport? streamingTransport,
            Options? options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _streamingTransport = streamingTransport;
            _options = options ?? new Options();
        }

        public async Task<string> GenerateAsync(
            string characterName,
            string assembledSystemPrompt,
            CancellationToken cancellationToken = default)
        {
            ValidateInputs(characterName, assembledSystemPrompt);
            string userMessage = BuildUserMessage(assembledSystemPrompt);

            try
            {
                string response = await _transport
                    .SendAsync(SystemPrompt, userMessage, _options.Temperature, _options.MaxTokens)
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

            string userMessage = BuildUserMessage(assembledSystemPrompt);

            IAsyncEnumerator<string> enumerator;
            try
            {
                enumerator = _streamingTransport.SendStreamAsync(
                        SystemPrompt, userMessage,
                        _options.Temperature, _options.MaxTokens,
                        cancellationToken)
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

        private static string BuildUserMessage(string assembledSystemPrompt)
        {
            int promptWindow = Math.Min(4000, assembledSystemPrompt.Length);
            string promptSlice = assembledSystemPrompt.Substring(0, promptWindow);

            return
                $@"Based on this character's assembled fragments, write a psychological portrait that a novelist would use to write their dialogue. Be creative and specific — fill in gaps based on what the fragments imply, don't summarize them.

TONAL INSTRUCTION: This is a comedy game about the absurdity of online dating. The psychological stakes should be over the top and slightly ridiculous — but the character treats them as completely, genuinely real. The comedy lives in the gap between how absurd the reason sounds stated plainly and how seriously the character feels it. A character who joined because they had a spiritual crisis in an IKEA and now believes their soulmate is someone who understands the existential weight of flat-pack furniture is funnier than a character who is simply 'looking for connection' — and no less emotionally true. Lean into specific absurdity. Make the precipitating events specific and a little unhinged. The character should never know they're funny.

Cover six things, each in 2-3 paragraphs:
1. Why they are on this app right now. Not a general 'looking for connection' — a specific, absurd, over-the-top emotional context. What ridiculous but emotionally real thing happened recently? Name the specific humiliating, strange, or unhinged moment that preceded this. Make it funny but play it straight.
2. What they actually want from a match. Their real underlying need — and it should be slightly deranged in its specificity. What exact bizarre thing would having it feel like? What would they do the morning after?
3. What they are secretly afraid of. The belief about themselves they are protecting — make it specific and a little ridiculous. What absurd thing would it confirm about them if they failed here?
4. What winning this conversation would mean emotionally — not 'getting the date' but what specific, slightly unhinged thing it proves or heals or demonstrates.
5. What losing would mean emotionally — not 'getting unmatched' but the specific catastrophic conclusion they would draw about themselves.
6. Their biographical backstory: 3-5 specific, concrete, slightly unhinged events from the last 2-3 years of their life. These should be specific enough to be revealed in conversation and funny enough to belong in a comedy — not themes but events. A named relationship and the specific absurd way it ended. A job decision and what they did the week after (something strange). A specific moment of realisation in an unlikely location. A place they went alone and what they did there. These are the facts the character can share when the conversation gets real. Write them as vivid, specific narrative fragments. The more specific and slightly absurd, the better.

Write 2-3 paragraphs per point. This is a novelist's character bible for a comedy. Write flowing prose only. Do NOT use markdown formatting of any kind: no headings (#, ##), no bold or italics (**, __, *, _), no bullet or numbered lists (-, *, +, 1., 2.), no blockquotes (>), no inline or fenced code (`, ```). Separate paragraphs with blank lines. Do not number the six points; let the prose flow from one to the next. The character is real, their feelings are genuine, their reasons are ridiculous.

CHARACTER PROFILE:
{promptSlice}";
        }

        /// <summary>Tunable knobs for <see cref="LlmStakeGenerator"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.9 (matches the legacy helper).</summary>
            public double Temperature { get; set; } = 0.9;

            /// <summary>Max output tokens. Default 800.</summary>
            public int MaxTokens { get; set; } = 800;
        }
    }
}
