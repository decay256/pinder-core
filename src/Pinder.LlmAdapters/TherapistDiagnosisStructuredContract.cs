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

        private static string BuildJsonSchema()
        {
            var properties = new Dictionary<string, object>();
            foreach (string field in TherapistDiagnosisContract.RequiredFields)
            {
                properties[field] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                };
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
