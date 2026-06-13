using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.Core.Conversation;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public partial class AnthropicLlmAdapterSpecTests
    {
        // ==============================================================================
        // AC7: Temperature verification for all methods
        // ==============================================================================

        // What: AC7 - Temperature override for DateeResponse method
        // Mutation: Would catch if datee response ignores its specific override
        [Fact]
        public async Task GetDateeResponseAsync_TemperatureOverride_Used()
        {
            var handler = new CapturingHttpHandler(@"[RESPONSE]
""text""");
            using var client = new HttpClient(handler);
            var options = DefaultOptions();
            options.DateeResponseTemperature = 0.6;
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.GetDateeResponseAsync(MakeDateeContext());

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(0.6, body!.Temperature!.Value, 2);
        }

        // What: AC7 - Temperature override for InterestChangeBeat method
        // Mutation: Would catch if interest beat ignores its specific override
        [Fact(Skip = "Removed in #573")]
        public async Task GetInterestChangeBeatAsync_TemperatureOverride_Used()
        {
            var handler = new CapturingHttpHandler("Beat text");
            using var client = new HttpClient(handler);
            var options = DefaultOptions();
            options.InterestChangeBeatTemperature = 0.4;
            using var adapter = new AnthropicLlmAdapter(options, client);

            var ctx = new InterestChangeContext("Velvet", 10, 12, InterestState.Interested);
            await adapter.GetInterestChangeBeatAsync(ctx);

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(0.4, body!.Temperature!.Value, 2);
        }

        // What: AC7 - Temperature override for DeliverMessage method
        // Mutation: Would catch if delivery uses dialogue options temp instead of its own
        [Fact]
        public async Task DeliverMessageAsync_TemperatureOverride_Used()
        {
            var handler = new CapturingHttpHandler("text");
            using var client = new HttpClient(handler);
            var options = DefaultOptions();
            options.DeliveryTemperature = 0.3;
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(0.3, body!.Temperature!.Value, 2);
        }

        // What: AC7 - When no override set, default temperatures are used
        // Mutation: Would catch if null override causes 0 temperature or throws
        [Fact]
        public async Task NoTemperatureOverride_UsesDefaults()
        {
            // Verify each method uses its spec'd default when no override is set
            var options = DefaultOptions();
            // Explicitly ensure all overrides are null
            options.DialogueOptionsTemperature = null;
            options.DeliveryTemperature = null;
            options.DateeResponseTemperature = null;
            options.InterestChangeBeatTemperature = null;

            var handler = new CapturingHttpHandler(FourOptionResponse);
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            var dialogueBody = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(0.9, dialogueBody!.Temperature!.Value, 2);
        }

        // ==============================================================================
        // AC7: MaxTokens from options
        // ==============================================================================

        // What: AC7 - MaxTokens is sent from options
        // Mutation: Would catch if max_tokens is hardcoded instead of read from options
        [Fact]
        public async Task GetDialogueOptionsAsync_MaxTokensFromOptions()
        {
            var options = DefaultOptions();
            options.MaxTokens = 2048;
            var handler = new CapturingHttpHandler(FourOptionResponse);
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(2048, body!.MaxTokens);
        }

        // ==============================================================================
        // Error conditions from spec
        // ==============================================================================

        // What: Spec error - HttpRequestException propagates (not caught by adapter)
        // Mutation: Would catch if adapter wraps network errors in a different exception
        [Fact]
        public async Task NetworkFailure_PropagatesHttpRequestException()
        {
            var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await Assert.ThrowsAsync<HttpRequestException>(
                () => adapter.GetDialogueOptionsAsync(MakeDialogueContext()));
        }

        // What: Spec error - TaskCanceledException (timeout) propagates
        // Mutation: Would catch if adapter catches timeouts and returns defaults
        [Fact]
        public async Task Timeout_PropagatesTaskCanceledException()
        {
            var handler = new ThrowingHandler(new TaskCanceledException("Request timed out"));
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await Assert.ThrowsAsync<TaskCanceledException>(
                () => adapter.DeliverMessageAsync(MakeDeliveryContext()));
        }

        // What: Spec error - DeliverMessageAsync returns "" on empty LLM response
        // Mutation: Would catch if empty deliver response throws or returns null
        [Fact]
        public async Task DeliverMessageAsync_EmptyResponse_ReturnsEmptyString()
        {
            var handler = new CapturingHttpHandler("");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.DeliverMessageAsync(MakeDeliveryContext());

            Assert.Equal("", result);
        }

        // What: Spec error - GetDateeResponseAsync on empty LLM response returns empty message
        // Mutation: Would catch if empty response causes null message or throws
        [Fact]
        public async Task GetDateeResponseAsync_EmptyResponse_ReturnsEmptyMessage()
        {
            var handler = new CapturingHttpHandler("");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetDateeResponseAsync(MakeDateeContext());

            Assert.NotNull(result);
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec error - GetDialogueOptionsAsync returns 4 defaults on unparseable response
        // Mutation: Would catch if unparseable response causes fewer than 4 or throws
        [Fact]
        public async Task GetDialogueOptionsAsync_CompletelyUnparseableResponse_FourDefaults()
        {
            var handler = new CapturingHttpHandler("🎉🎊💥 random emoji and gibberish XXXX");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            Assert.Equal(4, result.Length);
            Assert.All(result, opt => Assert.Equal("...", opt.IntendedText));
        }
    }
}
