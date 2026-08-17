using System;

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

    /// <summary>
    /// Provider-neutral measurement window against a cumulative usage source.
    /// Call-count deltas determine whether the window can be attributed to exactly one call.
    /// Unavailable or throwing providers produce no measurement rather than invented values.
    /// </summary>
    public sealed class TokenUsageMeasurement
    {
        private readonly ITokenUsageProvider? _provider;
        private readonly SessionTokenUsage? _before;

        private TokenUsageMeasurement(ITokenUsageProvider? provider, SessionTokenUsage? before)
        {
            _provider = provider;
            _before = before;
        }

        public static TokenUsageMeasurement Start(object? source)
        {
            var provider = source as ITokenUsageProvider;
            return new TokenUsageMeasurement(provider, Capture(provider));
        }

        public SessionTokenUsage? Complete()
        {
            SessionTokenUsage? after = Capture(_provider);
            if (_before == null || after == null)
            {
                return null;
            }

            return new SessionTokenUsage
            {
                InputTokens = Delta(after.InputTokens, _before.InputTokens),
                OutputTokens = Delta(after.OutputTokens, _before.OutputTokens),
                CacheReadInputTokens = Delta(after.CacheReadInputTokens, _before.CacheReadInputTokens),
                CacheCreationInputTokens = Delta(after.CacheCreationInputTokens, _before.CacheCreationInputTokens),
                CallCount = Delta(after.CallCount, _before.CallCount),
            };
        }

        private static SessionTokenUsage? Capture(ITokenUsageProvider? provider)
        {
            if (provider == null)
            {
                return null;
            }

            try
            {
                SessionTokenUsage usage = provider.GetSessionUsage();
                if (usage == null)
                {
                    return null;
                }

                return new SessionTokenUsage
                {
                    InputTokens = usage.InputTokens,
                    OutputTokens = usage.OutputTokens,
                    CacheReadInputTokens = usage.CacheReadInputTokens,
                    CacheCreationInputTokens = usage.CacheCreationInputTokens,
                    CallCount = usage.CallCount,
                };
            }
            catch
            {
                return null;
            }
        }

        private static int Delta(int after, int before) => Math.Max(0, after - before);
    }
}
