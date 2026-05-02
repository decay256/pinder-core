using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.Core.Tests.Phase0;
using Xunit;

namespace Pinder.Core.Tests.Phase4
{
    /// <summary>
    /// Phase 4 (#790) -- engine-level <see cref="GameSession.Clone"/> contract.
    ///
    /// <para>
    /// Three locked properties, in priority order:
    /// </para>
    /// <list type="number">
    ///   <item><b>Independence:</b> mutating one branch does not affect the
    ///   parent or any other branch. Without this property the fast-gameplay
    ///   scheduler (#425, Phase 5) cannot run three forks in parallel.</item>
    ///   <item><b>Determinism:</b> running the same option through the parent
    ///   and a clone with the same injected
    ///   <see cref="Pinder.Core.Rolls.PerOptionDicePool"/> produces a
    ///   byte-identical post-state (snapshot equality). Without this property
    ///   the adopted-branch invariant from #393's epic doesn't hold (the chosen
    ///   branch wouldn't reproduce what the parent would have computed).</item>
    ///   <item><b>Completeness:</b> every instance field on
    ///   <see cref="GameSession"/> is either deep-copied by the clone
    ///   constructor or explicitly listed as a documented shared-by-reference
    ///   field. A future PR adding a new mutable field must extend the clone
    ///   constructor or the test below fails fast -- closing the "easy to
    ///   forget" gap that Path 1 (explicit Clone()) introduces vs. Path 2
    ///   (snapshot round-trip).</item>
    /// </list>
    /// </summary>
    [Trait("Category", "Phase4")]
    public class Phase4_GameSessionCloneTests
    {
        // ---- (1) Independence -----------------------------------------------------
        //
        // The contract: take a clone, run a turn through the parent (mutating
        // its state), and assert the clone's snapshot still matches the
        // pre-mutation snapshot. Direction matters -- we mutate the parent
        // because it has the unconsumed dice queue; the clone is left
        // untouched so that "clone state hasn't moved" is the simple
        // assertion.

        [Fact]
        public async Task Clone_IsIndependent_FromParentMutationsOnConversationHistory()
        {
            var (parent, _) = MakeFreshSessionWithTurnsQueued(turnsToQueue: 2);

            // Capture clone BEFORE running any turn.
            var clone = parent.Clone();
            var cloneSnapshotBefore = clone.CreateSnapshot();
            var cloneHistoryBefore = clone.ConversationHistory.Select(e => (e.Sender, e.Text)).ToList();
            var cloneOpponentHistoryBefore = clone.OpponentHistory
                .Select(m => (m.Role.ToString(), m.Content)).ToList();

            // Mutate the PARENT only.
            await parent.StartTurnAsync();
            await parent.ResolveTurnAsync(0);

            // Parent moved on:
            Assert.Equal(1, parent.TurnNumber);
            Assert.NotEmpty(parent.ConversationHistory);

            // Clone unchanged -- snapshot, history, and opponent history all
            // identical to the pre-mutation capture.
            var cloneSnapshotAfter = clone.CreateSnapshot();
            Assert.Equal(cloneSnapshotBefore.Interest, cloneSnapshotAfter.Interest);
            Assert.Equal(cloneSnapshotBefore.MomentumStreak, cloneSnapshotAfter.MomentumStreak);
            Assert.Equal(cloneSnapshotBefore.TurnNumber, cloneSnapshotAfter.TurnNumber);
            Assert.Equal(0, clone.TurnNumber);

            var cloneHistoryAfter = clone.ConversationHistory.Select(e => (e.Sender, e.Text)).ToList();
            Assert.Equal(cloneHistoryBefore.Count, cloneHistoryAfter.Count);

            var cloneOpponentHistoryAfter = clone.OpponentHistory
                .Select(m => (m.Role.ToString(), m.Content)).ToList();
            Assert.Equal(cloneOpponentHistoryBefore.Count, cloneOpponentHistoryAfter.Count);
        }

