using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class Issue534_DebugFlagTests : IDisposable
    {
        private readonly string _debugDir;
        private readonly string _debugFile;

        public Issue534_DebugFlagTests()
        {
            _debugDir = Path.Combine(Path.GetTempPath(), "pinder_debug_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_debugDir);
            _debugFile = Path.Combine(_debugDir, "session-001-debug.md");
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
                "Player Prompt", "Datee Prompt", history, "Last message",
                new System.Collections.Generic.List<string>(), 10, availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty,  }, playerName: "P", dateeName: "O");
        }

        // #1125: the delivery LLM surface was removed; the "second call" in the
        // multi-call usage/debug tests below is now the datee response call.
        private static DateeContext MakeDateeContext()
        {
            var history = new System.Collections.Generic.List<(string, string)>();
            return new DateeContext(
                dateePrompt: "Datee Prompt",
                conversationHistory: history,
                dateeLastMessage: "Last message",
                activeTraps: new System.Collections.Generic.List<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "hello",
                interestBefore: 10,
                interestAfter: 11,
                responseDelayMinutes: 1.0, playerName: "P", dateeName: "O");
        }

        [Fact]
        public async Task GetDialogueOptionsAsync_WithDebugFile_WritesMarkdown()
        {
            // What: When DebugDirectory (now a file path) is set, adapter writes markdown sections
            // Mutation: Fails if adapter skips writing debug content or uses wrong path.
            var handler = new MockHttpMessageHandler((_, __) => MakeOptionsSuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugFile };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            var ctx = MakeContext();
            
            await adapter.GetDialogueOptionsAsync(ctx);

            Assert.True(File.Exists(_debugFile));
            
            var content = File.ReadAllText(_debugFile);
            Assert.Contains("# Session Debug Transcript", content);
            Assert.Contains("### OPTIONS REQUEST", content);
            Assert.Contains("### OPTIONS RESPONSE", content);
            Assert.Contains("**User message:**", content);
        }

        [Fact]
        public async Task MultipleCalls_AccumulateStats_InSummaryTable()
        {
            // What: WriteDebugSummary produces a markdown token table with correct totals
            // Mutation: Fails if summary doesn't accumulate or totals are wrong.
            var handler = new MockHttpMessageHandler((req, count) => 
                count == 1 ? MakeOptionsSuccessResponse() : MakeDeliverySuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugFile };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            
            await adapter.GetDialogueOptionsAsync(MakeContext());
            await adapter.GetDateeResponseAsync(MakeDateeContext());
            adapter.WriteDebugSummary();

            var content = File.ReadAllText(_debugFile);

            Assert.Contains("## Token Summary", content);
            Assert.Contains("| options |", content);
            Assert.Contains("| datee |", content);
            // Totals: input 10+15=25, output 5+10=15, cache read 30+50=80, cache write 20+0=20
            Assert.Contains("**25**", content);
            Assert.Contains("**15**", content);
            Assert.Contains("**80**", content);
            Assert.Contains("**20**", content);
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

            Assert.False(File.Exists(_debugFile));
        }
        
        [Fact]
        public async Task GetDialogueOptionsAsync_MultipleThreads_DoesNotThrow()
        {
            // What: Thread safety for writing to the debug file concurrently
            // Mutation: Fails if file writes are not thread-safe.
            var handler = new MockHttpMessageHandler((_, __) => MakeOptionsSuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugFile };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            var ctx = MakeContext();
            
            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                tasks[i] = Task.Run(() => adapter.GetDialogueOptionsAsync(ctx));
            }
            
            await Task.WhenAll(tasks);
            
            Assert.True(File.Exists(_debugFile));
            var content = File.ReadAllText(_debugFile);
            // Should have 100 OPTIONS REQUEST sections
            int count = 0;
            int idx = 0;
            while ((idx = content.IndexOf("### OPTIONS REQUEST", idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx++;
            }
            Assert.Equal(100, count);
        }

        [Fact]
        public async Task DebugFile_NoSubdirectoryCreated()
        {
            // What: Issue #639 — debug should write a single file, not create a subdirectory
            // Mutation: Fails if a subdirectory is created alongside the debug file.
            var handler = new MockHttpMessageHandler((_, __) => MakeOptionsSuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugFile };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            await adapter.GetDialogueOptionsAsync(MakeContext());

            // The debug file should exist as a file, not as a directory
            Assert.True(File.Exists(_debugFile));
            Assert.False(Directory.Exists(_debugFile));
            
            // No new subdirectories should have been created inside _debugDir
            var subDirs = Directory.GetDirectories(_debugDir);
            Assert.Empty(subDirs);
        }

        [Fact]
        public async Task DebugFile_ContainsTurnHeaders()
        {
            // What: Issue #639 — each turn's options call emits a ## Turn N header
            var handler = new MockHttpMessageHandler((_, __) => MakeOptionsSuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugFile };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            await adapter.GetDialogueOptionsAsync(MakeContext());

            var content = File.ReadAllText(_debugFile);
            Assert.Contains("## Turn 0", content);
        }

        [Fact]
        public async Task DebugFile_SystemPromptTruncatedTo200Chars()
        {
            // What: Issue #639 — system prompt now shown in full (no truncation, per fix in b2a6f3a)
            var handler = new MockHttpMessageHandler((_, __) => MakeOptionsSuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugFile };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            await adapter.GetDialogueOptionsAsync(MakeContext());

            var content = File.ReadAllText(_debugFile);
            Assert.Contains("**System prompt:**", content);
        }

        [Fact]
        public async Task GetSessionUsage_AccumulatesCorrectly_WhenDebugDirectoryIsNull()
        {
            var handler = new MockHttpMessageHandler((req, count) => 
                count == 1 ? MakeOptionsSuccessResponse() : MakeDeliverySuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = null };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            
            await adapter.GetDialogueOptionsAsync(MakeContext());
            await adapter.GetDateeResponseAsync(MakeDateeContext());

            var usage = adapter.GetSessionUsage();

            Assert.NotNull(usage);
            Assert.Equal(25, usage.InputTokens);
            Assert.Equal(15, usage.OutputTokens);
            Assert.Equal(80, usage.CacheReadInputTokens);
            Assert.Equal(20, usage.CacheCreationInputTokens);
            Assert.Equal(2, usage.CallCount);
        }

        [Fact]
        public async Task GetSessionUsage_TotalBilledInput_ExcludesCacheRead()
        {
            var handler = new MockHttpMessageHandler((req, count) => 
                count == 1 ? MakeOptionsSuccessResponse() : MakeDeliverySuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = null };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            
            await adapter.GetDialogueOptionsAsync(MakeContext());
            await adapter.GetDateeResponseAsync(MakeDateeContext());

            var usage = adapter.GetSessionUsage();

            Assert.Equal(45, usage.TotalBilledInput); // 25 (InputTokens) + 20 (CacheCreationInputTokens)
            Assert.NotEqual(125, usage.TotalBilledInput); // 45 + 80 (CacheReadInputTokens)
        }

        [Fact]
        public async Task GetSessionUsage_TotalsMatch_WriteDebugSummaryTable()
        {
            var handler = new MockHttpMessageHandler((req, count) => 
                count == 1 ? MakeOptionsSuccessResponse() : MakeDeliverySuccessResponse());
            var httpClient = new HttpClient(handler);
            var options = new AnthropicOptions { ApiKey = "test", DebugDirectory = _debugFile };
            
            using var adapter = new AnthropicLlmAdapter(options, httpClient);
            
            await adapter.GetDialogueOptionsAsync(MakeContext());
            await adapter.GetDateeResponseAsync(MakeDateeContext());
            adapter.WriteDebugSummary();

            var usage = adapter.GetSessionUsage();
            var content = File.ReadAllText(_debugFile);

            // Verify totals match what was written in the table
            Assert.Contains($"| **Total** | | **{usage.InputTokens}** | **{usage.OutputTokens}** | **{usage.CacheReadInputTokens}** | **{usage.CacheCreationInputTokens}** |", content);
        }
    }
}
