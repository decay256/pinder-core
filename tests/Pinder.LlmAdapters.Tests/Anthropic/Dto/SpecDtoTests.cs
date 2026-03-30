using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic.Dto
{
    /// <summary>
    /// Spec-driven tests for issue #205 DTO acceptance criteria.
    /// Tests verify behavior from docs/specs/issue-205-spec.md only.
    /// </summary>
    public class SpecDtoTests
    {
        #region AC2: MessagesRequest serialization

        // What: AC2 — MessagesRequest default MaxTokens must be 1024
        // Mutation: would catch if default MaxTokens changed to 0 or any other value
        [Fact]
        public void MessagesRequest_Default_MaxTokens_Is1024()
        {
            var req = new MessagesRequest();
            Assert.Equal(1024, req.MaxTokens);
        }

        // What: AC2 — MessagesRequest default Temperature must be 0.9
        // Mutation: would catch if default Temperature changed to 0.0 or 1.0
        [Fact]
        public void MessagesRequest_Default_Temperature_Is0Point9()
        {
            var req = new MessagesRequest();
            Assert.Equal(0.9, req.Temperature);
        }

        // What: AC2 — MessagesRequest default Model must be empty string
        // Mutation: would catch if default Model was null instead of ""
        [Fact]
        public void MessagesRequest_Default_Model_IsEmptyString()
        {
            var req = new MessagesRequest();
            Assert.Equal("", req.Model);
        }

        // What: Edge case — Empty arrays serialize as [] not null/omitted
        // Mutation: would catch if System/Messages defaulted to null instead of Array.Empty
        [Fact]
        public void MessagesRequest_EmptyArrays_SerializeAsBrackets_NotNull()
        {
            var req = new MessagesRequest();
            var json = JsonConvert.SerializeObject(req);
            var jobj = JObject.Parse(json);

            Assert.Equal(JTokenType.Array, jobj["system"]!.Type);
            Assert.Equal(JTokenType.Array, jobj["messages"]!.Type);
            Assert.Empty((JArray)jobj["system"]!);
            Assert.Empty((JArray)jobj["messages"]!);
        }

        // What: AC2 — JSON property names use snake_case per Anthropic API
        // Mutation: would catch if JsonProperty("max_tokens") was missing or wrong
        [Fact]
        public void MessagesRequest_JsonPropertyNames_AreSnakeCase()
        {
            var req = new MessagesRequest { Model = "test", MaxTokens = 500 };
            var json = JsonConvert.SerializeObject(req);
            var jobj = JObject.Parse(json);

            Assert.NotNull(jobj["model"]);
            Assert.NotNull(jobj["max_tokens"]);
            Assert.NotNull(jobj["temperature"]);
            Assert.NotNull(jobj["system"]);
            Assert.NotNull(jobj["messages"]);
            // Verify PascalCase keys do NOT exist
            Assert.Null(jobj["Model"]);
            Assert.Null(jobj["MaxTokens"]);
            Assert.Null(jobj["Temperature"]);
            Assert.Null(jobj["System"]);
            Assert.Null(jobj["Messages"]);
        }

        // What: AC2 — Full request serializes to match spec example exactly
        // Mutation: would catch any structural deviation from the Anthropic Messages API schema
        [Fact]
        public void MessagesRequest_FullExample_MatchesSpecJson()
        {
            var req = new MessagesRequest
            {
                Model = "claude-sonnet-4-20250514",
                MaxTokens = 1024,
                Temperature = 0.9,
                System = new[]
                {
                    new ContentBlock
                    {
                        Type = "text",
                        Text = "You are a character.",
                        CacheControl = new CacheControl { Type = "ephemeral" }
                    }
                },
                Messages = new[]
                {
                    new Message { Role = "user", Content = "Generate 4 dialogue options." }
                }
            };

            var json = JObject.Parse(JsonConvert.SerializeObject(req));

            Assert.Equal("claude-sonnet-4-20250514", json["model"]!.Value<string>());
            Assert.Equal(1024, json["max_tokens"]!.Value<int>());
            Assert.Equal(0.9, json["temperature"]!.Value<double>());

            var sys = (JArray)json["system"]!;
            Assert.Single(sys);
            Assert.Equal("text", sys[0]["type"]!.Value<string>());
            Assert.Equal("You are a character.", sys[0]["text"]!.Value<string>());
            Assert.Equal("ephemeral", sys[0]["cache_control"]!["type"]!.Value<string>());

            var msgs = (JArray)json["messages"]!;
            Assert.Single(msgs);
            Assert.Equal("user", msgs[0]["role"]!.Value<string>());
            Assert.Equal("Generate 4 dialogue options.", msgs[0]["content"]!.Value<string>());
        }

        // What: AC2 — MessagesRequest can be deserialized from JSON (round-trip)
        // Mutation: would catch if JsonProperty attributes were write-only
        [Fact]
        public void MessagesRequest_RoundTrips_ThroughJson()
        {
            var original = new MessagesRequest
            {
                Model = "test-model",
                MaxTokens = 2048,
                Temperature = 0.5,
                System = new[] { new ContentBlock { Type = "text", Text = "sys" } },
                Messages = new[] { new Message { Role = "user", Content = "hello" } }
            };

            var json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<MessagesRequest>(json)!;

            Assert.Equal("test-model", deserialized.Model);
            Assert.Equal(2048, deserialized.MaxTokens);
            Assert.Equal(0.5, deserialized.Temperature);
            Assert.Single(deserialized.System);
            Assert.Single(deserialized.Messages);
        }

        #endregion

        #region AC2: ContentBlock + CacheControl serialization

        // What: AC2 — CacheControl null omits cache_control key from JSON
        // Mutation: would catch if NullValueHandling.Ignore was removed from JsonProperty
        [Fact]
        public void ContentBlock_NullCacheControl_OmitsKeyEntirely()
        {
            var block = new ContentBlock { Type = "text", Text = "Hello" };
            var json = JsonConvert.SerializeObject(block);

            // Must NOT contain the string "cache_control" at all
            Assert.DoesNotContain("cache_control", json);
        }

        // What: AC2 — ContentBlock default Type is "text"
        // Mutation: would catch if default Type was "" or null
        [Fact]
        public void ContentBlock_Default_Type_IsText()
        {
            var block = new ContentBlock();
            Assert.Equal("text", block.Type);
        }

        // What: AC2 — ContentBlock default Text is ""
        // Mutation: would catch if default Text was null
        [Fact]
        public void ContentBlock_Default_Text_IsEmptyString()
        {
            var block = new ContentBlock();
            Assert.Equal("", block.Text);
        }

        // What: AC2 — CacheControl default Type is "ephemeral"
        // Mutation: would catch if default was "" or null or "permanent"
        [Fact]
        public void CacheControl_Default_Type_IsEphemeral()
        {
            var cc = new CacheControl();
            Assert.Equal("ephemeral", cc.Type);
        }

        // What: AC2 — CacheControl serializes type as snake_case
        // Mutation: would catch if JsonProperty("type") was missing
        [Fact]
        public void CacheControl_Serializes_TypeProperty()
        {
            var cc = new CacheControl { Type = "ephemeral" };
            var json = JObject.Parse(JsonConvert.SerializeObject(cc));
            Assert.Equal("ephemeral", json["type"]!.Value<string>());
            Assert.Null(json["Type"]); // no PascalCase
        }

        // What: AC2 — Message defaults
        // Mutation: would catch if Role or Content defaulted to null instead of ""
        [Fact]
        public void Message_Defaults_AreEmptyStrings()
        {
            var msg = new Message();
            Assert.Equal("", msg.Role);
            Assert.Equal("", msg.Content);
        }

        // What: AC2 — Message JSON property names are snake_case
        // Mutation: would catch if JsonProperty attributes were missing
        [Fact]
        public void Message_Serializes_WithCorrectJsonKeys()
        {
            var msg = new Message { Role = "assistant", Content = "reply" };
            var json = JObject.Parse(JsonConvert.SerializeObject(msg));
            Assert.Equal("assistant", json["role"]!.Value<string>());
            Assert.Equal("reply", json["content"]!.Value<string>());
        }

        #endregion

        #region AC2: MessagesResponse + GetText()

        // What: AC2 — GetText() returns Content[0].Text when content exists
        // Mutation: would catch if GetText() returned Content[1].Text or concatenated
        [Fact]
        public void MessagesResponse_GetText_ReturnsFirstBlockText()
        {
            var resp = new MessagesResponse
            {
                Content = new[]
                {
                    new ResponseContent { Type = "text", Text = "First block" },
                    new ResponseContent { Type = "text", Text = "Second block" }
                }
            };
            Assert.Equal("First block", resp.GetText());
        }

        // What: Edge case — GetText() returns "" when Content is empty array
        // Mutation: would catch if GetText() threw IndexOutOfRangeException on empty
        [Fact]
        public void MessagesResponse_GetText_ReturnsEmpty_WhenNoContent()
        {
            var resp = new MessagesResponse();
            Assert.Equal("", resp.GetText());
        }

        // What: AC2 — MessagesResponse Content defaults to empty array, not null
        // Mutation: would catch if Content defaulted to null
        [Fact]
        public void MessagesResponse_Content_DefaultsToEmptyArray()
        {
            var resp = new MessagesResponse();
            Assert.NotNull(resp.Content);
            Assert.Empty(resp.Content);
        }

        // What: Edge case — Usage can be null
        // Mutation: would catch if Usage defaulted to non-null
        [Fact]
        public void MessagesResponse_Usage_DefaultsToNull()
        {
            var resp = new MessagesResponse();
            Assert.Null(resp.Usage);
        }

        // What: AC2 — Full API response deserialization with usage stats
        // Mutation: would catch if any UsageStats field mapping was wrong
        [Fact]
        public void MessagesResponse_Deserializes_FullApiResponse()
        {
            var json = @"{
                ""content"": [
                    { ""type"": ""text"", ""text"": ""Here are your options:\n1. ..."" }
                ],
                ""usage"": {
                    ""input_tokens"": 1500,
                    ""output_tokens"": 350,
                    ""cache_creation_input_tokens"": 1200,
                    ""cache_read_input_tokens"": 0
                }
            }";

            var resp = JsonConvert.DeserializeObject<MessagesResponse>(json)!;

            Assert.Equal("Here are your options:\n1. ...", resp.GetText());
            Assert.NotNull(resp.Usage);
            Assert.Equal(1500, resp.Usage!.InputTokens);
            Assert.Equal(350, resp.Usage.OutputTokens);
            Assert.Equal(1200, resp.Usage.CacheCreationInputTokens);
            Assert.Equal(0, resp.Usage.CacheReadInputTokens);
        }

        // What: Edge case — Deserialization of response with null usage
        // Mutation: would catch if deserialization threw on missing usage
        [Fact]
        public void MessagesResponse_Deserializes_NullUsage()
        {
            var json = @"{ ""content"": [], ""usage"": null }";
            var resp = JsonConvert.DeserializeObject<MessagesResponse>(json)!;

            Assert.Null(resp.Usage);
            Assert.Equal("", resp.GetText());
        }

        // What: AC2 — ResponseContent defaults
        // Mutation: would catch if Type or Text defaulted to null
        [Fact]
        public void ResponseContent_Defaults_AreEmptyStrings()
        {
            var rc = new ResponseContent();
            Assert.Equal("", rc.Type);
            Assert.Equal("", rc.Text);
        }

        // What: AC2 — UsageStats JSON property names are snake_case for cache fields
        // Mutation: would catch if cache_creation_input_tokens or cache_read_input_tokens had wrong mapping
        [Fact]
        public void UsageStats_Deserializes_CacheTokenFields()
        {
            var json = @"{
                ""input_tokens"": 100,
                ""output_tokens"": 50,
                ""cache_creation_input_tokens"": 80,
                ""cache_read_input_tokens"": 20
            }";
            var stats = JsonConvert.DeserializeObject<UsageStats>(json)!;

            Assert.Equal(100, stats.InputTokens);
            Assert.Equal(50, stats.OutputTokens);
            Assert.Equal(80, stats.CacheCreationInputTokens);
            Assert.Equal(20, stats.CacheReadInputTokens);
        }

        // What: Edge case — UsageStats defaults to 0 for all int fields
        // Mutation: would catch if any token count defaulted to non-zero
        [Fact]
        public void UsageStats_Defaults_AreZero()
        {
            var stats = new UsageStats();
            Assert.Equal(0, stats.InputTokens);
            Assert.Equal(0, stats.OutputTokens);
            Assert.Equal(0, stats.CacheCreationInputTokens);
            Assert.Equal(0, stats.CacheReadInputTokens);
        }

        #endregion
    }
}
