using System;

namespace Pinder.LlmAdapters.OpenAi
{
    /// <summary>
    /// Builds the <c>content</c> field of an OpenAI-compatible
    /// <c>messages[i]</c> entry in one of two shapes:
    /// <list type="bullet">
    ///   <item><description>Plain string — the legacy shape, used when prompt
    ///   caching is disabled or when the upstream provider does not honour
    ///   Anthropic-style <c>cache_control</c> markers.</description></item>
    ///   <item><description>Single-element <c>text</c> content-block array with
    ///   an inline <c>cache_control: { type: "ephemeral" }</c> breakpoint — the
    ///   shape required by Anthropic prompt caching and by OpenRouter when
    ///   routing to Anthropic-family models.</description></item>
    /// </list>
    ///
    /// <para>
    /// Issue #947: the OpenAI-compatible request path was sending a plain string
    /// for the (large, byte-stable) system prompt, so the upstream Anthropic
    /// cache layer never saw a breakpoint and <c>cache_read_input_tokens</c>
    /// stayed at zero across multi-turn sessions. Wrapping the system prompt in
    /// a content block with <c>cache_control: ephemeral</c> makes the prefix
    /// cacheable on the Anthropic / OpenRouter→Anthropic path while remaining
    /// a no-op (OpenAI accepts content-block arrays but ignores unknown fields)
    /// on the OpenAI native path, which uses automatic caching anyway.
    /// </para>
    ///
    /// <para>
    /// OpenRouter explicitly documents per-block <c>cache_control</c> as the
    /// supported way to enable Anthropic prompt caching for Claude models on
    /// their platform (see
    /// <c>https://openrouter.ai/docs/guides/best-practices/prompt-caching</c>).
    /// Sonnet-4.6 requires a stable prefix of ≥2,048 tokens; the assembled
    /// session system prompt is well above that threshold.
    /// </para>
    /// </summary>
    internal static class OpenAiCacheControl
    {
        /// <summary>
        /// Build the <c>content</c> value for a system message.
        /// </summary>
        /// <param name="systemPrompt">The system prompt text.</param>
        /// <param name="useCacheControl">
        /// When true, return a single-element content-block array with
        /// <c>cache_control: ephemeral</c>. When false, return the plain string.
        /// </param>
        public static object BuildSystemContent(string systemPrompt, bool useCacheControl)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));

            if (!useCacheControl)
                return systemPrompt;

            return new object[]
            {
                new
                {
                    type = "text",
                    text = systemPrompt,
                    cache_control = new { type = "ephemeral" }
                }
            };
        }
    }
}
