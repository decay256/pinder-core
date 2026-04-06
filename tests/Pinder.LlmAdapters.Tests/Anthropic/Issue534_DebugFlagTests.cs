using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class Issue534_DebugFlagTests : IDisposable
    {
        private readonly string _debugDir;

        public Issue534_DebugFlagTests()
        {
            _debugDir = Path.Combine(Path.GetTempPath(), "pinder_debug_tests_" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_debugDir))
            {
                Directory.Delete(_debugDir, true);
            }
        }

        private static HttpResponseMessage MakeOptionsSuccessResponse()
        {
            var body = @"{
                ""id"": ""msg_123"",
                ""type"": ""message"",
                ""role"": ""assistant"",
                ""content"": [
                    { ""type"": ""text"", ""text"": ""<A>Option A</A><B>Option B</B><C>Option C</C><D>Option D</D>"" }
                ],
                ""model"": ""claude-test"",
                ""stop_reason"": ""end_turn"",
                ""stop_sequence"": null,
                ""usage"": {
                    ""input_tokens"": 10,
                    ""output_tokens"": 5,
                    ""cache_creation_input_tokens"": 20,
                    ""cache_read_input_tokens"": 30
                }
            }";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage MakeDeliverySuccessResponse()
        {
            var body = @"{
                ""id"": ""msg_124"",
                ""type"": ""message"",
                ""role"": ""assistant"",
                ""content"": [
                    { ""type"": ""text"", ""text"": ""Delivered message"" }
                ],
                ""model"": ""claude-test"",
                ""stop_reason"": ""end_turn"",
                ""stop_sequence"": null,
                ""usage"": {
                    ""input_tokens"": 15,
                    ""output_tokens"": 10,
                    ""cache_creation_input_tokens"": 0,
                    ""cache_read_input_tokens"": 50
                }
            }";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }

        private static DialogueContext MakeContext()
        {
            var history = new System.Collections.Generic.List<(string, string)>();
            return new DialogueContext(
                "Player Prompt", "Opponent Prompt", history, "Last message",
                new System.Collections.Generic.List<string>(), 10);
        }

        private static DeliveryContext MakeDeliveryContext()
        {
            var history = new System.Collections.Generic.List<(string, string)>();
            var option = new DialogueOption(StatType.Charm, "Test", 0, null, false);
            return new DeliveryContext(
                "Player Prompt", "Opponent Prompt", history, "Last message", option, Pinder.Core.Rolls.FailureTier.None, 5, new System.Collections.Generic.List<string>());
        }

        [Fact]
        public async Task GetDialogueOptionsAsync_WithDebugDirectory_WritesFiles()
        {
            // What: When DebugDirectory is set, adapter writes request, response, and session-summary.json
            // Mutation: Fails if adapter skips writing debug files or uses wrong path.
            var handler = new MockHttpMessageHandler((_, __) => MakeOptionsSuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugDir };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            var ctx = MakeContext();
            
            await adapter.GetDialogueOptionsAsync(ctx);

            Assert.True(Directory.Exists(_debugDir));
            
            var reqFile = Path.Combine(_debugDir, "turn-00-options-request.json");
            var resFile = Path.Combine(_debugDir, "turn-00-options-response.json");
            var summaryFile = Path.Combine(_debugDir, "session-summary.json");

            Assert.True(File.Exists(reqFile));
            Assert.True(File.Exists(resFile));
            Assert.True(File.Exists(summaryFile));

            var summaryText = File.ReadAllText(summaryFile);
            Assert.Contains("\"cache_creation_input_tokens\": 20", summaryText);
            Assert.Contains("\"cache_read_input_tokens\": 30", summaryText);
        }

        [Fact]
        public async Task DeliverMessageAsync_WithDebugDirectory_WritesFiles()
        {
            // What: Delivery call also writes debug files and updates summary
            // Mutation: Fails if only options call writes files.
            var handler = new MockHttpMessageHandler((_, __) => MakeDeliverySuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugDir };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            var ctx = MakeDeliveryContext();
            
            await adapter.DeliverMessageAsync(ctx);

            var reqFile = Path.Combine(_debugDir, "turn-00-delivery-request.json");
            var resFile = Path.Combine(_debugDir, "turn-00-delivery-response.json");

            Assert.True(File.Exists(reqFile));
            Assert.True(File.Exists(resFile));
        }

        [Fact]
        public async Task MultipleCalls_AccumulateStats_InSummary()
        {
            // What: session-summary.json contains cache stats and totals from multiple calls
            // Mutation: Fails if summary overwrites stats or doesn't sum totals correctly.
            var handler = new MockHttpMessageHandler((req, count) => 
                count == 1 ? MakeOptionsSuccessResponse() : MakeDeliverySuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugDir };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            
            await adapter.GetDialogueOptionsAsync(MakeContext());
            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            var summaryFile = Path.Combine(_debugDir, "session-summary.json");
            var summaryText = File.ReadAllText(summaryFile);

            // Using dynamic parse for flexibility
            dynamic summary = JsonConvert.DeserializeObject(summaryText);
            
            Assert.Equal(2, summary.calls.Count);
            
            // 20 + 0 = 20
            Assert.Equal(20, (int)summary.totals.cache_creation_input_tokens);
            // 30 + 50 = 80
            Assert.Equal(80, (int)summary.totals.cache_read_input_tokens);
            // 10 + 15 = 25
            Assert.Equal(25, (int)summary.totals.input_tokens);
            // 5 + 10 = 15
            Assert.Equal(15, (int)summary.totals.output_tokens);
        }

        [Fact]
        public async Task NullDebugDirectory_DoesNotWriteFiles()
        {
            // What: If DebugDirectory is null, no performance impact/no IO
            // Mutation: Fails if IO happens when DebugDirectory is null.
            var handler = new MockHttpMessageHandler((_, __) => MakeOptionsSuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = null };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            await adapter.GetDialogueOptionsAsync(MakeContext());

            Assert.False(Directory.Exists(_debugDir));
        }
        
        [Fact]
        public async Task GetDialogueOptionsAsync_MultipleThreads_DoesNotThrow()
        {
            // What: Thread safety for _callStats when appending in LogDebug
            // Mutation: Fails if _callStats collection is not thread-safe.
            var handler = new MockHttpMessageHandler((_, __) => MakeOptionsSuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugDir };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            var ctx = MakeContext();
            
            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                tasks[i] = Task.Run(() => adapter.GetDialogueOptionsAsync(ctx));
            }
            
            await Task.WhenAll(tasks);
            
            var summaryFile = Path.Combine(_debugDir, "session-summary.json");
            Assert.True(File.Exists(summaryFile));
            var summaryText = File.ReadAllText(summaryFile);
            dynamic summary = JsonConvert.DeserializeObject(summaryText);
            Assert.Equal(100, summary.calls.Count);
        }
    }
}
