using System;
using Pinder.Core.Rolls;
using Xunit;

namespace Pinder.Core.Tests.Phase4
{
    /// <summary>
    /// Unit pin for <see cref="CloneableRandom"/> — the owned, reflection-free
    /// PRNG that replaced the old reflection-based <c>RandomCloner</c>
    /// (audit 2026-07-10, High severity: "Core fast-gameplay cloning depends on
    /// private System.Random internals").
    ///
    /// <para>
    /// Cloning is correct iff:
    /// </para>
    /// <list type="number">
    ///   <item>The clone produces the SAME next-N sequence as the parent at
    ///   clone-time (next-sequence equivalence at the moment of fork).</item>
    ///   <item>Advancing the parent does NOT shift the clone's sequence
    ///   (independence after fork).</item>
    ///   <item>Advancing the clone does NOT shift the parent's sequence
    ///   (independence in either direction).</item>
    /// </list>
    ///
    /// <para>
    /// These are the exact properties <c>GameSession.Clone()</c> /
    /// <c>AdoptStateFrom</c> rely on for the fast-gameplay scheduler's
    /// independent speculative branches (#425 / #790).
    /// </para>
    /// </summary>
    [Trait("Category", "Phase4")]
    public class CloneableRandomTests
    {
        [Fact]
        public void Clone_ProducesIdenticalNextSequence_AtCloneTime()
        {
            var parent = new CloneableRandom(1234);
            // Advance parent by a non-trivial number of steps so we're not
            // just testing seed-state.
            for (int i = 0; i < 7; i++) parent.Next(100);

            var clone = parent.Clone();

            for (int i = 0; i < 16; i++)
            {
                int p = parent.Next(int.MaxValue);
                int c = clone.Next(int.MaxValue);
                Assert.Equal(p, c);
            }
        }

        [Fact]
        public void Clone_IsIndependent_FromParentAdvancement()
        {
            var parent = new CloneableRandom(99);
            for (int i = 0; i < 3; i++) parent.Next();

            // Capture clone's expected next-3 BEFORE advancing parent, via a
            // second snapshot taken at the same instant as `clone`.
            var prediction = parent.Clone();
            var clone = parent.Clone();
            var expectedClone = new int[3];
            for (int i = 0; i < 3; i++) expectedClone[i] = prediction.Next(1_000_000);

            // Advance parent a lot.
            for (int i = 0; i < 100; i++) parent.Next();

            // Clone still produces the expected sequence — parent's mutation
            // had no effect on clone's internal state.
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(expectedClone[i], clone.Next(1_000_000));
            }
        }

        [Fact]
        public void Clone_IsIndependent_FromCloneAdvancement()
        {
            var parent = new CloneableRandom(7);
            for (int i = 0; i < 5; i++) parent.Next();

            // Predict parent's next-3 by cloning before advancing the clone.
            var prediction = parent.Clone();
            var expectedParent = new[] { prediction.Next(1_000), prediction.Next(1_000), prediction.Next(1_000) };

            var clone = parent.Clone();
            for (int i = 0; i < 100; i++) clone.Next();

            // Parent unaffected by clone's advancement.
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(expectedParent[i], parent.Next(1_000));
            }
        }

        [Fact]
        public void TwoInstances_WithDifferentSeeds_DivergeImmediately()
        {
            var a = new CloneableRandom(1);
            var b = new CloneableRandom(2);

            bool anyDifferent = false;
            for (int i = 0; i < 8; i++)
            {
                if (a.Next(int.MaxValue) != b.Next(int.MaxValue)) anyDifferent = true;
            }
            Assert.True(anyDifferent, "Distinct seeds should not produce identical sequences.");
        }

        [Fact]
        public void SameSeed_ProducesIdenticalSequence_Deterministically()
        {
            var a = new CloneableRandom(4242);
            var b = new CloneableRandom(4242);

            for (int i = 0; i < 32; i++)
            {
                Assert.Equal(a.Next(1, 21), b.Next(1, 21));
            }
        }

        [Fact]
        public void Rolls_StayWithinRequestedRange()
        {
            var rng = new CloneableRandom(2026);
            for (int i = 0; i < 5000; i++)
            {
                int roll = rng.Next(1, 21); // d20 range, matches RandomDiceRollerAdapter usage
                Assert.InRange(roll, 1, 20);
            }
        }

        // ── CloneableRandom.RequireCloneable — the fail-fast contract used by
        // GameSession.Clone()/AdoptStateFrom ────────────────────────────────

        [Fact]
        public void RequireCloneable_WithCloneableRandom_ReturnsIndependentClone()
        {
            var src = new CloneableRandom(55);
            var required = CloneableRandom.RequireCloneable(src, "SteeringRng");

            // Same sequence at the moment of the call...
            Assert.Equal(src.Next(1000), required.Next(1000));

            // Predict required's next draw BEFORE advancing src, so the
            // prediction is independent of whatever src does next.
            var expectedRequiredNext = required.Clone().Next(1000);

            // ...independent thereafter: advancing src must not perturb required.
            for (int i = 0; i < 50; i++) src.Next();

            Assert.Equal(expectedRequiredNext, required.Next(1000));
        }

        [Fact]
        public void RequireCloneable_WithPlainSystemRandom_ThrowsInvalidOperationException()
        {
            var plainRandom = new Random(0);
            var ex = Assert.Throws<InvalidOperationException>(
                () => CloneableRandom.RequireCloneable(plainRandom, "SteeringRng"));
            Assert.Contains(nameof(CloneableRandom), ex.Message);
        }

        [Fact]
        public void RequireCloneable_WithNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => CloneableRandom.RequireCloneable(null!, "SteeringRng"));
        }
    }
}
