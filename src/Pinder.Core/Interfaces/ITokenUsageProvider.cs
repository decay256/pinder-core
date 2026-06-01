namespace Pinder.Core.Interfaces
{
    public sealed class SessionTokenUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CacheReadInputTokens { get; set; }
        public int CacheCreationInputTokens { get; set; }
        public int CallCount { get; set; }

        public int TotalBilledInput => InputTokens + CacheCreationInputTokens;
    }

    public interface ITokenUsageProvider
    {
        SessionTokenUsage GetSessionUsage();
    }
}
