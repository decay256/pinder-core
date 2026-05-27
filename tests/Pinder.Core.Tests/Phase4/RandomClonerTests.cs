using System;
using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests.Phase4
{
    /// <summary>
    /// Unit pin for <see cref="RandomCloner"/> \u2014 the deep-copy helper that
    /// underpins <see cref="GameSession.Clone"/>'s RNG independence guarantee.
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
    /// </summary>
    [Trait("Category", "Phase4")]
    public class RandomClonerTests
    {
        [Fact]
        public void Clone_ProducesIdenticalNextSequence_AtCloneTime()
        {
            var parent = new Random(1234);
            // Advance parent by a non-trivial number of steps so we're not
            // just testing seed-state.
            for (int i = 0; i < 7; i++) parent.Next(100);

            var clone = RandomCloner.Clone(parent);

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
            var parent = new Random(99);
            for (int i = 0; i < 3; i++) parent.Next();
            var clone = RandomCloner.Clone(parent);

            // Capture clone's expected next-3 BEFORE advancing parent.
            var expectedClone = new[] { 0, 0, 0 };
            // Use a second snapshot for prediction.
            var prediction = RandomCloner.Clone(parent);
            for (int i = 0; i < 3; i++) expectedClone[i] = prediction.Next(1_000_000);

            // Advance parent a lot.
            for (int i = 0; i < 100; i++) parent.Next();

            // Clone still produces the expected sequence \u2014 parent's mutation
            // had no effect on clone's internal state.
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(expectedClone[i], clone.Next(1_000_000));
            }
        }

        [Fact]
        public void Clone_IsIndependent_FromCloneAdvancement()
        {
            var parent = new Random(7);
            for (int i = 0; i < 5; i++) parent.Next();

            // Predict parent's next-3 by cloning before advancing the clone.
            var prediction = RandomCloner.Clone(parent);
            var expectedParent = new[] { prediction.Next(1_000), prediction.Next(1_000), prediction.Next(1_000) };

            var clone = RandomCloner.Clone(parent);
            for (int i = 0; i < 100; i++) clone.Next();

            // Parent unaffected by clone's advancement.
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(expectedParent[i], parent.Next(1_000));
            }
        }

        [Fact]
        public void Clone_NullArgument_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RandomCloner.Clone(null!));
        }

        /// <summary>
        /// Issue #1041 (Tier C): Regression guard for the field-cache optimisation.
        /// Verifies that repeated Clone() calls on the same source (hitting the
        /// ConcurrentDictionary warm-cache path) still produce identical sequences
        /// to the parent at each clone-time, and remain fully independent.
        /// </summary>
        [Fact]
        public void Clone_RepeatedCalls_CachedPathPreservesCorrectness()
        {
            var src = new Random(5678);
            for (int i = 0; i < 20; i++) src.Next();

            // 10 rapid clones — all should hit the warm cache after the first.
            for (int run = 0; run < 10; run++)
            {
                var parent = RandomCloner.Clone(src);
                var clone  = RandomCloner.Clone(parent);

                // Clone must produce same sequence as parent at clone-time.
                for (int i = 0; i < 8; i++)
                {
                    Assert.Equal(parent.Next(int.MaxValue), clone.Next(int.MaxValue));
                }
            }
        }
    }
}
