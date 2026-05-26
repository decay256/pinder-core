using System;
using System.Collections.Generic;

namespace Pinder.Core.Prompts
{
    public static partial class TextingStyleAggregator
    {
        // ------------------------------------------------------------------
        // Audit log entry — one per dropped (axis, value) pair.
        // #907: surfaced so callers can log which fragments were rejected
        // at session-creation time.
        // ------------------------------------------------------------------

        /// <summary>
        /// Records a single fragment that was rejected by the conflict resolver.
        /// </summary>
        public sealed class ConflictDropEntry
        {
            /// <summary>Character id (the <c>seedKey</c> passed by the caller).</summary>
            public string? CharacterId   { get; }
            /// <summary>The axis whose value was dropped.</summary>
            public string  Axis          { get; }
            /// <summary>The value that was dropped.</summary>
            public string  DroppedValue  { get; }
            /// <summary>The axis whose kept value triggered the drop.</summary>
            public string  ConflictAxis  { get; }
            /// <summary>The value that was already kept when the conflict fired.</summary>
            public string  KeptValue     { get; }
            /// <summary>Human-readable reason from the conflict matrix.</summary>
            public string  Reason        { get; }

            public ConflictDropEntry(
                string? characterId,
                string  axis,
                string  droppedValue,
                string  conflictAxis,
                string  keptValue,
                string  reason)
            {
                CharacterId  = characterId;
                Axis         = axis;
                DroppedValue = droppedValue;
                ConflictAxis = conflictAxis;
                KeptValue    = keptValue;
                Reason       = reason;
            }

            public override string ToString() =>
                $"[ConflictDrop] char={CharacterId ?? "(unknown)"} " +
                $"dropped={Axis}:{DroppedValue} " +
                $"conflict_with={ConflictAxis}:{KeptValue} " +
                $"reason=\"{Reason}\"";
        }

        /// <summary>
        /// Result of conflict-aware aggregation: the resolved axis lines
        /// plus the audit log of dropped fragments.
        /// </summary>
        public sealed class AggregationResult
        {
            public IReadOnlyList<string>           Lines   { get; }
            public IReadOnlyList<ConflictDropEntry> Drops  { get; }

            public AggregationResult(
                IReadOnlyList<string>           lines,
                IReadOnlyList<ConflictDropEntry> drops)
            {
                Lines = lines;
                Drops = drops;
            }
        }
    }
}
