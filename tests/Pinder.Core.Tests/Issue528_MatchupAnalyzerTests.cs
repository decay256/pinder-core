using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;

namespace Pinder.Core.Tests
{
    public class Issue528_MatchupAnalyzerTests
    {
        public class MockHttpMessageHandler : HttpMessageHandler
        {
            public List<string> Requests { get; } = new List<string>();
            private Queue<string> _responses = new Queue<string>();
            private Exception? _exceptionToThrow;

            public void EnqueueResponse(string jsonResponse)
            {
                _responses.Enqueue(jsonResponse);
            }

            public void SetException(Exception ex)
            {
                _exceptionToThrow = ex;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (_exceptionToThrow != null) throw _exceptionToThrow;

                if (request.Content != null)
                {
                    var content = await request.Content.ReadAsStringAsync();
                    Requests.Add(content);
                }
                else
                {
                    Requests.Add("");
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(_responses.Count > 0 ? _responses.Dequeue() : "{}")
                };
            }
        }

        // What: Verify MatchupAnalyzer matches the spec signature exactly
        // Mutation: Catches if the class is implemented as static (preventing HttpClient injection) or missing AnalyzeAsync
        [Fact]
        public void MatchupAnalyzer_ShouldMatchSpecSignature()
        {
            var type = Type.GetType("Pinder.SessionRunner.MatchupAnalyzer, session-runner");
            Assert.NotNull(type);
            Assert.True(type.IsClass, "MatchupAnalyzer should be a class");
            Assert.True(type.IsSealed, "MatchupAnalyzer should be sealed");
            Assert.False(type.IsAbstract, "MatchupAnalyzer should not be abstract (or static)");

            var ctor = type.GetConstructor(new[] { typeof(AnthropicOptions), typeof(HttpClient) });
            Assert.NotNull(ctor);

            var method = type.GetMethod("AnalyzeAsync", new[] { typeof(CharacterProfile), typeof(CharacterProfile) });
            Assert.NotNull(method);
            Assert.Equal(typeof(Task<string>), method.ReturnType);
        }

