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
    }
}
