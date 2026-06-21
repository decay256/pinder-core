using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Trait("Category", "LlmAdapters")]
    public class Issue1208_PromptOrderingTests
    {
        private static DialogueContext MakeDialogueContext(IReadOnlyList<(string, string)> history)
        {
            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: history,
                dateeLastMessage: history.Count > 0 ? history.Last().Item2 : "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                shadowThresholds: null,
                callbackOpportunities: null,
                horninessLevel: 0,
                requiresRizzOption: false,
                activeTrapInstructions: null,
                playerName: "P",
                dateeName: "O",
                currentTurn: 1,
                availableStats: new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
        }

        private static DateeContext MakeDateeContext(IReadOnlyList<(string, string)> history)
        {
            return new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: history,
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hey",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 1.0,
                shadowThresholds: null,
                activeTrapInstructions: null,
                playerName: "P",
                dateeName: "O");
        }

        [Fact]
        public void SystemPrompt_PlayerAvatar_StaticBasePrecedesCharacterSpec()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatarEx("My Player Prompt");
            
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, result.Text);
            
            int headerIndex = result.Text.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);
            
            var gmBaseSpan = result.Spans.First(s => s.Key == "game_master_prompt");
            var profileSpan = result.Spans.First(s => s.Key == "player-profile" || s.Key == "player_avatar_role_description");
            
            Assert.True(gmBaseSpan.Start < headerIndex, "Static GM base should precede the Character Spec Header");
            Assert.True(profileSpan.Start > headerIndex, "Character profile should follow the Character Spec Header");
        }

        [Fact]
        public void DialogueOptionsPrompt_OutputFormatInstruction_IsLastBlock()
        {
            var history = new List<(string, string)> { ("O", "Hello") };
            var ctx = MakeDialogueContext(history);
            var result = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(ctx);

            var historySpan = result.Spans.First(s => s.Key == "conversation-history");
            var engineSpan = result.Spans.First(s => s.Key == "engine-options-block");
            var instructionSpan = result.Spans.First(s => s.Key == "dialogue-options-instruction");

            var maxStart = result.Spans.Max(s => s.Start);
            Assert.Equal(maxStart, instructionSpan.Start);

            Assert.True(historySpan.Start < engineSpan.Start, "History should precede engine block");
            Assert.True(engineSpan.Start < instructionSpan.Start, "Engine block should precede instructions");
        }

        [Fact]
        public void DateePrompt_ResponseInstruction_IsLastBlock()
        {
            var history = new List<(string, string)> { ("O", "Hello") };
            var ctx = MakeDateeContext(history);
            var result = SessionDocumentBuilder.BuildDateePromptEx(ctx);

            var historySpan = result.Spans.First(s => s.Key == "conversation-history");
            var engineSpan = result.Spans.First(s => s.Key == "engine-datee-block");
            var instructionSpan = result.Spans.First(s => s.Key == "datee-response-instruction");

            var maxStart = result.Spans.Max(s => s.Start);
            Assert.Equal(maxStart, instructionSpan.Start);

            Assert.True(historySpan.Start < engineSpan.Start, "History should precede engine block");
            Assert.True(engineSpan.Start < instructionSpan.Start, "Engine block should precede instructions");
        }

        [Fact]
        public void PromptOrdering_AuditDoc_Exists_AndDocumentsBuilders()
        {
            var currentDir = Directory.GetCurrentDirectory();
            string repoRoot = null;
            
            var dir = new DirectoryInfo(currentDir);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, "Pinder.Core.sln")))
                {
                    repoRoot = dir.FullName;
                    break;
                }
                dir = dir.Parent;
            }

            Assert.NotNull(repoRoot);

            var docPath = Path.Combine(repoRoot, "docs", "prompt-cache-ordering.md");
            Assert.True(File.Exists(docPath), $"Audit doc should exist at {docPath}");

            var text = File.ReadAllText(docPath);
            Assert.Contains("BuildDialogueOptionsPromptEx", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("BuildDateePromptEx", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("immutable", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