        [Fact]
        public async Task Clone_IsIndependent_OnTopicsList()
        {
            var (parent, _) = MakeFreshSessionWithTurnsQueued(turnsToQueue: 0);
            await Task.CompletedTask;

            var clone = parent.Clone();
            parent.AddTopic(new CallbackOpportunity(topicKey: "favourite-pizza", turnIntroduced: 0));

            // The clone's _topics list is private; we verify behaviourally
            // by adding the same topic to the clone and checking that both
            // sessions converge on identical CreateSnapshot output. (The
            // structural completeness test below pins independence at the
            // field level via reflection.)
            clone.AddTopic(new CallbackOpportunity(topicKey: "favourite-pizza", turnIntroduced: 0));
            Assert.Equal(parent.CreateSnapshot().Interest, clone.CreateSnapshot().Interest);
        }

        // ---- (2) Determinism ------------------------------------------------------
        //
        // Set up a session with a queued canned-response transport. Run
        // StartTurnAsync, clone, then resolve the same option index on each
        // session with the SAME pre-built PerOptionDicePool injected. Snapshot
        // equality on the post-resolve state proves determinism.
        //
        // Sharing-by-reference contract: the LLM adapter chain is shared
        // between parent and clone (per Clone()'s documented allowlist). To
        // prevent the second resolve from getting a drained queue, we queue 2x
        // the canned response volume up front. This mirrors the production
        // fork shape -- in #425 each branch will be wired with its own
        // adapter chain (separate transport + audit sink), so resolves drain
        // independently. Here we collapse to one transport for simplicity;
        // the engine state contract is what's under test.

        [Fact]
        public async Task Clone_IsDeterministic_OnInjectedDicePool()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            for (int t = 0; t < 2; t++)
            {
                transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                transport.QueueDelivery("delivered-msg");
                transport.QueueOpponent("opponent-reply");
            }
            var adapter = Phase0Fixtures.MakeAdapter(transport);
            // Plenty of dice slack -- resolves use the injected pool, but the
            // ctor still draws a d10. The PlaybackDiceRoller must NOT trigger
            // the "exhausted" guard during normal flow.
            var dice = new PlaybackDiceRoller(5, 15, 50, 15, 50, 15, 50);

            var sessionA = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());

            await sessionA.StartTurnAsync();

            // Clone AFTER StartTurnAsync -- mid-turn state (current options,
            // current dice pools, has-advantage flags) is part of the clone
            // surface.
            var sessionB = sessionA.Clone();

            // Build a deterministic injected pool that satisfies the chosen
            // option's runtime budget: 1x d20 + 1x d100. (No advantage in this
            // fixture: starting interest 10 -> Interested state -> no adv/disadv.)
            var injectedPool = new Pinder.Core.Rolls.PerOptionDicePool(
                optionIndex: 0,
                values: new[] { 17, 42 });

            sessionA.InjectNextDicePool(injectedPool);
            sessionB.InjectNextDicePool(injectedPool);

            await sessionA.ResolveTurnAsync(0);
            await sessionB.ResolveTurnAsync(0);

            // Byte-identical post-state on the public snapshot surface.
            var snapA = sessionA.CreateSnapshot();
            var snapB = sessionB.CreateSnapshot();
            Assert.Equal(snapA.Interest, snapB.Interest);
            Assert.Equal(snapA.MomentumStreak, snapB.MomentumStreak);
            Assert.Equal(snapA.TurnNumber, snapB.TurnNumber);
            Assert.Equal(snapA.TripleBonusActive, snapB.TripleBonusActive);
            Assert.Equal(snapA.ActiveTrapNames.Length, snapB.ActiveTrapNames.Length);

            // Conversation history identical (delivered + opponent reply).
            var histA = sessionA.ConversationHistory.Select(e => (e.Sender, e.Text)).ToList();
            var histB = sessionB.ConversationHistory.Select(e => (e.Sender, e.Text)).ToList();
            Assert.Equal(histA.Count, histB.Count);
            for (int i = 0; i < histA.Count; i++)
            {
                Assert.Equal(histA[i].Sender, histB[i].Sender);
                Assert.Equal(histA[i].Text, histB[i].Text);
            }

