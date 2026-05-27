using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #1041 (Tier C): Unit tests for <see cref="SpeculativeWasteTracker"/>.
    /// </summary>
    public class SpeculativeWasteTrackerTests
    {
        [Fact]
        public void InitialState_IsParallelMode()
        {
            var tracker = new SpeculativeWasteTracker();
            Assert.True(tracker.ShouldRunParallel);
        }

        [Fact]
        public void BelowWasteThreshold_StaysParallel()
        {
            var tracker = new SpeculativeWasteTracker(wasteThreshold: 3);
            tracker.RecordWaste();
            tracker.RecordWaste();
            Assert.True(tracker.ShouldRunParallel,
                "Should remain parallel after fewer wastes than threshold.");
        }

        [Fact]
        public void AtWasteThreshold_SwitchesToSequential()
        {
            var tracker = new SpeculativeWasteTracker(wasteThreshold: 3);
            tracker.RecordWaste();
            tracker.RecordWaste();
            tracker.RecordWaste(); // 3rd waste — hits threshold
            Assert.False(tracker.ShouldRunParallel,
                "Should switch to sequential mode at waste threshold.");
        }

        [Fact]
        public void SequentialMode_NonWasteEvents_EvenuallyRestoresParallel()
        {
            var tracker = new SpeculativeWasteTracker(wasteThreshold: 3, recoveryThreshold: 2);
            tracker.RecordWaste();
            tracker.RecordWaste();
            tracker.RecordWaste(); // enter sequential
            Assert.False(tracker.ShouldRunParallel);

            tracker.RecordNonWaste(); // 1 of 2
            tracker.RecordNonWaste(); // 2 of 2 — should recover
            Assert.True(tracker.ShouldRunParallel,
                "Should recover to parallel mode after recovery threshold non-wastes.");
        }

        [Fact]
        public void RecordNonWaste_InParallelMode_KeepsParallel()
        {
            var tracker = new SpeculativeWasteTracker();
            tracker.RecordNonWaste();
            tracker.RecordNonWaste();
            Assert.True(tracker.ShouldRunParallel);
        }

        [Fact]
        public void AfterRecovery_NewWastes_CanReEnterSequential()
        {
            var tracker = new SpeculativeWasteTracker(wasteThreshold: 2, recoveryThreshold: 1);
            // Enter sequential
            tracker.RecordWaste();
            tracker.RecordWaste();
            Assert.False(tracker.ShouldRunParallel);
            // Recover
            tracker.RecordNonWaste();
            Assert.True(tracker.ShouldRunParallel);
            // Enter sequential again
            tracker.RecordWaste();
            tracker.RecordWaste();
            Assert.False(tracker.ShouldRunParallel);
        }

        [Fact]
        public void DiagnosticCounter_ReflectsInternalState()
        {
            var tracker = new SpeculativeWasteTracker(wasteThreshold: 3);
            Assert.Equal(0, tracker.DiagnosticCounter);
            tracker.RecordWaste();
            Assert.True(tracker.DiagnosticCounter < 0,
                "Negative counter indicates waste accumulation.");
        }

        [Fact]
        public void Constructor_InvalidWasteThreshold_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new SpeculativeWasteTracker(wasteThreshold: 0));
        }

        [Fact]
        public void Constructor_InvalidRecoveryThreshold_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new SpeculativeWasteTracker(recoveryThreshold: 0));
        }
    }
}
