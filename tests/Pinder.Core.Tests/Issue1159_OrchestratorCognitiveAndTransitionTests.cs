using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    public class Issue1159_OrchestratorCognitiveAndTransitionTests
    {
        private static readonly Guid PlayerCharacterId = Guid.Parse("14310000-0000-4000-8000-000000000071");
        private static readonly Guid DateeCharacterId = Guid.Parse("14310000-0000-4000-8000-000000000072");
        private sealed class FixedDice : IDiceRoller
        {
            private readonly int _value;

            public FixedDice(int value)
            {
                _value = value;
            }

            public int Roll(int sides) => _value;
        }

        private sealed class CapturingLlm : ILlmAdapter, IAvatarEmotionalDirectionProvider
        {
            public DialogueContext? LastDialogueContext { get; private set; }
            public bool SupportsAvatarEmotionalDirection => true;

            public Task<CharacterEmotionalDirectorResult> GetAvatarEmotionalDirectionAsync(
                DialogueContext context,
                IReadOnlyList<ConversationMessage> avatarHistory,
                LlmConversationSessionSnapshot? avatarSession,
                CancellationToken cancellationToken = default)
                => Task.FromResult(new CharacterEmotionalDirectorResult(new CharacterEmotionalDirection(
                    "shame",
                    "strong",
                    "fear of being exposed",
                    "reads the moment as risky but important",
                    "wants to admit what they mean",
                    "resists disappearing behind a joke",
                    "Writing from shame, the avatar risks honesty while resisting the urge to disappear."),
                    "director input"));

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
            {
                LastDialogueContext = context;
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "hey there"),
                    new DialogueOption(StatType.Honesty, "I should be direct")
                });
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("hi"));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, StatType stat, FailureTier tier, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);
        }

        [Fact]
        public async Task StartTurn_FirstTurn_DoesNotPassTransitionTarget()
        {
            var llm = new CapturingLlm();
            var session = MakeSession(
                llm,
                dateeDiagnosis: TestHelpers.MakePsychiatricDiagnosis());

            await session.StartTurnAsync();

            Assert.NotNull(llm.LastDialogueContext);
            Assert.Null(llm.LastDialogueContext!.ResolvedTarget);
        }

        [Fact]
        public async Task StartTurn_PresentDiagnosisMissingDefenseReaction_ThrowsTypedFailure()
        {
            var session = MakeSession(
                new CapturingLlm(),
                dateeDiagnosis: new Dictionary<string, string>
                {
                    { "derived_feeling", "abandonment" }
                });

            var ex = await Assert.ThrowsAsync<CognitiveSubtextException>(
                () => session.StartTurnAsync());

            Assert.Equal("defense_reaction", ex.MissingField);
            Assert.Equal("Sable", ex.DateeName);
            Assert.Equal(0, ex.TurnNumber);
            Assert.Equal("missing_diagnosis_field", ex.Reason);
            Assert.DoesNotContain("abandonment", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task StartTurn_PresentDiagnosisBlankDefenseReaction_ThrowsTypedFailure()
        {
            var session = MakeSession(
                new CapturingLlm(),
                dateeDiagnosis: new Dictionary<string, string>
                {
                    { TherapistDiagnosisContract.DerivedFeelingKey, "abandonment" },
                    { TherapistDiagnosisContract.DefenseReactionKey, "   " },
                });

            var ex = await Assert.ThrowsAsync<CognitiveSubtextException>(
                () => session.StartTurnAsync());

            Assert.Equal(TherapistDiagnosisContract.DefenseReactionKey, ex.MissingField);
            Assert.Equal("Sable", ex.DateeName);
            Assert.Equal(0, ex.TurnNumber);
            Assert.Equal("missing_diagnosis_field", ex.Reason);
        }

        [Fact]
        public async Task StartTurn_MissingDiagnosis_ThrowsTypedFailure()
        {
            var session = MakeSession(new CapturingLlm(), dateeDiagnosis: null);

            var ex = await Assert.ThrowsAsync<CognitiveSubtextException>(
                () => session.StartTurnAsync());

            Assert.Equal("derived_feeling", ex.MissingField);
            Assert.Equal("Sable", ex.DateeName);
            Assert.Equal(0, ex.TurnNumber);
        }

        [Fact]
        public async Task StartTurn_DerivesTellAndWeaknessFlagsFromGameState()
        {
            var llm = new CapturingLlm();
            var session = MakeSession(
                llm,
                dateeDiagnosis: TestHelpers.MakePsychiatricDiagnosis());
            session.State.ActiveTell = new Tell(StatType.Charm, "safe tell text");
            session.State.ActiveWeakness = new WeaknessWindow(StatType.Chaos, 2);

            var turn = await session.StartTurnAsync();

            Assert.True(turn.Options[0].HasTellBonus);
            Assert.False(turn.Options[0].HasWeaknessWindow);
            Assert.False(turn.Options[1].HasTellBonus);
            Assert.True(turn.Options[1].HasWeaknessWindow);
        }

        [Fact]
        public async Task StartTurn_ProjectsExactAvatarEmotionalStatusUsedByOptionsPrompt()
        {
            var llm = new CapturingLlm();
            var session = MakeSession(
                llm,
                dateeDiagnosis: TestHelpers.MakePsychiatricDiagnosis(),
                hungerForIntimacy: 7,
                terrorOfRejection: 12);

            var turn = await session.StartTurnAsync();

            Assert.Equal(7, llm.LastDialogueContext!.PlayerHungerForIntimacy);
            Assert.Equal(12, llm.LastDialogueContext.PlayerTerrorOfRejection);
            Assert.NotNull(turn.AvatarEmotionalStatusDebug);
            Assert.Equal(7, turn.AvatarEmotionalStatusDebug!.HungerForIntimacy);
            Assert.Equal(12, turn.AvatarEmotionalStatusDebug.TerrorOfRejection);
            Assert.Equal("shame", turn.AvatarEmotionalStatusDebug.Direction!.PrimaryEmotion);
            Assert.Contains("Writing from shame", turn.AvatarEmotionalStatusDebug.Direction.ResponsePosture);
            Assert.Equal("FEAR OF BEING OVERLOOKED + TESTS SINCERITY BEFORE TRUSTING IT", llm.LastDialogueContext.CognitiveSubtext);
            Assert.Equal("FEAR OF BEING OVERLOOKED + TESTS SINCERITY BEFORE TRUSTING IT", turn.AvatarEmotionalStatusDebug.CognitiveSubtext);
        }

        private static GameSession MakeSession(
            ILlmAdapter llm,
            IReadOnlyDictionary<string, string>? dateeDiagnosis,
            int hungerForIntimacy = 0,
            int terrorOfRejection = 0)
        {
            var player = MakeProfile("Velvet", PlayerCharacterId, psychiatricDiagnosis: TestHelpers.MakePsychiatricDiagnosis());
            var datee = MakeProfile("Sable", DateeCharacterId, psychiatricDiagnosis: dateeDiagnosis);

            return new GameSession(
                player,
                datee,
                llm,
                new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(
                    clock: TestHelpers.MakeClock(),
                    maxDialogueOptions: 2,
                    statDrawRng: new Random(1),
                    hungerForIntimacy: hungerForIntimacy,
                    terrorOfRejection: terrorOfRejection));
        }

        private static CharacterProfile MakeProfile(
            string name,
            Guid characterId,
            IReadOnlyDictionary<string, string>? psychiatricDiagnosis)
        {
            return new CharacterProfile(
                TestHelpers.MakeStatBlock(),
                "system prompt",
                name,
                new TimingProfile(5, 1.0f, 0.0f, "neutral"),
                level: 1,
                psychiatricDiagnosis: psychiatricDiagnosis,
                characterId: characterId);
        }

    }
}
