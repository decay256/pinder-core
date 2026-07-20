namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Conventional sender tags used in <c>GameSession.ConversationHistory</c>
    /// for non-conversational entries. Player and datee senders are
    /// the characters' display names (free strings) — these constants
    /// cover the synthetic, scene-setting tags that have no character
    /// behind them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #333: turn-0 scene entries (player bio and datee bio) are
    /// appended to the conversation log with <see cref="Scene"/> as
    /// their sender. The frontend renders
    /// these distinctly (italics / indented / different tint) — that's
    /// frontend-side polish, separate from the engine work — and the
    /// engine deliberately filters them out of the conversation history
    /// fed to subsequent LLM calls so the analyzer does not see itself.
    /// </para>
    /// </remarks>
    public static class Senders
    {
        /// <summary>
        /// Synthetic sender tag for non-conversational scene-setting
        /// entries (issue #333). Stable wire string — snapshot tooling,
        /// the wire DTO layer, and the frontend renderer all match
        /// against this exact value.
        /// </summary>
        public const string Scene = "[scene]";

        private const string SceneDisplayPrefix = Scene + ":";

        /// <summary>
        /// True when <paramref name="sender"/> is a synthetic
        /// scene-setting tag rather than a character speaking.
        /// </summary>
        public static bool IsScene(string? sender)
        {
            return sender != null && sender.StartsWith(Scene);
        }

        /// <summary>
        /// Removes the synthetic scene display prefix from sender labels.
        /// </summary>
        public static string StripScenePrefix(string? sender)
        {
            if (sender is null) return string.Empty;

            return sender.StartsWith(SceneDisplayPrefix)
                ? sender.Substring(SceneDisplayPrefix.Length)
                : sender;
        }
    }
}
