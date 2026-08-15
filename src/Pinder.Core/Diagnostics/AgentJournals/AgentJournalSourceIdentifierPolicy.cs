using System;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public static class AgentJournalSourceIdentifierPolicy
    {
        public const int MaxLength = 128;

        public static string? GetErrorCode(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            if (!IsOpaqueIdentifier(value))
            {
                return AgentJournalValidator.ForbiddenSourceLink;
            }
            if (LooksLikeCredential(value))
            {
                return AgentJournalValidator.CredentialShapedSourceIdentifier;
            }
            return null;
        }

        private static bool IsOpaqueIdentifier(string value)
        {
            if (value.Length == 0 || value.Length > MaxLength)
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (!IsAsciiLetterOrDigit(character)
                    && character != '.'
                    && character != '_'
                    && character != '-'
                    && character != ':')
                {
                    return false;
                }
            }
            return IsAsciiLetterOrDigit(value[0]) && IsAsciiLetterOrDigit(value[value.Length - 1]);
        }

        private static bool LooksLikeCredential(string value)
        {
            string lower = value.ToLowerInvariant();
            string[] markers =
            {
                "authorization",
                "bearer",
                "client_secret",
                "cookie",
                "credential",
                "password",
                "passwd",
                "private_key",
                "provider_token",
                "access_token",
                "api_key",
                "apikey",
                "secret",
            };
            for (int i = 0; i < markers.Length; i++)
            {
                if (lower.IndexOf(markers[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return HasCredentialPrefix(lower)
                || IsAwsAccessKey(value)
                || lower.StartsWith("eyj", StringComparison.Ordinal) && Count(value, '.') >= 2;
        }

        private static bool HasCredentialPrefix(string lower)
            => lower.StartsWith("sk-", StringComparison.Ordinal)
                || lower.StartsWith("pk-", StringComparison.Ordinal)
                || lower.StartsWith("ghp_", StringComparison.Ordinal)
                || lower.StartsWith("gho_", StringComparison.Ordinal)
                || lower.StartsWith("ghu_", StringComparison.Ordinal)
                || lower.StartsWith("ghs_", StringComparison.Ordinal)
                || lower.StartsWith("ghr_", StringComparison.Ordinal)
                || lower.StartsWith("github_pat_", StringComparison.Ordinal)
                || lower.StartsWith("xoxb-", StringComparison.Ordinal)
                || lower.StartsWith("xoxp-", StringComparison.Ordinal)
                || lower.StartsWith("xoxa-", StringComparison.Ordinal)
                || lower.StartsWith("xoxr-", StringComparison.Ordinal)
                || lower.StartsWith("xoxs-", StringComparison.Ordinal)
                || lower.StartsWith("xoxe-", StringComparison.Ordinal)
                || lower.StartsWith("xapp-", StringComparison.Ordinal);

        private static bool IsAwsAccessKey(string value)
        {
            if (value.Length != 20)
            {
                return false;
            }
            string prefix = value.Substring(0, 4);
            if (prefix != "AKIA"
                && prefix != "ASIA"
                && prefix != "AIDA"
                && prefix != "AROA"
                && prefix != "AIPA"
                && prefix != "ANPA"
                && prefix != "ANVA"
                && prefix != "ASCA")
            {
                return false;
            }
            for (int i = 4; i < value.Length; i++)
            {
                char character = value[i];
                if (!(character >= 'A' && character <= 'Z') && !(character >= '0' && character <= '9'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAsciiLetterOrDigit(char value)
            => value >= 'a' && value <= 'z'
                || value >= 'A' && value <= 'Z'
                || value >= '0' && value <= '9';

        private static int Count(string value, char expected)
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == expected) count++;
            }
            return count;
        }
    }
}
