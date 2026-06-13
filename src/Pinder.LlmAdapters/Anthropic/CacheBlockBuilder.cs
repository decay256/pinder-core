using System;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Builds ContentBlock arrays with cache_control: ephemeral annotations
    /// for Anthropic prompt caching. Character system prompts (~6k tokens)
    /// are cached so turns 2+ read from cache at ~10% of normal input cost.
    /// </summary>
    public static class CacheBlockBuilder
    {
        /// <summary>
        /// Builds system blocks with both character prompts cached.
        /// Used by dialogue options and delivery calls.
        /// </summary>
        /// <param name="playerPrompt">The player's assembled §3.1 system prompt.</param>
        /// <param name="dateePrompt">The datee's assembled §3.1 system prompt.</param>
        /// <returns>Two ContentBlocks, both with cache_control: ephemeral.</returns>
        public static ContentBlock[] BuildCachedSystemBlocks(string playerPrompt, string dateePrompt)
        {
            if (playerPrompt == null) throw new ArgumentNullException(nameof(playerPrompt));
            if (dateePrompt == null) throw new ArgumentNullException(nameof(dateePrompt));

            return new[]
            {
                new ContentBlock
                {
                    Type = "text",
                    Text = playerPrompt,
                    CacheControl = new CacheControl { Type = "ephemeral" }
                },
                new ContentBlock
                {
                    Type = "text",
                    Text = dateePrompt,
                    CacheControl = new CacheControl { Type = "ephemeral" }
                }
            };
        }

        /// <summary>
        /// Builds system blocks with only the player prompt cached.
        /// Used by delivery calls where only the player speaks.
        /// </summary>
        /// <param name="playerPrompt">The player's assembled §3.1 system prompt.</param>
        /// <returns>One ContentBlock with cache_control: ephemeral.</returns>
        public static ContentBlock[] BuildPlayerOnlySystemBlocks(string playerPrompt)
        {
            if (playerPrompt == null) throw new ArgumentNullException(nameof(playerPrompt));

            return new[]
            {
                new ContentBlock
                {
                    Type = "text",
                    Text = playerPrompt,
                    CacheControl = new CacheControl { Type = "ephemeral" }
                }
            };
        }

        /// <summary>
        /// Builds system blocks with only the datee prompt cached.
        /// Used by datee response calls.
        /// </summary>
        /// <param name="dateePrompt">The datee's assembled §3.1 system prompt.</param>
        /// <returns>One ContentBlock with cache_control: ephemeral.</returns>
        public static ContentBlock[] BuildDateeOnlySystemBlocks(string dateePrompt)
        {
            if (dateePrompt == null) throw new ArgumentNullException(nameof(dateePrompt));

            return new[]
            {
                new ContentBlock
                {
                    Type = "text",
                    Text = dateePrompt,
                    CacheControl = new CacheControl { Type = "ephemeral" }
                }
            };
        }
    }
}
