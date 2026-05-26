namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Configurable time-of-day horniness modifiers loaded from game-definition.yaml.
    /// </summary>
    public sealed class HorninessTimeModifiers
    {
        /// <summary>Modifier for 09:00–11:59.</summary>
        public int Morning { get; }

        /// <summary>Modifier for 12:00–17:59.</summary>
        public int Afternoon { get; }

        /// <summary>Modifier for 18:00–23:59.</summary>
        public int Evening { get; }

        /// <summary>Modifier for 00:00–08:59.</summary>
        public int Overnight { get; }

        public HorninessTimeModifiers(int morning, int afternoon, int evening, int overnight)
        {
            Morning = morning;
            Afternoon = afternoon;
            Evening = evening;
            Overnight = overnight;
        }
    }
}
