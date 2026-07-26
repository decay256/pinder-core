using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Compact typed facts for compiling private DATEE emotional reaction input.
    /// It deliberately excludes dice totals, modifiers, force labels, and other
    /// roll mechanics that prompt assembly should not see.
    /// </summary>
    public sealed class DateeEmotionalTurnEvent
    {
        public DateeEmotionalTurnEvent(
            StatType selectedStat,
            RollOutcomeIntensity outcomeIntensity,
            IReadOnlyDictionary<string, string>? therapistDiagnosis)
        {
            if (!Enum.IsDefined(typeof(StatType), selectedStat))
                throw new ArgumentException("Unknown selected stat.", nameof(selectedStat));
            if (!Enum.IsDefined(typeof(RollOutcomeIntensity), outcomeIntensity))
                throw new ArgumentException("Unknown roll outcome intensity.", nameof(outcomeIntensity));

            SelectedStat = selectedStat;
            OutcomeIntensity = outcomeIntensity;
            TherapistDiagnosis = SnapshotDiagnosis(therapistDiagnosis);
        }

        public StatType SelectedStat { get; }

        public RollOutcomeIntensity OutcomeIntensity { get; }

        public IReadOnlyDictionary<string, string>? TherapistDiagnosis { get; }

        private static IReadOnlyDictionary<string, string>? SnapshotDiagnosis(
            IReadOnlyDictionary<string, string>? diagnosis)
        {
            if (diagnosis == null)
                return null;

            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in diagnosis)
                snapshot.Add(pair.Key, pair.Value);

            return new ReadOnlyDictionary<string, string>(snapshot);
        }
    }
}