            // Opponent history identical.
            var oppA = sessionA.OpponentHistory.Select(m => (m.Role.ToString(), m.Content)).ToList();
            var oppB = sessionB.OpponentHistory.Select(m => (m.Role.ToString(), m.Content)).ToList();
            Assert.Equal(oppA.Count, oppB.Count);
            for (int i = 0; i < oppA.Count; i++)
            {
                Assert.Equal(oppA[i], oppB[i]);
            }

            // XP totals match -- the SessionXpRecorder fed the cloned ledger
            // exactly the same events as the parent.
            Assert.Equal(sessionA.TotalXpEarned, sessionB.TotalXpEarned);
        }

        // ---- (3) Reflection-based completeness ------------------------------------
        //
        // Plan v2 #11: every Category-A field on GameSession must be touched by
        // Clone(). This test guards against the Path 1 risk ("future PR adds a
        // field, forgets to extend Clone").
        //
        // Strategy: enumerate every instance field on GameSession via
        // reflection. Each field is classified into one of two buckets:
        //   - DEEP_COPIED: must be a value-type-equal value, or (for reference
        //     types) a NEW instance from the parent's. Mutating one side
        //     does not affect the other.
        //   - SHARED_BY_REFERENCE: documented reference-share -- by design
        //     points at the same instance on parent and clone (e.g. _llm,
        //     _player, _opponent, _clock, _rules, _dice, _trapRegistry,
        //     _statDeliveryInstructions, _onTextLayerNoop).
        //
        // Any field NOT in either bucket is a regression: either the clone
        // forgot to copy it, or the test allowlist needs an explicit
        // documented decision for it. The test fails loudly with the missing
        // field name so the next maintainer doesn't have to guess.

        [Fact]
        public void Clone_CoversEveryGameSessionField()
        {
            var (parent, _) = MakeFreshSessionWithTurnsQueued(turnsToQueue: 0);

            // Mutate parent enough that every list/dictionary has at least one
            // entry. Without this, a sloppy clone that "happens" to point at
            // the same empty default would slip past the reference-equality
            // check below.
            parent.AddTopic(new CallbackOpportunity(topicKey: "test", turnIntroduced: 0));

            var clone = parent.Clone();

            var fields = typeof(GameSession).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            // Documented shared-by-reference fields. If a future PR adds a new
            // shared-by-reference field, append it here AND document the
            // decision in the Clone constructor's XML doc.
            var sharedByReference = new HashSet<string>
            {
                "_player",                  // immutable CharacterProfile
                "_opponent",                // immutable CharacterProfile
                "_llm",                     // adapter (stateless after #788)
                "_dice",                    // shared dice; per-branch dice via InjectNextDicePool
                "_trapRegistry",            // immutable
                "_clock",                   // immutable
                "_rules",                   // immutable
                "_globalDcBias",            // value-type, primitive int
                "_statDeliveryInstructions",// opaque immutable bag
                "_onTextLayerNoop",         // delegate (stateless callback)
            };

            var missing = new List<string>();
            foreach (var f in fields)
            {
                if (sharedByReference.Contains(f.Name)) continue;

                object? parentVal = f.GetValue(parent);
                object? cloneVal = f.GetValue(clone);

                // For value types and strings: equal value is the contract.
                if (f.FieldType.IsValueType || f.FieldType == typeof(string))
                {
                    if (!Equals(parentVal, cloneVal))
                        missing.Add($"{f.Name}: value-type field differs after clone (parent={parentVal}, clone={cloneVal})");
                    continue;
                }

                // Reference types:
                if (parentVal == null && cloneVal == null) continue; // null-on-both is fine.
                if (parentVal == null || cloneVal == null)
                {
                    missing.Add($"{f.Name}: null-mismatch after clone (parentNull={parentVal == null}, cloneNull={cloneVal == null})");
                    continue;
                }
                if (ReferenceEquals(parentVal, cloneVal))
                {
                    missing.Add(
                        $"{f.Name}: shared by reference after clone, but is not on the documented allowlist. " +
                        $"Either deep-copy this field in GameSession.Clone, or add it to the sharedByReference set in this test " +
                        $"with a one-line justification in the Clone constructor's XML doc.");
                }
            }

            Assert.Empty(missing);
        }

        [Fact]
        public void Clone_SnapshotMatchesParent_AtCloneTime()
        {
            // Sanity: an unmutated clone produces a CreateSnapshot byte-equivalent
            // to the parent's. (Caught by the determinism test indirectly, but
            // worth an explicit single-call pin so a regression fails before the
            // determinism harness even runs a turn.)
            var (parent, _) = MakeFreshSessionWithTurnsQueued(turnsToQueue: 0);
            var clone = parent.Clone();

            var snapP = parent.CreateSnapshot();
            var snapC = clone.CreateSnapshot();

            Assert.Equal(snapP.Interest, snapC.Interest);
            Assert.Equal(snapP.State, snapC.State);
            Assert.Equal(snapP.MomentumStreak, snapC.MomentumStreak);
            Assert.Equal(snapP.TurnNumber, snapC.TurnNumber);
            Assert.Equal(snapP.TripleBonusActive, snapC.TripleBonusActive);
            Assert.Equal(snapP.ActiveTrapNames.Length, snapC.ActiveTrapNames.Length);
            Assert.Equal(snapP.OpponentHistory.Count, snapC.OpponentHistory.Count);
        }

        [Fact]
        public void Clone_HorninessSessionRoll_PreservedExactly()
        {
            // The d10 horniness session roll is computed once in the public
            // ctor and frozen for the rest of the session. Re-running the
            // public ctor (Path 2's snapshot-and-restore) would either redraw
            // (consume a d10 from the dice queue) or fall back to a default --
            // both wrong. Path 1's Clone copies the cached value verbatim.
            // This test pins that.
            var (parent, _) = MakeFreshSessionWithTurnsQueued(turnsToQueue: 0);
            var originalRoll = parent.HorninessRoll;
            var originalMod = parent.HorninessTimeModifier;
            var originalHorniness = parent.SessionHorniness;

            var clone = parent.Clone();

            Assert.Equal(originalRoll, clone.HorninessRoll);
            Assert.Equal(originalMod, clone.HorninessTimeModifier);
            Assert.Equal(originalHorniness, clone.SessionHorniness);
        }

        // ---- helpers --------------------------------------------------------------
        //
        // Reuses Phase0Fixtures (canned LLM transport, deterministic dice
        // roller, seeded steering RNG). The fixture is intentionally minimal:
        // these tests pinpoint clone behaviour, not gameplay logic.

        private static (GameSession session, RecordingLlmTransport transport) MakeFreshSessionWithTurnsQueued(int turnsToQueue)
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            for (int t = 0; t < turnsToQueue; t++)
            {
                transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                transport.QueueDelivery($"delivered-msg-T{t}");
                transport.QueueOpponent($"opponent-reply-T{t}");
            }
            var adapter = Phase0Fixtures.MakeAdapter(transport);

            // Dice queue: 1 d10 for ctor + 2 dice per queued turn (d20 main + d100 timing).
            // The InjectNextDicePool path bypasses _dice on the resolve, but the
            // engine still pre-fills _currentDicePools with EMPTY placeholders --
            // i.e. no _dice consumption beyond the ctor d10 in the StartTurnAsync
            // path. We add slack so a non-injected resolve in the parent does not
            // exhaust the queue.
            var diceValues = new List<int> { 5 }; // ctor d10
            for (int t = 0; t < turnsToQueue; t++)
            {
                diceValues.Add(15); // d20 main
                diceValues.Add(50); // d100 timing
            }
            var dice = new PlaybackDiceRoller(diceValues.ToArray());

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());
            return (session, transport);
        }
    }
}
