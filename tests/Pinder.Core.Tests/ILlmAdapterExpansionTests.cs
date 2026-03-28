using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for Issue #63: ILlmAdapter expansion — OpponentResponse type, new context fields,
    /// and interface signature change. Written from spec at docs/specs/issue-63-spec.md.
    /// Prototype maturity: happy-path tests per acceptance criterion plus edge cases.
    /// </summary>
    public class ILlmAdapterExpansionTests
    {
        // ============================================================
        // AC1: OpponentResponse class exists with correct properties
        // ============================================================

        // What: AC1 — OpponentResponse stores MessageText (spec §Function Signatures)
        // Mutation: would catch if constructor ignores messageText parameter
        [Fact]
        public void OpponentResponse_Constructor_Stores_MessageText()
        {
            var r = new OpponentResponse("Oh interesting, tell me more...");
            Assert.Equal("Oh interesting, tell me more...", r.MessageText);
        }

        // What: AC1 — OpponentResponse optional params default to null (spec §Function Signatures)
        // Mutation: would catch if defaults were non-null sentinel values
        [Fact]
        public void OpponentResponse_Minimal_Construction_Defaults_Tell_And_Weakness_To_Null()
        {
            var r = new OpponentResponse("Hi");
            Assert.Null(r.DetectedTell);
            Assert.Null(r.WeaknessWindow);
        }

        // What: AC1 — OpponentResponse with all params (spec §Input/Output Examples)
        // Mutation: would catch if Tell or WeaknessWindow are swapped in constructor
        [Fact]
        public void OpponentResponse_Full_Construction_Stores_Tell_And_Weakness()
        {
            var tell = new Tell(StatType.Charm, "She keeps mentioning confidence");
            var weakness = new WeaknessWindow(StatType.Wit, 2);
            var r = new OpponentResponse("Haha you're funny", tell, weakness);

            Assert.Equal("Haha you're funny", r.MessageText);
            Assert.Same(tell, r.DetectedTell);
            Assert.Same(weakness, r.WeaknessWindow);
        }

        // What: AC1 — Both Tell and Weakness can coexist (spec §Edge Cases)
        // Mutation: would catch if setting one nullifies the other
        [Fact]
        public void OpponentResponse_Both_Tell_And_Weakness_Set_Simultaneously()
        {
            var tell = new Tell(StatType.Honesty, "Nervous laugh");
            var weakness = new WeaknessWindow(StatType.Honesty, 2);
            var r = new OpponentResponse("Uh...", tell, weakness);

            Assert.NotNull(r.DetectedTell);
            Assert.NotNull(r.WeaknessWindow);
            Assert.Equal(StatType.Honesty, r.DetectedTell!.Stat);
            Assert.Equal(StatType.Honesty, r.WeaknessWindow!.DefendingStat);
        }

        // ============================================================
        // AC1 — Error conditions: OpponentResponse null/empty validation
        // ============================================================

        // What: AC1 — null messageText throws ArgumentNullException (spec §Edge Cases)
        // Mutation: would catch if null check is missing
        [Fact]
        public void OpponentResponse_Null_MessageText_Throws_ArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new OpponentResponse(null!));
            Assert.Equal("messageText", ex.ParamName);
        }

        // What: AC1 — empty messageText throws ArgumentException (spec §Edge Cases, resolved R1 contradiction)
        // Mutation: would catch if empty string validation is missing
        [Fact]
        public void OpponentResponse_Empty_MessageText_Throws_ArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new OpponentResponse(""));
        }

        // What: AC1 — whitespace-only messageText throws ArgumentException (spec §Edge Cases)
        // Mutation: would catch if only checking for empty but not whitespace
        [Fact]
        public void OpponentResponse_WhitespaceOnly_MessageText_Throws_ArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new OpponentResponse("   "));
        }

        // ============================================================
        // AC2: Tell type exists with correct properties
        // ============================================================

        // What: AC2 — Tell stores Stat and Description (spec §Function Signatures)
        // Mutation: would catch if Stat and Description are swapped or ignored
        [Theory]
        [InlineData(StatType.Charm)]
        [InlineData(StatType.Rizz)]
        [InlineData(StatType.Honesty)]
        [InlineData(StatType.Chaos)]
        [InlineData(StatType.Wit)]
        [InlineData(StatType.SelfAwareness)]
        public void Tell_Stores_Stat_And_Description_For_All_StatTypes(StatType stat)
        {
            var tell = new Tell(stat, "Some description");
            Assert.Equal(stat, tell.Stat);
            Assert.Equal("Some description", tell.Description);
        }

        // What: AC2 — Tell null description throws ArgumentNullException (spec §Error Conditions)
        // Mutation: would catch if null guard on description is missing
        [Fact]
        public void Tell_Null_Description_Throws_ArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new Tell(StatType.Charm, null!));
            Assert.Equal("description", ex.ParamName);
        }

        // ============================================================
        // AC2: WeaknessWindow type exists with correct properties
        // ============================================================

        // What: AC2 — WeaknessWindow stores DefendingStat and DcReduction (spec §Function Signatures)
        // Mutation: would catch if properties are not stored
        [Fact]
        public void WeaknessWindow_Stores_DefendingStat_And_DcReduction()
        {
            var w = new WeaknessWindow(StatType.Wit, 3);
            Assert.Equal(StatType.Wit, w.DefendingStat);
            Assert.Equal(3, w.DcReduction);
        }

        // What: AC2 — WeaknessWindow zero DcReduction is allowed (spec §Edge Cases)
        // Mutation: would catch if constructor rejects zero
        [Fact]
        public void WeaknessWindow_Zero_DcReduction_Is_Allowed()
        {
            var w = new WeaknessWindow(StatType.Wit, 0);
            Assert.Equal(0, w.DcReduction);
        }

        // What: AC2 — WeaknessWindow negative DcReduction is allowed (spec §Edge Cases)
        // Mutation: would catch if constructor validates DcReduction range
        [Fact]
        public void WeaknessWindow_Negative_DcReduction_Is_Allowed()
        {
            var w = new WeaknessWindow(StatType.Wit, -1);
            Assert.Equal(-1, w.DcReduction);
        }

        // ============================================================
        // AC2: CallbackOpportunity type exists with correct properties
        // ============================================================

        // What: AC2 — CallbackOpportunity stores TopicKey and TurnIntroduced (spec §Input/Output Examples)
        // Mutation: would catch if properties are not assigned
        [Fact]
        public void CallbackOpportunity_Stores_TopicKey_And_TurnIntroduced()
        {
            var cb = new CallbackOpportunity("pizza-story", 3);
            Assert.Equal("pizza-story", cb.TopicKey);
            Assert.Equal(3, cb.TurnIntroduced);
        }

        // What: AC2 — CallbackOpportunity null topicKey throws ArgumentNullException (spec §Error Conditions)
        // Mutation: would catch if null guard on topicKey is missing
        [Fact]
        public void CallbackOpportunity_Null_TopicKey_Throws_ArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new CallbackOpportunity(null!, 1));
            Assert.Equal("topicKey", ex.ParamName);
        }

        // What: AC2 — CallbackOpportunity negative TurnIntroduced is allowed (spec §Edge Cases)
        // Mutation: would catch if constructor validates TurnIntroduced range
        [Fact]
        public void CallbackOpportunity_Negative_TurnIntroduced_Is_Allowed()
        {
            var cb = new CallbackOpportunity("topic", -1);
            Assert.Equal(-1, cb.TurnIntroduced);
        }

        // ============================================================
        // AC3: ILlmAdapter.GetOpponentResponseAsync returns Task<OpponentResponse>
        // ============================================================

        // What: AC3 — interface method returns OpponentResponse not string (spec §Modified Interface)
        // Mutation: would catch if return type is still Task<string>
        [Fact]
        public async Task ILlmAdapter_GetOpponentResponseAsync_Returns_OpponentResponse()
        {
            ILlmAdapter adapter = new NullLlmAdapter();
            var ctx = MakeOpponentContext();

            // Compile-time proof: result is OpponentResponse, not string
            OpponentResponse result = await adapter.GetOpponentResponseAsync(ctx);
            Assert.NotNull(result);
            Assert.IsType<OpponentResponse>(result);
        }

        // ============================================================
        // AC4: Context types have new optional fields with correct defaults
        // ============================================================

        // What: AC4 — DialogueContext new fields default correctly (spec §Input/Output Examples)
        // Mutation: would catch if defaults are non-null/non-zero/non-false
        [Fact]
        public void DialogueContext_New_Fields_Have_Correct_Defaults()
        {
            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10);

            Assert.Null(ctx.ShadowThresholds);
            Assert.Null(ctx.CallbackOpportunities);
            Assert.Equal(0, ctx.HorninessLevel);
            Assert.False(ctx.RequiresRizzOption);
            Assert.Null(ctx.ActiveTrapInstructions);
        }

        // What: AC4 — DialogueContext accepts all new optional fields (spec §Input/Output Examples)
        // Mutation: would catch if new constructor params are not wired to properties
        [Fact]
        public void DialogueContext_Stores_All_New_Optional_Fields()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Horniness, 12 }
            };
            var callbacks = new List<CallbackOpportunity>
            {
                new CallbackOpportunity("topic1", 2)
            };
            var trapInstructions = new[] { "Taint: say something weird" };

            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                shadowThresholds: shadows,
                callbackOpportunities: callbacks,
                horninessLevel: 12,
                requiresRizzOption: true,
                activeTrapInstructions: trapInstructions);

            Assert.Same(shadows, ctx.ShadowThresholds);
            Assert.Equal(12, ctx.ShadowThresholds![ShadowStatType.Horniness]);
            Assert.Same(callbacks, ctx.CallbackOpportunities);
            Assert.Equal(12, ctx.HorninessLevel);
            Assert.True(ctx.RequiresRizzOption);
            Assert.Equal(trapInstructions, ctx.ActiveTrapInstructions);
        }

        // What: AC4 — DialogueContext empty ShadowThresholds dictionary is allowed (spec §Edge Cases)
        // Mutation: would catch if empty dict is rejected or coerced to null
        [Fact]
        public void DialogueContext_Empty_ShadowThresholds_Dictionary_Is_Allowed()
        {
            var empty = new Dictionary<ShadowStatType, int>();
            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                shadowThresholds: empty);

            Assert.NotNull(ctx.ShadowThresholds);
            Assert.Empty(ctx.ShadowThresholds!);
        }

        // What: AC4 — DeliveryContext new field defaults to null (spec §Modified Context Types)
        // Mutation: would catch if ShadowThresholds default is non-null
        [Fact]
        public void DeliveryContext_ShadowThresholds_Defaults_To_Null()
        {
            var ctx = new DeliveryContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                chosenOption: new DialogueOption(StatType.Charm, "Hi"),
                outcome: FailureTier.None,
                beatDcBy: 3,
                activeTraps: new List<string>());

            Assert.Null(ctx.ShadowThresholds);
        }

        // What: AC4 — DeliveryContext accepts ShadowThresholds (spec §Modified Context Types)
        // Mutation: would catch if ShadowThresholds parameter is ignored
        [Fact]
        public void DeliveryContext_Stores_ShadowThresholds()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 4 }
            };
            var ctx = new DeliveryContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                chosenOption: new DialogueOption(StatType.Charm, "Hi"),
                outcome: FailureTier.None,
                beatDcBy: 3,
                activeTraps: new List<string>(),
                shadowThresholds: shadows);

            Assert.NotNull(ctx.ShadowThresholds);
            Assert.Equal(4, ctx.ShadowThresholds![ShadowStatType.Madness]);
        }

        // What: AC4 — OpponentContext new fields default to null (spec §Modified Context Types)
        // Mutation: would catch if defaults are non-null
        [Fact]
        public void OpponentContext_New_Fields_Default_To_Null()
        {
            var ctx = MakeOpponentContext();

            Assert.Null(ctx.ShadowThresholds);
            Assert.Null(ctx.ActiveTrapInstructions);
        }

        // What: AC4 — OpponentContext stores new fields (spec §Modified Context Types)
        // Mutation: would catch if new params are not wired to properties
        [Fact]
        public void OpponentContext_Stores_ShadowThresholds_And_TrapInstructions()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Dread, 6 }
            };
            var instructions = new[] { "Add dread flavor" };

            var ctx = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello!",
                interestBefore: 10,
                interestAfter: 11,
                responseDelayMinutes: 1.5,
                shadowThresholds: shadows,
                activeTrapInstructions: instructions);

            Assert.NotNull(ctx.ShadowThresholds);
            Assert.Equal(6, ctx.ShadowThresholds![ShadowStatType.Dread]);
            Assert.NotNull(ctx.ActiveTrapInstructions);
            Assert.Single(ctx.ActiveTrapInstructions!);
        }

        // ============================================================
        // AC5: NullLlmAdapter returns OpponentResponse
        // ============================================================

        // What: AC5 — NullLlmAdapter.GetOpponentResponseAsync returns OpponentResponse("...")
        //        with null Tell and WeaknessWindow (spec §Modified Implementations)
        // Mutation: would catch if NullLlmAdapter still returns string
        [Fact]
        public async Task NullLlmAdapter_GetOpponentResponseAsync_Returns_OpponentResponse_With_Ellipsis()
        {
            var adapter = new NullLlmAdapter();
            var ctx = MakeOpponentContext();

            OpponentResponse result = await adapter.GetOpponentResponseAsync(ctx);

            Assert.Equal("...", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // ============================================================
        // AC4 — Backward compatibility: existing callers compile unchanged
        // ============================================================

        // What: AC4 — Existing positional callers of DialogueContext unaffected (spec §Edge Cases)
        // Mutation: would catch if new params were inserted before existing ones
        [Fact]
        public void DialogueContext_Existing_Positional_Args_Still_Work()
        {
            // This mirrors an existing call pattern — must compile and behave identically
            var history = new List<(string, string)> { ("Alice", "Hey") };
            var traps = new List<string> { "trap1" };

            var ctx = new DialogueContext("player", "opponent", history, "last", traps, 15);

            Assert.Equal("player", ctx.PlayerPrompt);
            Assert.Equal("opponent", ctx.OpponentPrompt);
            Assert.Equal(15, ctx.CurrentInterest);
            // New fields should be at defaults
            Assert.Null(ctx.ShadowThresholds);
            Assert.Equal(0, ctx.HorninessLevel);
            Assert.False(ctx.RequiresRizzOption);
        }

        // ============================================================
        // Tell — edge cases for all stat types
        // ============================================================

        // What: AC2 — Tell.Description can be empty string (spec has no restriction beyond non-null)
        // Mutation: would catch if Tell validates description is non-empty
        [Fact]
        public void Tell_Empty_Description_Is_Allowed()
        {
            var tell = new Tell(StatType.Wit, "");
            Assert.Equal("", tell.Description);
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static OpponentContext MakeOpponentContext()
        {
            return new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello!",
                interestBefore: 10,
                interestAfter: 11,
                responseDelayMinutes: 1.5);
        }
    }
}
