namespace Pinder.SessionSetup
{
    /// <summary>
    /// Centralized registry for default LLM generation parameters (temperature)
    /// across all generators within <c>Pinder.SessionSetup</c>.
    /// </summary>
    public static class GeneratorDefaultConfigs
    {
        public static class DramaticArc
        {
            public const double Temperature = 0.85;
        }

        public static class Backstory
        {
            public const double Temperature = 0.7;
        }

        public static class Stake
        {
            public const double Temperature = 0.9;
        }

        public static class Background
        {
            public const double Temperature = 0.8;
        }

        public static class Outfit
        {
            public const double Temperature = 0.8;
        }
    }
}
