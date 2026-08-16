using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pi.AI;
using Pi.Agent.Core;
using Pinder.Core.Conversation;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Short-lived adapter over Pi.Agent.Core's store/session API. Pinder owns
    /// when semantic messages commit; Pi owns ordering, parentage, snapshots,
    /// reconstruction, and branch mechanics.
    /// </summary>
    internal sealed class PiConversationSession : IAsyncDisposable
    {
        private readonly InMemorySessionStore _store;

        private PiConversationSession(InMemorySessionStore store, ISession<SessionMetadata> session)
        {
            _store = store;
            Session = session;
        }

        public ISession<SessionMetadata> Session { get; }

        public async Task<PiConversationBranch> ForkAsync(string branchKind)
        {
            if (string.IsNullOrWhiteSpace(branchKind))
                throw new ArgumentException("Branch kind is required.", nameof(branchKind));

            var repository = Repository(_store);
            ISession<SessionMetadata> branch = await repository.ForkAsync(
                await Session.GetMetadataAsync().ConfigureAwait(false),
                new InMemorySessionCreateOptions
                {
                    Id = $"pinder-{branchKind}-{SessionUtilities.CreateSessionId()}",
                },
                SessionForkSelection.All()).ConfigureAwait(false);
            return new PiConversationBranch(repository, branch);
        }

        public static async Task<PiConversationSession> RestoreOrImportAsync(
            LlmConversationSessionSnapshot? snapshot,
            IReadOnlyList<ConversationMessage> legacyHistory,
            string sessionKind)
        {
            if (legacyHistory == null) throw new ArgumentNullException(nameof(legacyHistory));
            var store = new InMemorySessionStore();
            var repository = Repository(store);
            try
            {
                ISession<SessionMetadata> session;
                if (snapshot != null)
                {
                    if (!string.Equals(
                        snapshot.Format,
                        LlmConversationSessionSnapshot.PiAgentSessionV1,
                        StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Unsupported {sessionKind} session snapshot format '{snapshot.Format}'.");
                    }

                    SessionSnapshot decoded = SessionJsonCodec.DeserializeSnapshot(snapshot.Payload);
                    await store.RestoreSnapshotAsync(decoded).ConfigureAwait(false);
                    session = await repository.OpenAsync(decoded.Metadata).ConfigureAwait(false);
                }
                else
                {
                    session = await repository.CreateAsync(new InMemorySessionCreateOptions
                    {
                        Id = $"pinder-{sessionKind}-{SessionUtilities.CreateSessionId()}",
                    }).ConfigureAwait(false);
                    foreach (ConversationMessage message in legacyHistory)
                        await session.AppendMessageAsync(ToAgentMessage(message)).ConfigureAwait(false);
                }

                return new PiConversationSession(store, session);
            }
            catch
            {
                await store.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<IReadOnlyList<ConversationMessage>> BuildSemanticHistoryAsync()
            => await BuildSemanticHistoryAsync(Session).ConfigureAwait(false);

        internal static async Task<IReadOnlyList<ConversationMessage>> BuildSemanticHistoryAsync(
            ISession<SessionMetadata> session)
        {
            SessionContext context = await session.BuildContextAsync(new SessionContextBuildOptions
            {
                EntryProjectors = PiAgentJournalRegistry.CreateZeroContextProjectors(),
            }).ConfigureAwait(false);
            var result = new List<ConversationMessage>(context.Messages.Count);
            foreach (AgentMessage message in context.Messages)
            {
                if (message.Message is UserMessage user)
                    result.Add(ConversationMessage.User(TextUtilities.ContentText(user.Content)));
                else if (message.Message is AssistantMessage assistant)
                    result.Add(ConversationMessage.Assistant(TextUtilities.ContentText(assistant.Content)));
                else
                    throw new InvalidOperationException(
                        $"Pinder semantic session contains unsupported role '{message.Role}'.");
            }
            return result;
        }

        public async Task<string> GetAgentSessionIdAsync()
            => (await Session.GetMetadataAsync().ConfigureAwait(false)).Id;

        public Task<string> AppendUserAsync(string text)
            => Session.AppendMessageAsync(AgentMessage.FromMessage(
                new UserMessage(text ?? string.Empty, Timestamp())));

        public Task<string> AppendAssistantAsync(string text)
            => Session.AppendMessageAsync(AgentMessage.FromMessage(new AssistantMessage(
                new IAssistantMessageContent[] { new TextContent(text ?? string.Empty) },
                new Api("pinder-semantic"),
                new ProviderId("pinder"),
                "semantic-history",
                Usage.Zero,
                StopReason.Stop,
                Timestamp())));

        public async Task<LlmConversationSessionSnapshot> SnapshotAsync()
        {
            var snapshot = new SessionSnapshot
            {
                Metadata = await Session.GetMetadataAsync().ConfigureAwait(false),
                Entries = await Session.GetEntriesAsync().ConfigureAwait(false),
            };
            return new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                SessionJsonCodec.SerializeSnapshot(snapshot));
        }

        public async ValueTask DisposeAsync()
            => await _store.DisposeAsync().ConfigureAwait(false);

        private static SessionRepository<SessionMetadata, InMemorySessionCreateOptions, object> Repository(
            InMemorySessionStore store)
            => new SessionRepository<SessionMetadata, InMemorySessionCreateOptions, object>(
                new SessionRepositoryOptions<SessionMetadata, InMemorySessionCreateOptions, object>
                {
                    Store = store,
                });

        internal static AgentMessage ToAgentMessage(ConversationMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.Role == ConversationMessage.UserRole)
                return AgentMessage.FromMessage(new UserMessage(message.Content, Timestamp()));
            if (message.Role == ConversationMessage.AssistantRole)
            {
                return AgentMessage.FromMessage(new AssistantMessage(
                    new IAssistantMessageContent[] { new TextContent(message.Content) },
                    new Api("pinder-semantic"),
                    new ProviderId("pinder"),
                    "semantic-history",
                    Usage.Zero,
                    StopReason.Stop,
                    Timestamp()));
            }
            throw new InvalidOperationException($"Unsupported Pinder conversation role '{message.Role}'.");
        }

        internal static long Timestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Disposable private branch over the same Pi session store as its canonical
    /// parent. Deleting the branch cannot move or append to the canonical leaf.
    /// </summary>
    internal sealed class PiConversationBranch : IAsyncDisposable
    {
        private readonly SessionRepository<SessionMetadata, InMemorySessionCreateOptions, object> _repository;
        private readonly ISession<SessionMetadata> _session;
        private int _disposed;

        internal PiConversationBranch(
            SessionRepository<SessionMetadata, InMemorySessionCreateOptions, object> repository,
            ISession<SessionMetadata> session)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal ISession<SessionMetadata> Session => _session;

        public Task<IReadOnlyList<ConversationMessage>> BuildSemanticHistoryAsync()
            => PiConversationSession.BuildSemanticHistoryAsync(_session);

        public async Task<string> GetAgentSessionIdAsync()
            => (await _session.GetMetadataAsync().ConfigureAwait(false)).Id;

        public async Task<PiAcceptedExchangeEntryIds> AppendAcceptedExchangeAsync(string userText, string assistantText)
        {
            string userEntryId = await _session.AppendMessageAsync(PiConversationSession.ToAgentMessage(
                ConversationMessage.User(userText ?? string.Empty))).ConfigureAwait(false);
            string assistantEntryId = await _session.AppendMessageAsync(PiConversationSession.ToAgentMessage(
                ConversationMessage.Assistant(assistantText ?? string.Empty))).ConfigureAwait(false);
            return new PiAcceptedExchangeEntryIds(userEntryId, assistantEntryId);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            await _repository.DeleteAsync(await _session.GetMetadataAsync().ConfigureAwait(false))
                .ConfigureAwait(false);
        }
    }

    internal sealed class PiAcceptedExchangeEntryIds
    {
        public PiAcceptedExchangeEntryIds(string userEntryId, string assistantEntryId)
        {
            UserEntryId = userEntryId ?? throw new ArgumentNullException(nameof(userEntryId));
            AssistantEntryId = assistantEntryId ?? throw new ArgumentNullException(nameof(assistantEntryId));
        }

        public string UserEntryId { get; }

        public string AssistantEntryId { get; }
    }
}
