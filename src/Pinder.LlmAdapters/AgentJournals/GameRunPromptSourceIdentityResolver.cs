using System;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters.AgentJournals
{
    public sealed class GameRunPromptSourceIdentityResolver : IPromptTraceSourceIdentityResolver
    {
        public static GameRunPromptSourceIdentityResolver Instance { get; } =
            new GameRunPromptSourceIdentityResolver();

        private GameRunPromptSourceIdentityResolver()
        {
        }

        public bool TryResolve(string? annotatedSourceFile, out string? sourceId)
        {
            string source = annotatedSourceFile ?? string.Empty;
            if (IsRuntimeSource(source))
            {
                sourceId = "runtime";
                return true;
            }

            if (string.Equals(source, "game-definition.yaml", StringComparison.Ordinal)
                || string.Equals(source, "data/game-definition.yaml", StringComparison.Ordinal))
            {
                sourceId = "game.definition";
                return true;
            }

            if (string.Equals(source, "data/prompts/emotional-reactions.yaml", StringComparison.Ordinal)
                || string.Equals(source, "data/prompts/templates.yaml", StringComparison.Ordinal)
                || string.Equals(source, "data/prompts/structural.yaml", StringComparison.Ordinal)
                || string.Equals(source, "data/prompts/archetypes.yaml", StringComparison.Ordinal)
                || source.StartsWith("data/prompts/", StringComparison.Ordinal))
            {
                sourceId = "prompt.catalog";
                return true;
            }

            sourceId = null;
            return false;
        }

        public string ResolveRequired(string? annotatedSourceFile)
        {
            if (TryResolve(annotatedSourceFile, out string? sourceId)
                && !string.IsNullOrWhiteSpace(sourceId))
            {
                return sourceId;
            }

            throw new PromptTraceSourceIdentityException(
                PromptTraceSourceIdentityException.UnmappedSourceIdentity,
                "Prompt trace source has no registered journal identity mapping.");
        }

        internal static bool IsRuntimeSource(string? annotatedSourceFile)
            => string.Equals(annotatedSourceFile, "character-profile", StringComparison.Ordinal)
                || string.Equals(annotatedSourceFile, "conversation-history", StringComparison.Ordinal)
                || string.Equals(annotatedSourceFile, PromptTraceDiagnosticContract.RuntimeDateeContextSource, StringComparison.Ordinal)
                || string.Equals(annotatedSourceFile, PromptTraceDiagnosticContract.EmotionalDirectorRuntimeSource, StringComparison.Ordinal)
                || string.Equals(annotatedSourceFile, PromptTraceDiagnosticContract.CharacterDiagnosisSource, StringComparison.Ordinal)
                || (annotatedSourceFile ?? string.Empty).StartsWith("runtime:", StringComparison.Ordinal);
    }
}
