using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Tool definitions for structured output via Anthropic tool_use.
    /// Schema text is loaded from repo-owned assets under data/schemas.
    /// </summary>
    internal static class ToolSchemas
    {
        private const string DataPathEnvVar = "PINDER_DATA_PATH";
        private const string SchemaRoot = "data/schemas";
        private const string DialogueOptionsSchemaFile = "anthropic_submit_dialogue_options_tool.json";
        private const string DateeResponseSchemaFile = "anthropic_submit_datee_response_tool.json";
        private const string ImprovementSchemaFile = "anthropic_submit_improvement_tool.json";

        /// <summary>
        /// Tool for GetDialogueOptionsAsync.
        /// Schema: {options: [{stat, text, callback, combo}]}
        /// </summary>
        public static ToolDefinition GetDialogueOptions(int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "Dialogue option count must be positive.");

            return LoadToolDefinition(DialogueOptionsSchemaFile, inputSchema =>
            {
                var options = inputSchema["properties"]?["options"] as JObject
                    ?? throw new InvalidDataException($"{DialogueOptionsSchemaFile}: input_schema.properties.options must be an object.");
                options["minItems"] = count;
                options["maxItems"] = count;
            });
        }

        public static readonly ToolDefinition DialogueOptions = GetDialogueOptions(4);

        // #1125 - the submit_delivery tool schema was removed along with the
        // collapsed delivery LLM call (DeliverMessageAsync is gone from the
        // adapter surface). Options now carry the full sendable line and the
        // engine commits it deterministically via DeliveryOverlay, so no
        // structured-output tool for delivery is needed.

        /// <summary>
        /// Tool for GetDateeResponseAsync.
        /// Schema: {message, tell?, weakness?}
        /// </summary>
        public static readonly ToolDefinition DateeResponse = LoadToolDefinition(DateeResponseSchemaFile);

        /// <summary>
        /// Tool for ApplyImprovementAsync.
        /// Schema: {improved: string}
        /// </summary>
        public static readonly ToolDefinition Improvement = LoadToolDefinition(ImprovementSchemaFile);

        /// <summary>
        /// Standard tool choice that forces the model to use the specified tool.
        /// </summary>
        public static ToolChoiceOption ForceAny()
        {
            return new ToolChoiceOption { Type = "any" };
        }

        private static ToolDefinition LoadToolDefinition(string fileName, Action<JObject>? configureInputSchema = null)
        {
            string path = ResolveSchemaPath(fileName);
            JObject root;
            try
            {
                root = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException || ex is Newtonsoft.Json.JsonException)
            {
                throw new InvalidDataException($"Could not load Anthropic tool schema asset '{path}'.", ex);
            }

            string name = RequiredString(root, "name", path);
            string description = RequiredString(root, "description", path);
            var inputSchema = root["input_schema"] as JObject
                ?? throw new InvalidDataException($"{path}: required object property 'input_schema' is missing.");

            configureInputSchema?.Invoke(inputSchema);
            ValidateInputSchema(inputSchema, path);

            return new ToolDefinition
            {
                Name = name,
                Description = description,
                InputSchema = inputSchema
            };
        }

        private static string ResolveSchemaPath(string fileName)
        {
            string relativePath = Path.Combine(SchemaRoot, fileName);
            string? envPath = Environment.GetEnvironmentVariable(DataPathEnvVar);
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                string candidate = Path.Combine(envPath!, relativePath);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);

                string dataRootCandidate = Path.Combine(envPath!, "schemas", fileName);
                if (File.Exists(dataRootCandidate)) return Path.GetFullPath(dataRootCandidate);
            }

            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, relativePath);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new FileNotFoundException(
                $"Required Anthropic tool schema asset was not found: {relativePath}. " +
                $"Set {DataPathEnvVar} to the repo root or data root if running outside the checkout.");
        }

        private static string RequiredString(JObject root, string propertyName, string path)
        {
            string? value = root.Value<string>(propertyName);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"{path}: required string property '{propertyName}' is missing.");
            }
            return value!;
        }

        private static void ValidateInputSchema(JObject inputSchema, string path)
        {
            if (!string.Equals(inputSchema.Value<string>("type"), "object", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{path}: input_schema.type must be 'object'.");
            }

            if (!(inputSchema["properties"] is JObject))
            {
                throw new InvalidDataException($"{path}: input_schema.properties must be an object.");
            }

            if (!(inputSchema["required"] is JArray))
            {
                throw new InvalidDataException($"{path}: input_schema.required must be an array.");
            }
        }
    }
}
