using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Parses assembled prompt files (design/examples/{name}-prompt.md) to extract
    /// character data: stats, shadows, level, and system prompt.
    /// </summary>
    public static class CharacterLoader
    {
        /// <summary>
        /// Load a CharacterProfile from the named prompt file.
        /// </summary>
        /// <param name="name">Character name (e.g. "gerald", "sable")</param>
        /// <param name="promptDirectory">Directory containing {name}-prompt.md files</param>
        /// <returns>Loaded CharacterProfile</returns>
        public static CharacterProfile Load(string name, string promptDirectory)
        {
            string normalised = name.ToLowerInvariant().Trim();
            string path = Path.Combine(promptDirectory, $"{normalised}-prompt.md");

            if (!File.Exists(path))
            {
                string available = ListAvailable(promptDirectory);
                throw new FileNotFoundException(
                    $"Character prompt file not found: {path}\nAvailable characters: {available}",
                    path);
            }

            string content = File.ReadAllText(path);
            return Parse(content, normalised);
        }

        /// <summary>
        /// Parse a prompt file's content into a CharacterProfile.
        /// </summary>
        public static CharacterProfile Parse(string content, string fallbackName)
        {
            string displayName = ParseDisplayName(content) ?? CapitaliseName(fallbackName);
            int level = ParseLevel(content);
            var stats = ParseEffectiveStats(content);
            var shadows = ParseShadows(content);
            string systemPrompt = ExtractSystemPrompt(content);
            string bio = ParseBio(content);
            string textingStyle = ParseTextingStyle(content);

            var statBlock = new StatBlock(stats, shadows);
            var timing = new TimingProfile(0, 1.0f, 0.0f, "neutral");

            return new CharacterProfile(statBlock, systemPrompt, displayName, timing, level, bio: bio, textingStyleFragment: textingStyle);
        }

        /// <summary>
        /// List available character names from prompt files in a directory.
        /// </summary>
        public static string ListAvailable(string promptDirectory)
        {
            if (!Directory.Exists(promptDirectory))
                return "(directory not found)";

            var files = Directory.GetFiles(promptDirectory, "*-prompt.md");
            if (files.Length == 0)
                return "(none found)";

            var names = files
                .Select(f => Path.GetFileName(f))
                .Where(f => f.EndsWith("-prompt.md", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Substring(0, f.Length - "-prompt.md".Length))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return string.Join(", ", names);
        }

        // ── parsing helpers ────────────────────────────────────────────────

        internal static string? ParseDisplayName(string content)
        {
            var lines = content.Split(new[] { '\n' }, StringSplitOptions.None);

            // Priority 1: Extract from "name=X" in the Inputs block
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                // Look for "name=X" pattern (e.g. "> **Inputs:** name=Gerald_42 · he/him")
                int nameIdx = trimmed.IndexOf("name=", StringComparison.Ordinal);
                if (nameIdx >= 0)
                {
                    string afterName = trimmed.Substring(nameIdx + "name=".Length);
                    // Name ends at space, · (middle dot), comma, or end of string
                    string extracted = "";
                    foreach (char c in afterName)
                    {
                        if (c == ' ' || c == '\u00b7' || c == ',' || c == '\t')
                            break;
                        extracted += c;
                    }
                    extracted = extracted.Trim();
                    if (extracted.Length > 0)
                        return extracted;
                }
            }

            // Priority 2: "You are playing the role of X," inside code fence
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                const string rolePrefix = "You are playing the role of ";
                if (trimmed.StartsWith(rolePrefix))
                {
                    string rest = trimmed.Substring(rolePrefix.Length);
                    int commaIdx = rest.IndexOf(',');
                    if (commaIdx > 0)
                        return rest.Substring(0, commaIdx).Trim();
                }
            }

            // Priority 3: Header "# Name — ..."
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("# "))
                {
                    string rest = trimmed.Substring(2).Trim();
                    int dashIdx = rest.IndexOf(" — ", StringComparison.Ordinal);
                    if (dashIdx < 0) dashIdx = rest.IndexOf(" - ", StringComparison.Ordinal);
                    if (dashIdx > 0) return rest.Substring(0, dashIdx).Trim();
                    return rest;
                }
            }
            return null;
        }

        internal static int ParseLevel(string content)
        {
            // Pattern: "- Level: 3 (Getting Somewhere) | Level bonus: +1"
            var lines = content.Split(new[] { '\n' }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("- Level:"))
                {
                    // Extract the number after "Level: "
                    string afterColon = trimmed.Substring("- Level:".Length).Trim();
                    string numStr = "";
                    foreach (char c in afterColon)
                    {
                        if (char.IsDigit(c)) numStr += c;
                        else break;
                    }
                    if (int.TryParse(numStr, out int level))
                        return level;
                }
            }
            return 1; // default
        }

        internal static Dictionary<StatType, int> ParseEffectiveStats(string content)
        {
            var stats = new Dictionary<StatType, int>();
            var lines = content.Split(new[] { '\n' }, StringSplitOptions.None);
            bool inSection = false;
            bool foundSection = false;

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed == "EFFECTIVE STATS")
                {
                    inSection = true;
                    foundSection = true;
                    continue;
                }

                if (!inSection) continue;

                // Stop when we leave the stat section (empty line or ``` fence)
                if (trimmed.StartsWith("```") || (trimmed.Length == 0 && stats.Count > 0))
                    break;

                // Pattern: "- Charm: +7" or "- Self-Awareness: +4" or "- Rizz: 0" or "- Rizz: -1"
                if (trimmed.StartsWith("- ") && trimmed.Contains(":"))
                {
                    string rest = trimmed.Substring(2);
                    int colonIdx = rest.IndexOf(':');
                    if (colonIdx < 0) continue;

                    string statName = rest.Substring(0, colonIdx).Trim();
                    string valueStr = rest.Substring(colonIdx + 1).Trim();

                    StatType? statType = ParseStatType(statName);
                    if (statType == null) continue;

                    // Parse value: "+7", "-1", "0"
                    if (int.TryParse(valueStr.Replace("+", ""), out int value))
                        stats[statType.Value] = value;
                }
            }

            if (!foundSection)
            {
                throw new FormatException("File does not contain an EFFECTIVE STATS section");
            }

            // Verify all 6 required stats are present
            var requiredStats = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var missing = new List<string>();
            foreach (var stat in requiredStats)
            {
                if (!stats.ContainsKey(stat))
                    missing.Add(stat.ToString());
            }
            if (missing.Count > 0)
            {
                throw new FormatException($"EFFECTIVE STATS section is missing: {string.Join(", ", missing)}");
            }

            return stats;
        }

        internal static Dictionary<ShadowStatType, int> ParseShadows(string content)
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };

            var lines = content.Split(new[] { '\n' }, StringSplitOptions.None);
            bool inShadowSection = false;

            foreach (var line in lines)
            {
                string trimmed = line.Trim();

                // Look for shadow state heading
                if (trimmed.Contains("Shadow state"))
                {
                    inShadowSection = true;
                    continue;
                }

                if (!inShadowSection) continue;

                // Stop at next section or blank line after we've parsed some
                if (trimmed.Length == 0 && shadows.Values.Any(v => v > 0))
                    break;

                // Pattern: "- Denial: ~3 (...)" or "- Madness: ~8 (...)"
                // Also handle: "All shadows at 0."
                if (trimmed.StartsWith("All shadows at 0", StringComparison.OrdinalIgnoreCase))
                    break;

                if (trimmed.StartsWith("- ") && trimmed.Contains(":"))
                {
                    string rest = trimmed.Substring(2);
                    int colonIdx = rest.IndexOf(':');
                    if (colonIdx < 0) continue;

                    string statName = rest.Substring(0, colonIdx).Trim();
                    string valueStr = rest.Substring(colonIdx + 1).Trim();

                    ShadowStatType? shadowType = ParseShadowStatType(statName);
                    if (shadowType == null) continue;

                    // Parse value: "~3 (note)" → extract number after ~
                    string numStr = "";
                    bool foundTilde = false;
                    foreach (char c in valueStr)
                    {
                        if (c == '~') { foundTilde = true; continue; }
                        if (foundTilde && char.IsDigit(c)) numStr += c;
                        else if (foundTilde && numStr.Length > 0) break;
                        else if (!foundTilde && char.IsDigit(c)) numStr += c;
                        else if (!foundTilde && numStr.Length > 0) break;
                    }

                    if (int.TryParse(numStr, out int value))
                        shadows[shadowType.Value] = value;
                }
            }

            return shadows;
        }

        internal static string ExtractSystemPrompt(string content)
        {
            int start = content.IndexOf("```\n", StringComparison.Ordinal);
            if (start < 0) start = content.IndexOf("```\r\n", StringComparison.Ordinal);
            if (start < 0) return content;
            start = content.IndexOf('\n', start) + 1;

            int end = content.LastIndexOf("\n```", StringComparison.Ordinal);
            if (end < 0) end = content.Length;

            return content.Substring(start, end - start).Trim();
        }

        private static StatType? ParseStatType(string name)
        {
            switch (name.ToLowerInvariant().Replace("-", "").Replace(" ", ""))
            {
                case "charm": return StatType.Charm;
                case "rizz": return StatType.Rizz;
                case "honesty": return StatType.Honesty;
                case "chaos": return StatType.Chaos;
                case "wit": return StatType.Wit;
                case "selfawareness":
                case "sa": return StatType.SelfAwareness;
                default: return null;
            }
        }

        private static ShadowStatType? ParseShadowStatType(string name)
        {
            switch (name.ToLowerInvariant().Replace("-", "").Replace(" ", ""))
            {
                case "madness": return ShadowStatType.Madness;
                case "horniness": return ShadowStatType.Horniness;
                case "denial": return ShadowStatType.Denial;
                case "fixation": return ShadowStatType.Fixation;
                case "dread": return ShadowStatType.Dread;
                case "overthinking": return ShadowStatType.Overthinking;
                default: return null;
            }
        }

        internal static string ParseTextingStyle(string content)
        {
            // Extract the TEXTING STYLE section from the system prompt
            string prompt = ExtractSystemPrompt(content);
            var lines = prompt.Split(new[] { '\n' }, StringSplitOptions.None);
            var result = new List<string>();
            bool inSection = false;

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                // Match section headers like "TEXTING STYLE", "## TEXTING STYLE", "### TEXTING STYLE"
                if (trimmed.Replace("#", "").Trim().StartsWith("TEXTING STYLE", StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    continue;
                }

                if (inSection)
                {
                    // Stop at the next section header (all-caps line or # heading)
                    if (trimmed.Length > 0 && trimmed == trimmed.ToUpperInvariant() && !trimmed.StartsWith("-") && !trimmed.StartsWith("*") && trimmed.Length > 3)
                        break;
                    if (trimmed.StartsWith("##"))
                        break;
                    result.Add(line);
                }
            }

            // Trim trailing empty lines
            while (result.Count > 0 && string.IsNullOrWhiteSpace(result[result.Count - 1]))
                result.RemoveAt(result.Count - 1);

            return result.Count > 0 ? string.Join("\n", result).Trim() : string.Empty;
        }


        internal static string ParseBio(string content)
        {
            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- Bio:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed.Substring("- Bio:".Length).Trim();
                    // Strip optional surrounding quotes if present
                    if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                        value = value.Substring(1, value.Length - 2);
                    return value;
                }
            }
            return string.Empty;
        }

        private static string CapitaliseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return char.ToUpperInvariant(name[0]) + name.Substring(1).ToLowerInvariant();
        }
    }
}
