using System;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IStakeGenerator"/> built on <see cref="ILlmTransport"/>.
    /// Generates a novella-style "psychological stake" character bible.
    /// </summary>
    public sealed class LlmStakeGenerator : IStakeGenerator
    {
        private readonly ILlmTransport _transport;
        private readonly Options _options;

        public LlmStakeGenerator(ILlmTransport transport, Options? options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
        }

        public async Task<string> GenerateAsync(
            string characterName,
            string assembledSystemPrompt,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(characterName))
                throw new ArgumentException("characterName must not be null or whitespace.", nameof(characterName));
            if (assembledSystemPrompt == null)
                throw new ArgumentNullException(nameof(assembledSystemPrompt));

            int promptWindow = Math.Min(4000, assembledSystemPrompt.Length);
            string promptSlice = assembledSystemPrompt.Substring(0, promptWindow);

            string userMessage =
                $@"Based on this character's assembled fragments, write a psychological portrait that a novelist would use to write their dialogue. Be creative and specific — fill in gaps based on what the fragments imply, don't summarize them.

TONAL INSTRUCTION: This is a comedy game about the absurdity of online dating. The psychological stakes should be over the top and slightly ridiculous — but the character treats them as completely, genuinely real. The comedy lives in the gap between how absurd the reason sounds stated plainly and how seriously the character feels it. A character who joined because they had a spiritual crisis in an IKEA and now believes their soulmate is someone who understands the existential weight of flat-pack furniture is funnier than a character who is simply 'looking for connection' — and no less emotionally true. Lean into specific absurdity. Make the precipitating events specific and a little unhinged. The character should never know they're funny.

Cover six things, each in 2-3 paragraphs:
1. Why they are on this app right now. Not a general 'looking for connection' — a specific, absurd, over-the-top emotional context. What ridiculous but emotionally real thing happened recently? Name the specific humiliating, strange, or unhinged moment that preceded this. Make it funny but play it straight.
2. What they actually want from a match. Their real underlying need — and it should be slightly deranged in its specificity. What exact bizarre thing would having it feel like? What would they do the morning after?
3. What they are secretly afraid of. The belief about themselves they are protecting — make it specific and a little ridiculous. What absurd thing would it confirm about them if they failed here?
4. What winning this conversation would mean emotionally — not 'getting the date' but what specific, slightly unhinged thing it proves or heals or demonstrates.
5. What losing would mean emotionally — not 'getting unmatched' but the specific catastrophic conclusion they would draw about themselves.
6. Their biographical backstory: 3-5 specific, concrete, slightly unhinged events from the last 2-3 years of their life. These should be specific enough to be revealed in conversation and funny enough to belong in a comedy — not themes but events. A named relationship and the specific absurd way it ended. A job decision and what they did the week after (something strange). A specific moment of realisation in an unlikely location. A place they went alone and what they did there. These are the facts the character can share when the conversation gets real. Write them as vivid, specific narrative fragments. The more specific and slightly absurd, the better.

Write 2-3 paragraphs per point. This is a novelist's character bible for a comedy. Do not use headers or bullet points — write flowing prose. The character is real, their feelings are genuine, their reasons are ridiculous.

CHARACTER PROFILE:
{promptSlice}";

            try
            {
                const string systemPrompt =
                    "You are a novelist writing a character bible for a comedy about online dating.";
                string response = await _transport
                    .SendAsync(systemPrompt, userMessage, _options.Temperature, _options.MaxTokens)
                    .ConfigureAwait(false);
                return (response ?? string.Empty).Trim();
            }
            catch
            {
                // Parity with legacy helper: transport failure → empty string.
                return string.Empty;
            }
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
