using System;
using System.Threading;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Issue #1041 (Tier C): Tracks speculative-shadow LLM call waste in
    /// <see cref="LlmDispatcher"/> and adapts the dispatch mode to prevent
    /// repeated wasted token consumption.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When both a Trap overlay and a Shadow corruption call are dispatched
    /// in parallel, the shadow result is discarded ("wasted") whenever the
    /// trap call fires and changes the message — because shadow ran against
    /// the original text, not the trap-modified text. In that case shadow
    /// must be re-run sequentially, costing a second LLM round-trip.
    /// </para>
    /// <para>
    /// This tracker uses a sliding-window heuristic:
    /// <list type="bullet">
    ///   <item>After <see cref="WasteThreshold"/> consecutive wasted shadow
    ///   calls, the tracker switches to <em>sequential mode</em>: the
    ///   caller runs Trap first, then Shadow only if Trap did not change the
    ///   message. This eliminates wasted calls at the cost of increased
    ///   overall latency for the (now sequential) happy path.</item>
    ///   <item>After <see cref="RecoveryThreshold"/> consecutive non-wasted
    ///   calls in sequential mode, the tracker reverts to <em>parallel mode</em>
    ///   and speculatively dispatches both overlays again.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Thread-safety: all state is updated via <see cref="Interlocked"/>
    /// primitives so the tracker can be shared across concurrent sessions
    /// without locking.
    /// </para>
    /// </remarks>
    public sealed class SpeculativeWasteTracker
    {
        /// <summary>
        /// Consecutive waste events required to enter sequential mode.
        /// Default 3: after three wasted shadow calls in a row, prefer
        /// sequential dispatch.
        /// </summary>
        public int WasteThreshold { get; }

        /// <summary>
        /// Consecutive non-waste events in sequential mode required to
        /// re-enable parallel dispatch. Default 5.
        /// </summary>
        public int RecoveryThreshold { get; }

        // _consecutiveWastes > 0  → counting wastes toward WasteThreshold.
        // _consecutiveWastes < 0  → in sequential mode, counting non-wastes
        //                           toward RecoveryThreshold (stored as -count).
        private int _counter;

        /// <summary>
        /// Initialises a new <see cref="SpeculativeWasteTracker"/> with the
        /// given thresholds.
        /// </summary>
        /// <param name="wasteThreshold">
        /// Consecutive waste events before switching to sequential mode.
        /// Must be ≥ 1.
        /// </param>
        /// <param name="recoveryThreshold">
        /// Consecutive non-waste events in sequential mode before reverting
        /// to parallel. Must be ≥ 1.
        /// </param>
        public SpeculativeWasteTracker(int wasteThreshold = 3, int recoveryThreshold = 5)
        {
            if (wasteThreshold < 1)
                throw new ArgumentOutOfRangeException(nameof(wasteThreshold), "Must be >= 1.");
            if (recoveryThreshold < 1)
                throw new ArgumentOutOfRangeException(nameof(recoveryThreshold), "Must be >= 1.");

            WasteThreshold = wasteThreshold;
            RecoveryThreshold = recoveryThreshold;
            _counter = 0;
        }

        /// <summary>
        /// Returns <c>true</c> when the tracker recommends running Trap and
        /// Shadow in parallel (normal speculative mode). Returns <c>false</c>
        /// when sequential dispatch is preferred to avoid wasted tokens.
        /// </summary>
        public bool ShouldRunParallel
        {
            get
            {
                // Sequential mode is encoded as _counter <= -WasteThreshold.
                int snap = Volatile.Read(ref _counter);
                return snap > -WasteThreshold;
            }
        }

        /// <summary>
        /// Record that a speculative shadow call was wasted (trap fired and
        /// changed the delivered message). Increments the waste counter;
        /// once <see cref="WasteThreshold"/> is reached, flips into
        /// sequential mode.
        /// </summary>
        public void RecordWaste()
        {
            // In parallel mode (_counter >= 0): increment toward -WasteThreshold.
            // In sequential mode (_counter <= -WasteThreshold): keep counting
            // (stays in sequential mode).
            int prev, next;
            do
            {
                prev = Volatile.Read(ref _counter);
                next = prev <= 0 ? prev - 1 : -1; // restart negative count
            }
            while (Interlocked.CompareExchange(ref _counter, next, prev) != prev);
        }

        /// <summary>
        /// Record that a dispatch completed without waste (either the trap
        /// did not fire, or we were already in sequential mode and ran only
        /// the necessary calls). Counts toward recovery from sequential mode.
        /// </summary>
        public void RecordNonWaste()
        {
            int prev, next;
            do
            {
                prev = Volatile.Read(ref _counter);
                if (prev >= 0)
                {
                    // Already in parallel mode — clamp at 0 (no negative waste credit).
                    next = 0;
                }
                else if (prev <= -WasteThreshold)
                {
                    // In sequential mode: count up toward recovery.
                    next = prev + 1;
                    // If recovery threshold reached, flip back to parallel mode.
                    if (next > -WasteThreshold + RecoveryThreshold)
                        next = 0;
                }
                else
                {
                    // Partial waste count: reset to 0.
                    next = 0;
                }
            }
            while (Interlocked.CompareExchange(ref _counter, next, prev) != prev);
        }

        /// <summary>
        /// Snapshot of the current internal counter for diagnostics.
        /// Positive: waste events since last non-waste.
        /// Zero: clean state (parallel mode, no recent waste).
        /// Negative: consecutive wastes accrued; at or below -<see cref="WasteThreshold"/>
        /// means sequential mode is active.
        /// </summary>
        public int DiagnosticCounter => Volatile.Read(ref _counter);
    }
}
