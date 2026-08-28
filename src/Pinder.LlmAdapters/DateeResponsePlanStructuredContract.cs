using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters
{
    internal static class DateeResponsePlanStructuredContract
    {
        public const string SchemaName = "datee_response_plan";
        public const string SchemaVersion = DateeResponsePlan.CurrentSchemaVersion;
        public const string ParserName = "DateeResponsePlanStructuredContract";

        internal static StructuredLlmRequest CreateRequest(
            PromptEntry prompt,
            DateeResponsePlanCompilationResult compilation,
            string systemPrompt,
            string userPrompt,
            int currentTurn,
            IReadOnlyDictionary<string, string> metadata)
        {
            if (compilation.Plan == null) throw new ArgumentException("Creative ambiguity requires a candidate plan.", nameof(compilation));
            var requestMetadata = metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            requestMetadata["phase"] = LlmPhase.OpponentResponse;
            requestMetadata["datee_private_phase"] = "response-plan-reconciliation";
            requestMetadata["schema_name"] = SchemaName;
            requestMetadata["schema_version"] = SchemaVersion;
            requestMetadata["turn"] = currentTurn.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new StructuredLlmRequest(
                SchemaName,
                SchemaVersion,
                BuildJsonSchema(compilation),
                systemPrompt,
                userPrompt,
                prompt.Temperature ?? throw new InvalidOperationException("Response-plan reconciliation prompt requires temperature."),
                prompt.MaxTokens ?? throw new InvalidOperationException("Response-plan reconciliation prompt requires max_tokens."),
                LlmPhase.OpponentResponse,
                requestMetadata);
        }

        internal static DateeResponsePlan ParseStrict(
            string? json,
            int turn,
            string? provider,
            string? model)
        {
            try
            {
                return DateeResponsePlanJson.ParseStrict(json ?? string.Empty);
            }
            catch (DateeResponsePlanContractException ex)
            {
                throw new LlmContractException(
                    phase: "datee_response_plan_reconciliation",
                    reason: ex.Code,
                    message: ex.Message,
                    provider: provider,
                    model: model,
                    parserName: ParserName,
                    turnId: turn);
            }
        }

        private static string BuildJsonSchema(DateeResponsePlanCompilationResult compilation)
        {
            if (compilation.Plan == null) throw new ArgumentException("Candidate plan is required.", nameof(compilation));
            JObject sample = JObject.Parse(DateeResponsePlanJson.Serialize(compilation.Plan));
            var properties = new JObject();
            foreach (JProperty property in sample.Properties())
            {
                if (property.Name == "movement")
                {
                    properties[property.Name] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray(compilation.AllowedMovements.Select(DateeResponsePlanJson.Token)),
                    };
                }
                else if (property.Name == "conversational_move")
                {
                    properties[property.Name] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray(compilation.AllowedConversationalMoves.Select(DateeResponsePlanJson.Token)),
                    };
                }
                else if (property.Name == "movement_stages")
                {
                    var allowed = new JArray();
                    if (compilation.AllowedStageOrders.Count == 0)
                        allowed.Add(new JObject { ["const"] = property.Value.DeepClone() });
                    else
                        foreach (IReadOnlyList<DateeResponseMovementStage> order in compilation.AllowedStageOrders)
                            allowed.Add(new JObject { ["const"] = StagesJson(order) });
                    properties[property.Name] = new JObject { ["anyOf"] = allowed };
                }
                else
                {
                    properties[property.Name] = new JObject { ["const"] = property.Value.DeepClone() };
                }
            }
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray(sample.Properties().Select(property => property.Name)),
                ["properties"] = properties,
            }.ToString(Formatting.None);
        }

        private static JArray StagesJson(IReadOnlyList<DateeResponseMovementStage> stages)
        {
            var array = new JArray();
            foreach (DateeResponseMovementStage stage in stages)
            {
                array.Add(new JObject
                {
                    ["movement"] = DateeResponsePlanJson.Token(stage.Movement),
                    ["owns_disclosure"] = stage.OwnsDisclosure,
                });
            }
            return array;
        }
    }
}
