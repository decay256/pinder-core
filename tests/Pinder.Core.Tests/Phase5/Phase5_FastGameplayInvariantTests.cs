using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Tests.Phase0;
using Xunit;

namespace Pinder.Core.Tests.Phase5
{
    /// <summary>
    /// Issue #425 (Phase 5 of #393): master invariant tests for
    /// fast-gameplay. Pin the contract that a session resolved via
    /// fast-gameplay's clone+adopt path produces byte-equivalent
    /// post-state to a session resolved without it, given the same
    /// dice seed and same chosen option.
    /// </summary>
    [Trait("Category", "Phase5")]
    public sealed class Phase5_FastGameplayInvariantTests
    {
        // 1. Master invariant: same option + same dice POOL -> same post-state.
        // The pool is the unit of determinism (per #789 Phase 2). Both paths
        // here inject the SAME PerOptionDicePool, so any state divergence
        // post-resolve indicates a clone-vs-direct semantic mismatch.
        [Fact]
        public async Task FastGameplayResolve_ByteEqualToNonSpeculative_ForSameOption()
        {
            var (parentA, transportA) = MakeFreshParent();
            var (parentB, transportB) = MakeFreshParent();

            transportA.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transportA.QueueDelivery("d-T0");
            transportA.QueueOpponent("o-T0");
            transportB.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transportB.QueueDelivery("d-T0");
            transportB.QueueOpponent("o-T0");

            var startA = await parentA.StartTurnAsync();
            var startB = await parentB.StartTurnAsync();
            Assert.Equal(startA.Options.Length, startB.Options.Length);

            const int chosen = 1;
            // Build a deterministic pool both sides will inject. The pool
            // size matches the engine's worst-case budget (1 d20 + 1 d100
            // for a no-advantage path).
            var sharedPool = new Pinder.Core.Rolls.PerOptionDicePool(chosen, 14, 50);

            // Path A: inject the pool, do a non-speculative resolve.
            parentA.InjectNextDicePool(sharedPool);
            var resultA = await parentA.ResolveTurnAsync(chosen);
            var snapA = parentA.CreateSnapshot();

            // Path B: clone the parent, inject the SAME pool on the clone,
            // resolve on the clone, adopt back into the parent.
            var cloneB = parentB.Clone();
            cloneB.InjectNextDicePool(sharedPool);
            var resultB = await cloneB.ResolveTurnAsync(chosen);
            parentB.AdoptStateFrom(cloneB);
            var snapB = parentB.CreateSnapshot();

            // Master invariant: post-state matches.
            Assert.Equal(snapA.Interest, snapB.Interest);
            Assert.Equal(snapA.State, snapB.State);
            Assert.Equal(snapA.MomentumStreak, snapB.MomentumStreak);
            Assert.Equal(snapA.TurnNumber, snapB.TurnNumber);
            Assert.Equal(snapA.TripleBonusActive, snapB.TripleBonusActive);
            Assert.Equal(snapA.OpponentHistory.Count, snapB.OpponentHistory.Count);
            Assert.Equal(resultA.IsGameOver, resultB.IsGameOver);
            Assert.Equal(resultA.DeliveredMessage, resultB.DeliveredMessage);
        }

        // 2. No-trace: discarded clones do not perturb the parent
        [Fact]
        public async Task DiscardedClones_DoNotPerturbParent()
        {
            var (parent, transport) = MakeFreshParent();
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            for (int i = 0; i < 3; i++)
            {
                transport.QueueDelivery($"d-discard-{i}");
                transport.QueueOpponent($"o-discard-{i}");
            }

            await parent.StartTurnAsync();
            var preSnap = parent.CreateSnapshot();
            var prePools = parent.EnsureAllDicePoolsFilled();

            for (int i = 0; i < 3; i++)
            {
                var clone = parent.Clone();
                clone.InjectNextDicePool(prePools[i]);
                await clone.ResolveTurnAsync(i);
                // Drop without adopting.
            }

            var postSnap = parent.CreateSnapshot();
            Assert.Equal(preSnap.Interest, postSnap.Interest);
            Assert.Equal(preSnap.TurnNumber, postSnap.TurnNumber);
            Assert.Equal(preSnap.MomentumStreak, postSnap.MomentumStreak);
            Assert.Equal(preSnap.OpponentHistory.Count, postSnap.OpponentHistory.Count);
        }

