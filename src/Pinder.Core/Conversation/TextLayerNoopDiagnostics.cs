using System;
using System.Security.Cryptography;
using System.Text;

namespace Pinder.Core.Conversation
{
    internal static class TextLayerNoopDiagnostics
    {
        internal static void Emit(
            Action<TextLayerNoopEvent>? onTextLayerNoop,
            int turnNumber,
            string layer,
            string? beforeText,
            string? afterText)
        {
            if (onTextLayerNoop == null) return;

            try
            {
                string beforeHash = ComputeStableHash(beforeText);
                string afterHash = ComputeStableHash(afterText);
                onTextLayerNoop(new TextLayerNoopEvent(turnNumber, layer, beforeHash, afterHash));
            }
            catch
            {
                // Diagnostic-only path - swallow.
            }
        }

        internal static string ComputeStableHash(string? text)
        {
            if (text == null) return string.Empty;

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(16);
                for (int i = 0; i < Math.Min(8, bytes.Length); i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
