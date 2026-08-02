namespace Pinder.SessionRunner
{
    /// <summary>Provider-neutral token summary for one simulated-player call.</summary>
    public sealed class CallSummaryStat
    {
        public int Turn { get; set; }
        public string Type { get; set; } = "";
        public int CacheCreationInputTokens { get; set; }
        public int CacheReadInputTokens { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }
}