        // 3. AdoptStateFrom round-trip
        [Fact]
        public async Task AdoptStateFrom_AfterResolve_ParentMatchesDirectResolve()
        {
            var (pA, tA) = MakeFreshParent();
            var (pB, tB) = MakeFreshParent();

            tA.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            tA.QueueDelivery("d-A");
            tA.QueueOpponent("o-A");
            tB.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            tB.QueueDelivery("d-A");
            tB.QueueOpponent("o-A");

            await pA.StartTurnAsync();
            await pB.StartTurnAsync();

            const int idx = 2;
            var sharedPool = new Pinder.Core.Rolls.PerOptionDicePool(idx, 12, 40);

            pA.InjectNextDicePool(sharedPool);
            var direct = await pA.ResolveTurnAsync(idx);

            var clone = pB.Clone();
            clone.InjectNextDicePool(sharedPool);
            var viaClone = await clone.ResolveTurnAsync(idx);
            pB.AdoptStateFrom(clone);

            var sA = pA.CreateSnapshot();
            var sB = pB.CreateSnapshot();
            Assert.Equal(sA.Interest, sB.Interest);
            Assert.Equal(sA.TurnNumber, sB.TurnNumber);
            Assert.Equal(sA.MomentumStreak, sB.MomentumStreak);
            Assert.Equal(sA.OpponentHistory.Count, sB.OpponentHistory.Count);
            Assert.Equal(direct.DeliveredMessage, viaClone.DeliveredMessage);
        }

        // 4. Distinct clones with distinct adapters route to own transports
        [Fact]
        public async Task DistinctClonesWithDistinctAdapters_RouteToOwnTransports()
        {
            var (parent, parentTransport) = MakeFreshParent();
            parentTransport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            await parent.StartTurnAsync();

            var t1 = new RecordingLlmTransport { DefaultResponse = "" };
            t1.QueueDelivery("d-clone1");
            t1.QueueOpponent("o-clone1");
            var t2 = new RecordingLlmTransport { DefaultResponse = "" };
            t2.QueueDelivery("d-clone2");
            t2.QueueOpponent("o-clone2");

            var pools = parent.EnsureAllDicePoolsFilled();

            var c1 = parent.Clone(Phase0Fixtures.MakeAdapter(t1));
            c1.InjectNextDicePool(pools[0]);
            var r1 = await c1.ResolveTurnAsync(0);

            var c2 = parent.Clone(Phase0Fixtures.MakeAdapter(t2));
            c2.InjectNextDicePool(pools[1]);
            var r2 = await c2.ResolveTurnAsync(1);

            Assert.Equal("d-clone1", r1.DeliveredMessage);
            Assert.Equal("d-clone2", r2.DeliveredMessage);
        }

        // 5. Concurrency stress: independent clones, parallel resolves, no corruption
        [Fact]
        public async Task ConcurrentResolveOnIndependentClones_NoStateCorruption()
        {
            var (parent, parentTransport) = MakeFreshParent();
            parentTransport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            await parent.StartTurnAsync();
            var pools = parent.EnsureAllDicePoolsFilled();

            var clones = new List<GameSession>();
            for (int i = 0; i < 8; i++)
            {
                var t = new RecordingLlmTransport { DefaultResponse = "" };
                t.QueueDelivery($"d{i}");
                t.QueueOpponent($"o{i}");
                var c = parent.Clone(Phase0Fixtures.MakeAdapter(t));
                c.InjectNextDicePool(pools[i % pools.Length]);
                clones.Add(c);
            }

            var tasks = clones
                .Select((c, i) => Task.Run(() => c.ResolveTurnAsync(i % pools.Length)))
                .ToList();
            var results = await Task.WhenAll(tasks);
            Assert.Equal(8, results.Length);
            for (int i = 0; i < 8; i++)
            {
                Assert.Equal($"d{i}", results[i].DeliveredMessage);
            }

            // Parent untouched.
            var snap = parent.CreateSnapshot();
            Assert.Equal(0, snap.TurnNumber);
        }

        // 6. EnsureAllDicePoolsFilled is idempotent
        [Fact]
        public async Task EnsureAllDicePoolsFilled_Idempotent()
        {
            var (parent, transport) = MakeFreshParent();
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            await parent.StartTurnAsync();

            var pools1 = parent.EnsureAllDicePoolsFilled();
            var snapshotValues1 = pools1.Select(p => string.Join(",", p.Values)).ToList();
            var pools2 = parent.EnsureAllDicePoolsFilled();
            var snapshotValues2 = pools2.Select(p => string.Join(",", p.Values)).ToList();

            // Same pool object array, same values \u2014 second call is a no-op.
            Assert.Same(pools1, pools2);
            Assert.Equal(snapshotValues1, snapshotValues2);
        }

        // helpers
        private static (GameSession session, RecordingLlmTransport transport) MakeFreshParent()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var diceValues = new List<int>();
            diceValues.Add(5); // ctor d10
            // Plenty of headroom for fills + extra resolves.
            for (int i = 0; i < 256; i++) diceValues.Add(10 + (i % 7));
            var dice = new Pinder.Core.Tests.Phase0.PlaybackDiceRoller(diceValues.ToArray());
            var session = new GameSession(
                Phase0Fixtures.MakeProfile("P"),
                Phase0Fixtures.MakeProfile("O"),
                adapter, dice, new Pinder.Core.Traps.NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());
            return (session, transport);
        }
    }
}
