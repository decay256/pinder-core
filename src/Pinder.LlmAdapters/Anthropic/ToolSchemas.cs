using Newtonsoft.Json.Linq;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Static tool definitions for structured output via Anthropic tool_use.
    /// Each schema forces the model to return JSON matching the expected shape
    /// instead of free-text that requires regex parsing.
    /// </summary>
    internal static class ToolSchemas
    {
        /// <summary>
        /// Tool for GetDialogueOptionsAsync — returns 4 structured dialogue options.
        /// Schema: {options: [{stat, text, callback, combo, tell_bonus, weakness_window}]}
        /// </summary>
        public static readonly ToolDefinition DialogueOptions = new ToolDefinition
        {
            Name = "submit_dialogue_options",
            Description = "Submit the generated dialogue options for the player.",
            InputSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""options"": {
                        ""type"": ""array"",
                        ""items"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""stat"": {
                                    ""type"": ""string"",
                                    ""description"": ""The stat used for this option. Must be one of: Charm, Rizz, Honesty, Chaos, Wit, SelfAwareness""
                                },
                                ""text"": {
                                    ""type"": ""string"",
                                    ""description"": ""The dialogue text for this option.""
                                },
                                ""callback"": {
                                    ""type"": [""string"", ""null""],
                                    ""description"": ""Callback turn reference (e.g. '3' or 'turn_3') or null if none.""
                                },
                                ""combo"": {
                                    ""type"": [""string"", ""null""],
                                    ""description"": ""Combo name being completed, or null if none.""
                                },
                                ""tell_bonus"": {
                                    ""type"": ""boolean"",
                                    ""description"": ""Whether this option has a tell bonus.""
                                },
                                ""weakness_window"": {
                                    ""type"": ""boolean"",
                                    ""description"": ""Whether a weakness window is active for this option.""
                                }
                            },
                            ""required"": [""stat"", ""text"", ""tell_bonus"", ""weakness_window""]
                        },
                        ""minItems"": 4,
                        ""maxItems"": 4
                    }
                },
                ""required"": [""options""]
            }")
        };

        /// <summary>
        /// Tool for DeliverMessageAsync — returns the delivered message text.
        /// Schema: {delivered: string}
        /// </summary>
        public static readonly ToolDefinition Delivery = new ToolDefinition
        {
            Name = "submit_delivery",
            Description = "Submit the delivered message text.",
            InputSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""delivered"": {
                        ""type"": ""string"",
                        ""description"": ""The delivered message text after applying the roll outcome.""
                    }
                },
                ""required"": [""delivered""]
            }")
        };

        /// <summary>
        /// Tool for GetOpponentResponseAsync — returns the opponent's message and optional signals.
        /// Schema: {message, tell?, weakness?}
        /// </summary>
        public static readonly ToolDefinition OpponentResponse = new ToolDefinition
        {
            Name = "submit_opponent_response",
            Description = "Submit the opponent's response message and any detected gameplay signals.",
            InputSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""message"": {
                        ""type"": ""string"",
                        ""description"": ""The opponent's message text.""
                    },
                    ""tell"": {
                        ""type"": [""object"", ""null""],
                        ""description"": ""A detected tell signal, or null if none."",
                        ""properties"": {
                            ""stat"": {
                                ""type"": ""string"",
                                ""description"": ""The stat the tell relates to (e.g. Charm, Rizz, Wit).""
                            },
                            ""description"": {
                                ""type"": ""string"",
                                ""description"": ""Brief description of the tell behaviour.""
                            }
                        },
                        ""required"": [""stat"", ""description""]
                    },
                    ""weakness"": {
                        ""type"": [""object"", ""null""],
                        ""description"": ""A weakness window signal, or null if none."",
                        ""properties"": {
                            ""defending_stat"": {
                                ""type"": ""string"",
                                ""description"": ""The defending stat whose DC is reduced.""
                            },
                            ""dc_reduction"": {
                                ""type"": ""integer"",
                                ""description"": ""How much the DC is reduced by (positive integer).""
                            }
                        },
                        ""required"": [""defending_stat"", ""dc_reduction""]
                    }
                },
                ""required"": [""message""]
            }")
        };

        /// <summary>
        /// Tool for ApplyImprovementAsync — returns the improved content.
        /// Schema: {improved: string}
        /// </summary>
        public static readonly ToolDefinition Improvement = new ToolDefinition
        {
            Name = "submit_improvement",
            Description = "Submit the improved content.",
            InputSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""improved"": {
                        ""type"": ""string"",
                        ""description"": ""The improved content text. If no changes are needed, return the original content unchanged.""
                    }
                },
                ""required"": [""improved""]
            }")
        };

        /// <summary>
        /// Standard tool choice that forces the model to use the specified tool.
        /// </summary>
        public static ToolChoiceOption ForceAny()
        {
            return new ToolChoiceOption { Type = "any" };
        }
    }
}
