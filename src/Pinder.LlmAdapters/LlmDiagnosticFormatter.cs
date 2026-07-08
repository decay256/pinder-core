using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pinder.LlmAdapters
{
    internal static class LlmDiagnosticFormatter
    {
        private const int MaxExcerptLength = 160;

        public static string ProviderFailure(
            string provider,
            string failure,
            int? statusCode = null,
            string? model = null,
            string? phase = null,
            string? body = null,
            string bodyLabel = "body")
        {
            var builder = new StringBuilder();
            builder.Append(failure);
            AppendField(builder, "provider", provider);
            if (statusCode.HasValue) AppendField(builder, "status", statusCode.Value.ToString());
            if (!string.IsNullOrWhiteSpace(model)) AppendField(builder, "model", model);
            if (!string.IsNullOrWhiteSpace(phase)) AppendField(builder, "phase", phase);
            AppendBodyDiagnostics(builder, bodyLabel, body);
            return builder.ToString();
        }

        public static string GeneratedTextFailure(string failure, string phase, string? output)
        {
            var builder = new StringBuilder();
            builder.Append(failure);
            AppendField(builder, "phase", phase);
            AppendBodyDiagnostics(builder, "output", output);
            return builder.ToString();
        }

        public static string? RedactedBodyExcerptOrEmpty(string? body)
        {
            if (body == null) return null;
            if (body.Length == 0) return "";
            return RedactedExcerpt(body);
        }

        private static void AppendBodyDiagnostics(StringBuilder builder, string label, string? body)
        {
            var length = body == null ? 0 : body.Length;
            AppendField(builder, label + "_length", length.ToString());
            AppendField(builder, label + "_sha256", body == null ? "none" : Sha256Hex(body));
            AppendField(builder, label + "_excerpt", RedactedExcerpt(body));
        }

        private static void AppendField(StringBuilder builder, string name, string value)
        {
            builder.Append(' ');
            builder.Append(name);
            builder.Append('=');
            builder.Append(value);
        }

        private static string RedactedExcerpt(string? body)
        {
            if (string.IsNullOrEmpty(body))
                return "<empty>";

            var redacted = TryRedactJson(body!) ?? "[redacted non-json payload]";
            if (redacted.Length > MaxExcerptLength)
                redacted = redacted.Substring(0, MaxExcerptLength) + "...";
            return Quote(redacted);
        }

        private static string? TryRedactJson(string body)
        {
            try
            {
                var token = JToken.Parse(body);
                RedactToken(token);
                return token.ToString(Formatting.None);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void RedactToken(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (property.Value is JValue)
                        property.Value = "[redacted]";
                    else
                        RedactToken(property.Value);
                }
                return;
            }

            if (token is JArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    var item = array[i];
                    if (item is JValue value)
                        value.Replace("[redacted]");
                    else
                        RedactToken(item);
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string Sha256Hex(string value)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
