using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Issue #1123 — Symmetric two-session GM acceptance tests.
    ///
    /// <para>
    /// The avatar (delivery) session is now stateful + cached + bleed-isolated,
    /// structurally identical to the datee session. These tests lock the three
    /// mandatory acceptance criteria:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     <b>Avatar history accumulation</b>: the engine owns
    ///     <see cref="GameSession.AvatarHistory"/> and threads the accumulated
    ///     prior turns into the stateful avatar adapter on each subsequent turn —
    ///     the mirror of the existing datee-history contract.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Bidirectional bleed isolation</b>: the datee session's context never
    ///     carries the avatar's full private system prompt, and the avatar
    ///     session's context never carries the datee's full private system prompt.
    ///     Each session sees only its OWN private stake plus the opposing
    ///     character's PUBLIC dating-app card.
    ///   </description></item>
    /// </list>
    /// (The third criterion — caching of the avatar stateful path — lives in
    /// <c>Pinder.LlmAdapters.Tests/Anthropic/AnthropicTransportCachingTests.cs</c>
    /// alongside the rest of the cache-block coverage.)
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue1123_SymmetricTwoSessionGmTests
    {
        // Private stake markers — distinct, unguessable tokens embedded in each
        // character's full assembled system prompt. Used to prove the OTHER
        // session never receives the opposing character's private spec.
        private const string AvatarPrivateStake = "AVATAR_PRIVATE_STAKE_7f3a";
        private const string DateePrivateStake = "DATEE_PRIVATE_STAKE_9c2b";

        private static CharacterProfile MakeAvatarProfile() =>
            new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(2),
                assembledSystemPrompt: $"You are Avery. {AvatarPrivateStake} You secretly want to impress.",
                displayName: "Avery",
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1,
                bio: "Avery's public bio.",
                genderIdentity: "they/them");

        private static CharacterProfile MakeDateeProfile() =>
            new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(2),
                assembledSystemPrompt: $"You are Dakota. {DateePrivateStake} You secretly are unsure.",
                displayName: "Dakota",
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1,
                bio: "Dakota's public bio.",
                genderIdentity: "she/her");

        /// <summary>
        /// Recording adapter: implements the full stateful surface and captures the
        /// avatar history it is handed on every <see cref="DeliverMessageAsync(DeliveryContext, IReadOnlyList{ConversationMessage}, CancellationToken)"/>
        /// call, plus the system prompts/context seen by both the avatar (delivery)
        /// and datee paths. Contributes one user + one assistant entry per stateful
        /// call so the engine's owned history grows across turns.
        /// </summary>
        private sealed class RecordingStatefulAdapter : IStatefulLlmAdapter
        {
            // Avatar (delivery) path observations.
            public readonly List<IReadOnlyList<ConversationMessage>> AvatarHistoriesSeen = new();
            public readonly List<string> AvatarPromptsSeen = new();
            public readonly List<PublicProfileCard> AvatarDateeCardsSeen = new();
            private int _avatarCallCount;

            // Datee path observations.
            public readonly List<IReadOnlyList<ConversationMessage>> DateeHistoriesSeen = new();
            public readonly List<string> DateePromptsSeen = new();
            public readonly List<PublicProfileCard> DateePlayerAvatarCardsSeen = new();

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
                => Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "option 1"),
                    new DialogueOption(StatType.Rizz, "option 2"),
                    new DialogueOption(StatType.Honesty, "option 3"),
                    new DialogueOption(StatType.Wit, "option 4"),
                });

            public Task<string> DeliverMessageAsync(DeliveryContext context, CancellationToken ct = default)
                => Task.FromResult("delivered");

            public Task<StatefulAvatarResult> DeliverMessageAsync(
                DeliveryContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken ct = default)
            {
                // Snapshot the history as the engine handed it to us this turn.
                AvatarHistoriesSeen.Add(history.ToArray());
                AvatarPromptsSeen.Add(context.PlayerAvatarPrompt);
                AvatarDateeCardsSeen.Add(context.DateeCard);
                int callNo = ++_avatarCallCount;
                string delivered = context.ChosenOption.IntendedText ?? "delivered";
                return Task.FromResult(new StatefulAvatarResult(delivered, new ConversationMessage[]
                {
                    ConversationMessage.User($"avatar-prompt-call-{callNo}"),
                    ConversationMessage.Assistant(delivered),
                }));
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("response"));

            public Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken ct = default)
            {
                DateeHistoriesSeen.Add(history.ToArray());
                DateePromptsSeen.Add(context.DateePrompt);
                DateePlayerAvatarCardsSeen.Add(context.PlayerAvatarCard);
                return Task.FromResult(new StatefulDateeResult(
                    new DateeResponse("response"),
                    new ConversationMessage[]
                    {
                        ConversationMessage.User($"datee-prompt-turn-{context.CurrentTurn}"),
                        ConversationMessage.Assistant("response"),
                    }));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
                => Task.FromResult(string.Empty);
            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default) => Task.FromResult(message);
            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default) => Task.FromResult(message);
            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default) => Task.FromResult(message);
        }

        private static GameSession MakeSession(RecordingStatefulAdapter adapter, IDiceRoller dice) =>
            new GameSession(
                MakeAvatarProfile(),
                MakeDateeProfile(),
                adapter,
                dice,
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

        // AC-1: Avatar GM response at turn N includes turns 1..N-1 from its
        // persistent history — the engine accumulates AvatarHistory and threads
        // it into the stateful avatar adapter, mirroring the datee-history test.
        [Fact]
        public async Task AvatarSession_AccumulatesHistory_AndThreadsItIntoSubsequentTurns()
        {
            var adapter = new RecordingStatefulAdapter();
            // ctor d10 + per-turn (d20 main + d100 timing) for 3 turns.
            var session = MakeSession(adapter, new FixedDice(5, 15, 50, 15, 50, 15, 50));

            // Engine starts with empty avatar history.
            Assert.Empty(session.AvatarHistory);

            // Turn 1.
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // First avatar call saw an EMPTY history (no prior turns).
            Assert.Single(adapter.AvatarHistoriesSeen);
            Assert.Empty(adapter.AvatarHistoriesSeen[0]);
            // Engine appended the one user + one assistant entry the adapter returned.
            Assert.Equal(2, session.AvatarHistory.Count);
            Assert.Equal(ConversationMessage.UserRole, session.AvatarHistory[0].Role);
            Assert.Equal(ConversationMessage.AssistantRole, session.AvatarHistory[1].Role);

            // Turn 2.
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Second avatar call saw the 2 entries accumulated from turn 1.
            Assert.Equal(2, adapter.AvatarHistoriesSeen.Count);
            Assert.Equal(2, adapter.AvatarHistoriesSeen[1].Count);
            Assert.Equal("avatar-prompt-call-1", adapter.AvatarHistoriesSeen[1][0].Content);
            Assert.Equal(4, session.AvatarHistory.Count);

            // Turn 3.
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Third avatar call saw all 4 entries from turns 1..2.
            Assert.Equal(3, adapter.AvatarHistoriesSeen.Count);
            Assert.Equal(4, adapter.AvatarHistoriesSeen[2].Count);
            Assert.Equal("avatar-prompt-call-1", adapter.AvatarHistoriesSeen[2][0].Content);
            Assert.Equal("avatar-prompt-call-2", adapter.AvatarHistoriesSeen[2][2].Content);
            Assert.Equal(6, session.AvatarHistory.Count); // 3 turns × (user + assistant)
        }

        // AC-2: Bidirectional bleed isolation. The datee session's context never
        // carries the avatar's full private system prompt, AND the avatar
        // session's context never carries the datee's full private system prompt.
        // Each session sees only its own private stake + the opposing PUBLIC card.
        [Fact]
        public async Task BothSessions_AreBleedIsolated_FromEachOthersPrivateStake()
        {
            var adapter = new RecordingStatefulAdapter();
            var session = MakeSession(adapter, new FixedDice(5, 15, 50));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotEmpty(adapter.AvatarPromptsSeen);
            Assert.NotEmpty(adapter.DateePromptsSeen);

            // --- Avatar (delivery) session ---
            string avatarPrompt = adapter.AvatarPromptsSeen[0];
            // Sees its OWN private stake...
            Assert.Contains(AvatarPrivateStake, avatarPrompt);
            // ...but NOT the datee's private stake.
            Assert.DoesNotContain(DateePrivateStake, avatarPrompt);
            // The datee appears ONLY as its public dating-app card (no full spec).
            PublicProfileCard dateeCard = adapter.AvatarDateeCardsSeen[0];
            Assert.Equal("Dakota", dateeCard.DisplayName);
            Assert.DoesNotContain(DateePrivateStake, dateeCard.Render());
            Assert.DoesNotContain(DateePrivateStake, dateeCard.Bio);

            // --- Datee session ---
            string dateePrompt = adapter.DateePromptsSeen[0];
            // Sees its OWN private stake...
            Assert.Contains(DateePrivateStake, dateePrompt);
            // ...but NOT the avatar's private stake.
            Assert.DoesNotContain(AvatarPrivateStake, dateePrompt);
            // The avatar appears ONLY as its public dating-app card (no full spec).
            PublicProfileCard avatarCard = adapter.DateePlayerAvatarCardsSeen[0];
            Assert.Equal("Avery", avatarCard.DisplayName);
            Assert.DoesNotContain(AvatarPrivateStake, avatarCard.Render());
            Assert.DoesNotContain(AvatarPrivateStake, avatarCard.Bio);
        }
    }
}
