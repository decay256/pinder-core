using System;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class Issue54_PiConversationSessionTests
    {
        [Fact]
        public async Task LegacyImport_SnapshotRestoreAndContinuationUseCanonicalTypedMessages()
        {
            LlmConversationSessionSnapshot snapshot;
            await using (PiConversationSession imported = await PiConversationSession.RestoreOrImportAsync(
                snapshot: null,
                new[]
                {
                    ConversationMessage.User("older player line"),
                    ConversationMessage.Assistant("older datee reply"),
                },
                "datee"))
            {
                await imported.AppendUserAsync("new player line");
                await imported.AppendAssistantAsync("new datee reply");
                snapshot = await imported.SnapshotAsync();
            }

            Assert.Equal(LlmConversationSessionSnapshot.PiAgentSessionV1, snapshot.Format);
            await using PiConversationSession restored = await PiConversationSession.RestoreOrImportAsync(
                snapshot,
                new[] { ConversationMessage.User("legacy fallback must be ignored") },
                "datee");
            var messages = await restored.BuildSemanticHistoryAsync();

            Assert.Equal(
                new[] { "older player line", "older datee reply", "new player line", "new datee reply" },
                messages.Select(message => message.Content).ToArray());
            Assert.Equal(
                new[]
                {
                    ConversationMessage.UserRole,
                    ConversationMessage.AssistantRole,
                    ConversationMessage.UserRole,
                    ConversationMessage.AssistantRole,
                },
                messages.Select(message => message.Role).ToArray());
        }

        [Fact]
        public async Task UnsupportedSnapshotFormatFailsClosedWithoutLegacyFallback()
        {
            var snapshot = new LlmConversationSessionSnapshot("unknown.v9", "{}");

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await PiConversationSession.RestoreOrImportAsync(
                    snapshot,
                    new[] { ConversationMessage.User("must not silently import") },
                    "datee"));

            Assert.Contains("Unsupported datee session snapshot format", error.Message, StringComparison.Ordinal);
        }
    }
}
