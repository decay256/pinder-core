using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public sealed class Issue1340_EmotionalTurnEventForwardingTests
    {
        public static IEnumerable<object[]> CanonicalOutcomeCases()
        {
            yield return new object[] { MakeSuccess(0), RollOutcomeIntensity.Clean, "clean" };
            yield return new object[] { MakeSuccess(5), RollOutcomeIntensity.Strong, "strong" };
            yield return new object[] { MakeSuccess(10), RollOutcomeIntensity.Critical, "critical" };
            yield return new object[] { MakeSuccess(15), RollOutcomeIntensity.Exceptional, "exceptional" };
            yield return new object[] { MakeNat20(), RollOutcomeIntensity.Nat20, "nat20" };
            yield return new object[] { MakeFailure(FailureTier.Fumble, 1), RollOutcomeIntensity.Fumble, "fumble" };
            yield return new object[] { MakeFailure(FailureTier.Misfire, 3), RollOutcomeIntensity.Misfire, "misfire" };
            yield return new object[] { MakeFailure(FailureTier.TropeTrap, 6), RollOutcomeIntensity.TropeTrap, "trope_trap" };
            yield return new object[] { MakeFailure(FailureTier.Catastrophe, 10), RollOutcomeIntensity.Catastrophe, "catastrophe" };
            yield return new object[] { MakeNat1(), RollOutcomeIntensity.Nat1, "nat1" };
        }

        [Theory]
        [MemberData(nameof(CanonicalOutcomeCases))]
        public void RollOutcomeIntensityContract_DerivesCanonicalKeyFromRoll(
            RollResult roll,
            RollOutcomeIntensity expectedIntensity,
            string expectedKey)
        {
            RollOutcomeIntensity actual = RollOutcomeIntensityContract.FromRollResult(roll);

            Assert.Equal(expectedIntensity, actual);
            Assert.Equal(expectedKey, RollOutcomeIntensityContract.ToKey(actual));
        }

        [Fact]
        public void RollOutcomeIntensityContract_ExposesExactOrderedTenKeys()
        {
            Assert.Equal(
                new[]
                {
                    "clean",
                    "strong",
                    "critical",
                    "exceptional",
                    "nat20",
                    "fumble",
                    "misfire",
                    "trope_trap",
                    "catastrophe",
                    "nat1",
                },
                RollOutcomeIntensityContract.OrderedKeys);
        }

        [Fact]
        public void DateeContext_PreservesOptionalEmotionalTurnEventWithDiagnosisSnapshotAndBackCompatDefault()
        {
            var diagnosis = TestHelpers.MakePsychiatricDiagnosis()
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            const string originalFeeling = "original immutable feeling";
            diagnosis[TherapistDiagnosisContract.DerivedFeelingKey] = originalFeeling;
            var evt = new DateeEmotionalTurnEvent(
                StatType.Honesty,
                RollOutcomeIntensity.Catastrophe,
                diagnosis);
            diagnosis[TherapistDiagnosisContract.DerivedFeelingKey] = "mutated after event construction";

            var context = new DateeContext(
                dateePrompt: "datee",
                conversationHistory: Array.Empty<(string Sender, string Text)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "delivered",
                interestBefore: 10,
                interestAfter: 8,
                responseDelayMinutes: 0,
                emotionalTurnEvent: evt);

            Assert.Same(evt, context.EmotionalTurnEvent);
            Assert.NotSame(diagnosis, evt.TherapistDiagnosis);
            Assert.Equal(
                originalFeeling,
                evt.TherapistDiagnosis![TherapistDiagnosisContract.DerivedFeelingKey]);

            var legacy = new DateeContext(
                dateePrompt: "datee",
                conversationHistory: Array.Empty<(string Sender, string Text)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "delivered",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 0);

            Assert.Null(legacy.EmotionalTurnEvent);
        }

        [Fact]
        public async Task ResolveTurnAsync_ForwardsCompactTypedEventFromRealTurn()
        {
            var adapter = new CapturingAdapter();
            var player = MakeProfile("Player", TestHelpers.MakeStatBlock(2));
            var datee = MakeProfile("Datee", TestHelpers.MakeStatBlock(0));
            var session = new GameSession(
                player,
                datee,
                adapter,
                new FixedDice(
                    5,
                    18,
                    50),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(adapter.LastDateeContext);
            var evt = adapter.LastDateeContext!.EmotionalTurnEvent;
            Assert.NotNull(evt);
            Assert.Equal(StatType.Charm, evt!.SelectedStat);
            Assert.Equal(RollOutcomeIntensity.Clean, evt.OutcomeIntensity);
            Assert.NotSame(datee.PsychiatricDiagnosis, evt.TherapistDiagnosis);
            Assert.Equal(datee.PsychiatricDiagnosis, evt.TherapistDiagnosis);
            Assert.Contains("Hey, you come here often?", adapter.LastDateeContext.PlayerDeliveredMessage, StringComparison.Ordinal);
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            return TestHelpers.MakeCharacterProfile(
                stats: stats,
                assembledSystemPrompt: "You are " + name + ".",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private static RollResult MakeSuccess(int beatBy)
        {
            const int dc = 10;
            return new RollResult(
                dieRoll: dc,
                secondDieRoll: null,
                usedDieRoll: dc,
                stat: StatType.Charm,
                statModifier: beatBy,
                levelBonus: 0,
                dc: dc,
                tier: FailureTier.Success);
        }

        private static RollResult MakeNat20()
        {
            return new RollResult(
                dieRoll: 20,
                secondDieRoll: null,
                usedDieRoll: 20,
                stat: StatType.Charm,
                statModifier: 0,
                levelBonus: 0,
                dc: 30,
                tier: FailureTier.Success);
        }

        private static RollResult MakeFailure(FailureTier tier, int missBy)
        {
            const int dc = 20;
            int used = dc - missBy;
            return new RollResult(
                dieRoll: used,
                secondDieRoll: null,
                usedDieRoll: used,
                stat: StatType.Charm,
                statModifier: 0,
                levelBonus: 0,
                dc: dc,
                tier: tier);
        }

        private static RollResult MakeNat1()
        {
            return new RollResult(
                dieRoll: 1,
                secondDieRoll: null,
                usedDieRoll: 1,
                stat: StatType.Charm,
                statModifier: 0,
                levelBonus: 0,
                dc: 20,
                tier: FailureTier.Legendary);
        }

        private sealed class CapturingAdapter : NullLlmAdapter
        {
            public DateeContext? LastDateeContext { get; private set; }

            public override Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
            {
                LastDateeContext = context;
                return Task.FromResult(new DateeResponse("noted"));
            }

            public override Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken cancellationToken = default)
            {
                LastDateeContext = context;
                return Task.FromResult(new StatefulDateeResult(
                    new DateeResponse("noted"),
                    Array.Empty<ConversationMessage>()));
            }
        }
    }
}
