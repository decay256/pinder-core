using System.Collections.Generic;
using Newtonsoft.Json;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters
{
    public static class TherapistDiagnosisStructuredContract
    {
        public const string SchemaName = "therapist_diagnosis";
        public const string SchemaVersion = "therapist_diagnosis.v1";

        public static StructuredLlmRequest CreateRequest(
            string systemPrompt,
            string userMessage,
            double temperature,
            int? maxTokens = null)
        {
            return new StructuredLlmRequest(
                SchemaName,
                SchemaVersion,
                BuildJsonSchema(),
                systemPrompt,
                userMessage,
                temperature,
                maxTokens,
                LlmPhase.Synthesis);
        }

        private static readonly Dictionary<string, string> FieldDescriptions = new Dictionary<string, string>
        {
            ["derived_feeling"] = "One concise sentence describing the core wound or emotional driver.",
            ["defense_reaction"] = "One concise sentence describing the defensive behavior pattern.",
            ["safe_connection"] = "Second-person prose describing what makes genuine connection feel safe to this character.",
            ["hurt_protection"] = "Second-person prose describing how this character protects themselves when hurt or threatened.",
            ["repair_requirement"] = "Second-person prose describing what this character needs before repair feels believable.",
            ["charm_reaction"] = "Second-person prose covering the affirming and threatening meaning of charm to this character.",
            ["rizz_reaction"] = "Second-person prose covering the affirming and threatening meaning of direct desire or physical-sexual confidence to this character.",
            ["honesty_reaction"] = "Second-person prose covering the affirming and threatening meaning of candor to this character.",
            ["chaos_reaction"] = "Second-person prose covering the affirming and threatening meaning of risk, mess, and disruption to this character.",
            ["wit_reaction"] = "Second-person prose covering the affirming and threatening meaning of humor and cleverness to this character.",
            ["self_awareness_reaction"] = "Second-person prose covering the affirming and threatening meaning of reflection and emotional insight to this character.",
        };

        private static string BuildJsonSchema()
        {
            var properties = new Dictionary<string, object>();
            foreach (string field in TherapistDiagnosisContract.RequiredFields)
            {
                var prop = new Dictionary<string, object>
                {
                    ["type"] = "string",
                };
                if (FieldDescriptions.TryGetValue(field, out var desc))
                {
                    prop["description"] = desc;
                }
                properties[field] = prop;
            }

            return JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = TherapistDiagnosisContract.RequiredFields,
                ["additionalProperties"] = false,
            });
        }
    }
}
