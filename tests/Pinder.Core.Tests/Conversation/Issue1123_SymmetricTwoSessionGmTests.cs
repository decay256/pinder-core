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
    /// #1125 supersedes the avatar (delivery) <em>session</em>: the delivery LLM
    /// call was collapsed into the deterministic, non-LLM DeliveryOverlay commit
    /// step, so there is no longer an avatar GM session, no avatar LLM call, and
    /// no avatar history accumulation. The clean-history rule the avatar session
    /// once upheld is now trivially satisfied — only the committed line is
    /// persisted. These tests are updated to lock the post-#1125 contract:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     <b>No avatar session</b>: <see cref="GameSession.AvatarHistory"/> stays
    ///     EMPTY across turns and no stateful avatar (delivery) LLM call fires.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Datee bleed isolation (unchanged)</b>: the datee session's context
    ///     still carries only its OWN private stake plus the player avatar's
    ///     PUBLIC dating-app card, never the player's full private system prompt.
    ///   </description></item>
    /// </list>
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

            // #1125: the stateful avatar (delivery) call no longer exists on the
            // interface and the engine never invokes it. Kept as a guard: if the
            // engine ever routed through an avatar call again, _avatarCallCount
            // would be observed non-zero by the AC-1 test below.
            public bool AvatarCallFired => _avatarCallCount > 0;

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

        // AC-1 (post-#1125): there is no avatar (delivery) session anymore, so the
        // engine accumulates NO avatar history across turns and never fires a
        // stateful avatar call. (Was: avatar history accumulation.)
        [Fact]
        public async Task AvatarSession_IsGone_NoHistoryAccumulated_NoAvatarCall()
        {
            var adapter = new RecordingStatefulAdapter();
            // ctor d10 + per-turn (d20 main + d100 timing) for 3 turns.
            var session = MakeSession(adapter, new FixedDice(5, 15, 50, 15, 50, 15, 50));

            Assert.Empty(session.AvatarHistory);

            for (int turn = 0; turn < 3; turn++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // No avatar GM session: history stays empty and no avatar call fired.
            Assert.Empty(session.AvatarHistory);
            Assert.Empty(adapter.AvatarHistoriesSeen);
            Assert.False(adapter.AvatarCallFired);
        }

        // AC-2 (post-#1125): the surviving datee session's context still carries
        // only its OWN private stake plus the player avatar's PUBLIC card, never
        // the player's full private system prompt.
        [Fact]
        public async Task DateeSession_IsBleedIsolated_FromPlayerPrivateStake()
        {
            var adapter = new RecordingStatefulAdapter();
            var session = MakeSession(adapter, new FixedDice(5, 15, 50));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotEmpty(adapter.DateePromptsSeen);
            // No avatar session fired, so no avatar prompt was ever built.
            Assert.Empty(adapter.AvatarPromptsSeen);

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
