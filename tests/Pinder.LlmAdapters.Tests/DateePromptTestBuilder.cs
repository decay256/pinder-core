using System;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pinder.LlmAdapters.Tests
{
    internal static class DateePromptTestBuilder
    {
        public static string Build(DateeContext context, PromptCatalog? catalog = null)
            => BuildEx(context, catalog).Text;

        public static PromptTraceResult BuildEx(DateeContext context, PromptCatalog? catalog = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var direction = new CharacterEmotionalDirection(
                "interest",
                CharacterEmotionalDirection.NoneSecondaryEmotion,
                "controlled",
                3,
                "steady",
                "preserve connection without abandoning boundaries",
                "reads the visible message as potentially meaningful",
                "respond to what was actually said",
                "retain a proportionate boundary",
                "make one clear conversational move");
            DateeResponsePlanCompilationResult compilation = new DateeResponsePlanCompiler().Compile(
                DateeResponsePlanInput.From(context, direction));
            if (compilation.Outcome != DateeResponsePlanCompilationOutcome.Accepted || compilation.Plan == null)
            {
                throw compilation.Rejection ?? new InvalidOperationException(
                    "Test DATEE prompt fixture did not compile to an accepted response plan.");
            }

            return SessionDocumentBuilder.BuildDateePerformancePromptEx(context, compilation.Plan, catalog);
        }

        public static StructuredLlmResponse StructuredResponse(
            StructuredLlmRequest request,
            string output,
            string model = "test")
            => new StructuredLlmResponse(
                request.SchemaName == DateePerformanceStructuredContract.SchemaName
                    ? (output.TrimStart().StartsWith("{", StringComparison.Ordinal)
                        ? output
                        : PerformanceJson(output))
                    : output,
                provider: "test",
                model: model);

        public static string PerformanceJson(string message)
            => new JObject
            {
                ["schema_version"] = DateePerformanceStructuredContract.SchemaVersion,
                ["message"] = message,
                ["signals"] = new JObject
                {
                    ["tell"] = JValue.CreateNull(),
                    ["weakness"] = JValue.CreateNull(),
                },
            }.ToString(Formatting.None);
    }
}
