using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters.OpenAi
{
    internal sealed class OpenAiUsageCollector
    {
        private readonly List<OpenAiCallStat> _callStats = new List<OpenAiCallStat>();
        private readonly object _lock = new object();

        public void Collect(JObject responseJson)
        {
            if (responseJson == null) return;

            lock (_lock)
            {
                var usage = responseJson["usage"];
                int promptTokens = 0;
                int completionTokens = 0;
                int cachedTokens = 0;

                if (usage != null)
                {
                    promptTokens = usage["prompt_tokens"]?.Value<int>() ?? 0;
                    completionTokens = usage["completion_tokens"]?.Value<int>() ?? 0;
                    
                    var promptTokensDetails = usage["prompt_tokens_details"];
                    cachedTokens = promptTokensDetails?["cached_tokens"]?.Value<int>() ?? 0;
                }

                var model = responseJson["model"]?.Value<string>() ?? string.Empty;

                _callStats.Add(new OpenAiCallStat
                {
                    Model = model,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    CachedTokens = cachedTokens
                });
            }
        }

        public SessionTokenUsage GetSessionUsage()
        {
            lock (_lock)
            {
                return new SessionTokenUsage
                {
                    InputTokens = _callStats.Sum(s => s.PromptTokens),
                    OutputTokens = _callStats.Sum(s => s.CompletionTokens),
                    CacheReadInputTokens = _callStats.Sum(s => s.CachedTokens),
                    CacheCreationInputTokens = 0,
                    CallCount = _callStats.Count
                };
            }
        }

        public IReadOnlyList<OpenAiCallStat> GetCallStats()
        {
            lock (_lock)
            {
                return _callStats.AsReadOnly();
            }
        }
    }

    internal sealed class OpenAiCallStat
    {
        public string Model { get; set; } = string.Empty;
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int CachedTokens { get; set; }
    }
}
