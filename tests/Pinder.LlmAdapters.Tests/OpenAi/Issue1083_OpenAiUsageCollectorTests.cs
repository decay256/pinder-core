using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests.OpenAi
{
    public class Issue1083_OpenAiUsageCollectorTests
    {
        [Fact]
        public void Collector_WithAbsentPromptTokensDetails_AccumulatesCorrectly()
        {
            // Arrange
            var collector = new OpenAiUsageCollector();
            var response1 = JObject.Parse(@"{
                ""model"": ""groq-model-1"",
                ""usage"": {
                    ""prompt_tokens"": 120,
                    ""completion_tokens"": 45
                }
            }");

            // Act
            collector.Collect(response1);

            // Assert
            var usage = collector.GetSessionUsage();
            Assert.Equal(1, usage.CallCount);
            Assert.Equal(120, usage.InputTokens);
            Assert.Equal(45, usage.OutputTokens);
            Assert.Equal(0, usage.CacheReadInputTokens);
            Assert.Equal(0, usage.CacheCreationInputTokens);
            Assert.Equal(120, usage.TotalBilledInput);
        }

        [Fact]
        public void Collector_WithMultipleCallsAndCachedTokens_AccumulatesCorrectly()
        {
            // Arrange
            var collector = new OpenAiUsageCollector();
            var response1 = JObject.Parse(@"{
                ""model"": ""openai-gpt-4o"",
                ""usage"": {
                    ""prompt_tokens"": 150,
                    ""completion_tokens"": 50,
                    ""prompt_tokens_details"": {
                        ""cached_tokens"": 30
                    }
                }
            }");
            var response2 = JObject.Parse(@"{
                ""model"": ""openai-gpt-4o"",
                ""usage"": {
                    ""prompt_tokens"": 200,
                    ""completion_tokens"": 80,
                    ""prompt_tokens_details"": {
                        ""cached_tokens"": 60
                    }
                }
            }");

            // Act
            collector.Collect(response1);
            collector.Collect(response2);

            // Assert
            var usage = collector.GetSessionUsage();
            Assert.Equal(2, usage.CallCount);
            Assert.Equal(350, usage.InputTokens);
            Assert.Equal(130, usage.OutputTokens);
            Assert.Equal(90, usage.CacheReadInputTokens);
            Assert.Equal(0, usage.CacheCreationInputTokens);
            Assert.Equal(350, usage.TotalBilledInput);

            var stats = collector.GetCallStats();
            Assert.Equal(2, stats.Count);
            Assert.Equal("openai-gpt-4o", stats[0].Model);
            Assert.Equal(150, stats[0].PromptTokens);
            Assert.Equal(50, stats[0].CompletionTokens);
            Assert.Equal(30, stats[0].CachedTokens);
        }

        [Fact]
        public void Collector_WhenUsageKeyIsAbsent_IsSafeAndIncrementsCallCount()
        {
            // Arrange
            var collector = new OpenAiUsageCollector();
            var response = JObject.Parse(@"{
                ""model"": ""openai-gpt-4o""
            }");

            // Act
            collector.Collect(response);

            // Assert
            var usage = collector.GetSessionUsage();
            Assert.Equal(1, usage.CallCount);
            Assert.Equal(0, usage.InputTokens);
            Assert.Equal(0, usage.OutputTokens);
            Assert.Equal(0, usage.CacheReadInputTokens);
            Assert.Equal(0, usage.CacheCreationInputTokens);
        }

        [Fact]
        public async Task OpenAiTransport_TracksUsageCorrectly_AcrossMultipleCalls()
        {
            // Arrange
            var responseQueue = new System.Collections.Generic.Queue<string>();
            responseQueue.Enqueue(@"{
                ""choices"": [{ ""message"": { ""content"": ""response-1"" } }],
                ""model"": ""mock-model"",
                ""usage"": {
                    ""prompt_tokens"": 100,
                    ""completion_tokens"": 40,
                    ""prompt_tokens_details"": {
                        ""cached_tokens"": 25
                    }
                }
            }");
            responseQueue.Enqueue(@"{
                ""choices"": [{ ""message"": { ""content"": ""response-2"" } }],
                ""model"": ""mock-model"",
                ""usage"": {
                    ""prompt_tokens"": 120,
                    ""completion_tokens"": 50,
                    ""prompt_tokens_details"": {
                        ""cached_tokens"": 35
                    }
                }
            }");

            var handler = new QueueHttpMessageHandler(responseQueue);
            using var http = new HttpClient(handler);
            using var transport = new OpenAiTransport("sk-test", "https://example.test", "mock-model", http);

            // Act
            var res1 = await transport.SendAsync("sys1", "usr1");
            var res2 = await transport.SendAsync("sys2", "usr2");

            // Assert
            Assert.Equal("response-1", res1);
            Assert.Equal("response-2", res2);

            var usageProvider = transport as ITokenUsageProvider;
            Assert.NotNull(usageProvider);

            var usage = usageProvider.GetSessionUsage();
            Assert.Equal(2, usage.CallCount);
            Assert.Equal(220, usage.InputTokens);
            Assert.Equal(90, usage.OutputTokens);
            Assert.Equal(60, usage.CacheReadInputTokens);
            Assert.Equal(0, usage.CacheCreationInputTokens);
            Assert.Equal(220, usage.TotalBilledInput);
        }

        private sealed class QueueHttpMessageHandler : HttpMessageHandler
        {
            private readonly System.Collections.Generic.Queue<string> _responses;

            public QueueHttpMessageHandler(System.Collections.Generic.Queue<string> responses)
            {
                _responses = responses;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var responseBody = _responses.Count > 0 ? _responses.Dequeue() : "{}";
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(resp);
            }
        }
    }
}