        // What: Verify AnalyzeAsync constructs the prompt properly and requests analysis
        // Mutation: Catches if the prompt omits DC table, shadow risks, best lanes, or prediction requests
        [Fact]
        public async Task AnalyzeAsync_ShouldSendCorrectPromptAndReturnAnalysis()
        {
            var type = Type.GetType("Pinder.SessionRunner.MatchupAnalyzer, session-runner");
            if (type == null || type.IsAbstract || type.GetConstructor(new[] { typeof(AnthropicOptions), typeof(HttpClient) }) == null)
            {
                Assert.Fail("MatchupAnalyzer does not match spec signature, cannot inject HttpClient for test.");
            }

            var options = new AnthropicOptions { ApiKey = "test-key" };
            var mockHttp = new MockHttpMessageHandler();
            var httpClient = new HttpClient(mockHttp);

            dynamic analyzer = Activator.CreateInstance(type, options, httpClient)!;

            var player = new CharacterProfile(new StatBlock(new Dictionary<StatType, int>(), new Dictionary<ShadowStatType, int>()), "Sys1", "Player1", new Pinder.Core.Conversation.TimingProfile(5, 1f, 1f, "test"), 1, "Bio1");
            var opponent = new CharacterProfile(new StatBlock(new Dictionary<StatType, int>(), new Dictionary<ShadowStatType, int>()), "Sys2", "Player2", new Pinder.Core.Conversation.TimingProfile(5, 1f, 1f, "test"), 1, "Bio2");

            mockHttp.EnqueueResponse("{\"content\":[{\"text\":\"**Player1** analysis...\\n\\n**Player2** analysis...\\n\\n**Prediction:** ...\"}]}");

            Task<string> task = analyzer.AnalyzeAsync(player, opponent);
            string result = await task;

            Assert.Single(mockHttp.Requests);
            var requestBody = mockHttp.Requests[0];

            Assert.Contains("best lane", requestBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("shadow risk", requestBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Prediction", requestBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DC Reference", requestBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("3", requestBody, StringComparison.OrdinalIgnoreCase); // 3-4 sentences
            Assert.Contains("4 sentences", requestBody, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("**Player1** analysis", result);
        }

        // What: Verify edge case with missing bio handles gracefully without crashing
        // Mutation: Catches NullReferenceException during prompt building when Bio is null/empty
        [Fact]
        public async Task AnalyzeAsync_WithEmptyBio_ShouldFormatProperly()
        {
            var type = Type.GetType("Pinder.SessionRunner.MatchupAnalyzer, session-runner");
            if (type == null || type.IsAbstract || type.GetConstructor(new[] { typeof(AnthropicOptions), typeof(HttpClient) }) == null)
            {
                Assert.Fail("MatchupAnalyzer does not match spec signature, cannot inject HttpClient for test.");
            }

            var options = new AnthropicOptions { ApiKey = "test-key" };
            var mockHttp = new MockHttpMessageHandler();
            var httpClient = new HttpClient(mockHttp);

            dynamic analyzer = Activator.CreateInstance(type, options, httpClient)!;

            var player = new CharacterProfile(new StatBlock(new Dictionary<StatType, int>(), new Dictionary<ShadowStatType, int>()), "Sys1", "Player1", new Pinder.Core.Conversation.TimingProfile(5, 1f, 1f, "test"), 1, "");
            var opponent = new CharacterProfile(new StatBlock(new Dictionary<StatType, int>(), new Dictionary<ShadowStatType, int>()), "Sys2", "Player2", new Pinder.Core.Conversation.TimingProfile(5, 1f, 1f, "test"), 1, "");

            mockHttp.EnqueueResponse("{\"content\":[{\"text\":\"Empty bio analysis\"}]}");

            Task<string> task = analyzer.AnalyzeAsync(player, opponent);
            string result = await task;

            Assert.Equal("Empty bio analysis", result);
            Assert.Contains("Player1", mockHttp.Requests[0]);
        }

        // What: Verify identical characters logic still succeeds
        // Mutation: Catches if cache key generation or logic fails for mirror matches
        [Fact]
        public async Task AnalyzeAsync_WithIdenticalCharacters_ShouldStillAnalyze()
        {
            var type = Type.GetType("Pinder.SessionRunner.MatchupAnalyzer, session-runner");
            if (type == null || type.IsAbstract || type.GetConstructor(new[] { typeof(AnthropicOptions), typeof(HttpClient) }) == null)
            {
                Assert.Fail("MatchupAnalyzer does not match spec signature, cannot inject HttpClient for test.");
            }

            var options = new AnthropicOptions { ApiKey = "test-key" };
            var mockHttp = new MockHttpMessageHandler();
            var httpClient = new HttpClient(mockHttp);

            dynamic analyzer = Activator.CreateInstance(type, options, httpClient)!;

            var player = new CharacterProfile(new StatBlock(new Dictionary<StatType, int>(), new Dictionary<ShadowStatType, int>()), "Sys", "Clone", new Pinder.Core.Conversation.TimingProfile(5, 1f, 1f, "test"), 1, "Bio");
            var opponent = new CharacterProfile(new StatBlock(new Dictionary<StatType, int>(), new Dictionary<ShadowStatType, int>()), "Sys", "Clone", new Pinder.Core.Conversation.TimingProfile(5, 1f, 1f, "test"), 1, "Bio");

            mockHttp.EnqueueResponse("{\"content\":[{\"text\":\"Mirror match analysis\"}]}");

            Task<string> task = analyzer.AnalyzeAsync(player, opponent);
            string result = await task;

            Assert.Equal("Mirror match analysis", result);
        }

        // What: Verify API failure returns graceful fallback string
        // Mutation: Catches if HttpRequestException bubbles up or if it returns null instead of a fallback message
        [Fact]
        public async Task AnalyzeAsync_OnApiFailure_ShouldReturnGracefulFallback()
        {
            var type = Type.GetType("Pinder.SessionRunner.MatchupAnalyzer, session-runner");
            if (type == null || type.IsAbstract || type.GetConstructor(new[] { typeof(AnthropicOptions), typeof(HttpClient) }) == null)
            {
                Assert.Fail("MatchupAnalyzer does not match spec signature, cannot inject HttpClient for test.");
            }

            var options = new AnthropicOptions { ApiKey = "test-key" };
            var mockHttp = new MockHttpMessageHandler();
            mockHttp.SetException(new HttpRequestException("Network down"));
            var httpClient = new HttpClient(mockHttp);

            dynamic analyzer = Activator.CreateInstance(type, options, httpClient)!;

            var player = new CharacterProfile(new StatBlock(new Dictionary<StatType, int>(), new Dictionary<ShadowStatType, int>()), "Sys1", "Player1", new Pinder.Core.Conversation.TimingProfile(5, 1f, 1f, "test"), 1, "Bio1");
            var opponent = new CharacterProfile(new StatBlock(new Dictionary<StatType, int>(), new Dictionary<ShadowStatType, int>()), "Sys2", "Player2", new Pinder.Core.Conversation.TimingProfile(5, 1f, 1f, "test"), 1, "Bio2");

            Task<string> task = analyzer.AnalyzeAsync(player, opponent);
            string result = await task;

            Assert.NotNull(result);
            Assert.Contains("Matchup analysis unavailable due to API error", result, StringComparison.OrdinalIgnoreCase);
        }

        // What: Verify Program.cs invokes MatchupAnalyzer properly and prints under ## Matchup Analysis header
        // Mutation: Catches if the header is missing, or the call is omitted, or method signature doesn't match spec
        [Fact]
        public void ProgramCs_ContainsMatchupAnalysisHeaderAndOutput()
        {
            string programPath = FindProgramCs();
            string content = System.IO.File.ReadAllText(programPath);

            Assert.Contains("## Matchup Analysis", content);
            Assert.Contains("new MatchupAnalyzer(", content);
            Assert.Contains(".AnalyzeAsync(", content);
        }

        private static string FindProgramCs()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "session-runner", "Program.cs");
                if (File.Exists(candidate))
                    return candidate;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent;
            }
            throw new FileNotFoundException("Could not find session-runner/Program.cs");
        }
    }
}
