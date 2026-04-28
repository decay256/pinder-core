using Newtonsoft.Json.Linq;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests.OpenAi
{
    /// <summary>
    /// Regression coverage for issue #320: the non-streaming OpenAI-compatible
    /// client must surface assistant text from the reasoning channels (used by
    /// OpenRouter when proxying Anthropic extended-thinking, OpenAI o-series,
    /// gpt-5* with reasoning, etc.) when <c>message.content</c> is empty or
    /// missing. The streaming variant already did this in pinder-core #178/#764;
    /// the non-streaming gap surfaced as <c>matchup_llm_failed: matchup stage
    /// produced empty output</c> on the GameApi side.
    /// </summary>
    public class OpenAiClientReasoningTests
    {
        [Fact]
        public void ExtractAssistantText_ReturnsContent_WhenContentNonEmpty()
        {
            var json = JObject.Parse(@"{
                ""choices"": [{
                    ""message"": { ""role"": ""assistant"", ""content"": ""Hello world."" }
                }]
            }");
            Assert.Equal("Hello world.", OpenAiClient.ExtractAssistantText(json));
        }

        [Fact]
        public void ExtractAssistantText_FallsBackToReasoning_WhenContentEmpty()
        {
            var json = JObject.Parse(@"{
                ""choices"": [{
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": """",
                        ""reasoning"": ""The real answer with reasoning prefix.""
                    }
                }]
            }");
            Assert.Equal(
                "The real answer with reasoning prefix.",
                OpenAiClient.ExtractAssistantText(json));
        }

        [Fact]
        public void ExtractAssistantText_FallsBackToReasoning_WhenContentNull()
        {
            // OpenRouter occasionally omits content entirely; older parsers
            // tripped on the null and returned empty.
            var json = JObject.Parse(@"{
                ""choices"": [{
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": null,
                        ""reasoning"": ""Reasoning-only response.""
                    }
                }]
            }");
            Assert.Equal(
                "Reasoning-only response.",
                OpenAiClient.ExtractAssistantText(json));
        }

        [Fact]
        public void ExtractAssistantText_FallsBackToReasoningDetailsSummary_WhenContentAndReasoningEmpty()
        {
            // OpenRouter v1 structured-reasoning schema: reasoning_details is an
            // array of typed blocks, each carrying a `summary` string.
            var json = JObject.Parse(@"{
                ""choices"": [{
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": """",
                        ""reasoning"": """",
                        ""reasoning_details"": [
                            { ""type"": ""reasoning.summary"", ""summary"": ""Part A. "" },
                            { ""type"": ""reasoning.summary"", ""summary"": ""Part B."" }
                        ]
                    }
                }]
            }");
            Assert.Equal(
                "Part A. Part B.",
                OpenAiClient.ExtractAssistantText(json));
        }

        [Fact]
        public void ExtractAssistantText_PrefersContent_OverReasoningChannels()
        {
            // When content is present, reasoning is metadata and must not
            // contaminate the user-visible text.
            var json = JObject.Parse(@"{
                ""choices"": [{
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": ""Final answer."",
                        ""reasoning"": ""scratchpad..."",
                        ""reasoning_details"": [{ ""summary"": ""scratchpad..."" }]
                    }
                }]
            }");
            Assert.Equal("Final answer.", OpenAiClient.ExtractAssistantText(json));
        }

        [Fact]
        public void ExtractAssistantText_ReturnsEmpty_WhenAllChannelsBlank()
        {
            var json = JObject.Parse(@"{
                ""choices"": [{
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": """",
                        ""reasoning"": """",
                        ""reasoning_details"": []
                    }
                }]
            }");
            Assert.Equal("", OpenAiClient.ExtractAssistantText(json));
        }

        [Fact]
        public void ExtractAssistantText_ReturnsEmpty_WhenNoChoices()
        {
            var json = JObject.Parse(@"{ ""choices"": [] }");
            Assert.Equal("", OpenAiClient.ExtractAssistantText(json));
        }

        [Fact]
        public void ExtractAssistantText_HandlesWhitespaceOnlyContent_PrefersReasoning()
        {
            // Defensive: providers occasionally return a single newline / space
            // for content during reasoning-heavy turns.
            var json = JObject.Parse(@"{
                ""choices"": [{
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": ""   \n  "",
                        ""reasoning"": ""Real text.""
                    }
                }]
            }");
            Assert.Equal("Real text.", OpenAiClient.ExtractAssistantText(json));
        }
    }
}
