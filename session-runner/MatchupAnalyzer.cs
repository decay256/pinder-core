using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.SessionRunner
{
    public static class MatchupAnalyzer
    {
        public static async Task<string?> AnalyzeMatchupAsync(
            AnthropicOptions options,
            CharacterProfile player,
            CharacterProfile opponent)
        {
            // Simple file cache to avoid spamming the LLM across multiple identical playtests
            string cacheDir = Path.Combine(Environment.CurrentDirectory, ".matchup-cache");
            Directory.CreateDirectory(cacheDir);

            // Create a hash of the stats to ensure if they change, we regenerate
            string cacheKey = $"{player.DisplayName}_vs_{opponent.DisplayName}_{GetStatsHash(player, opponent)}";
            string cacheFile = Path.Combine(cacheDir, $"{cacheKey}.md");

            if (File.Exists(cacheFile))
            {
                return await File.ReadAllTextAsync(cacheFile);
            }

            var prompt = BuildPrompt(player, opponent);
            
            using var client = new AnthropicClient(options.ApiKey);
            
            var request = new MessagesRequest
            {
                Model = options.Model ?? "claude-sonnet-4-20250514",
                MaxTokens = 500,
                Temperature = 0.7f,
                System = new[] { new ContentBlock { Type = "text", Text = "You are an expert game designer analyzing a matchup in a dating RPG." } },
                Messages = new[]
                {
                    new Message
                    {
                        Role = "user",
                        Content = prompt
                    }
                }
            };

            try
            {
                var response = await client.SendMessagesAsync(request);
                string analysis = response.GetText().Trim();
                await File.WriteAllTextAsync(cacheFile, analysis);
                return analysis;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Failed to get matchup analysis: {ex.Message}");
                return null;
            }
        }

        private static string GetStatsHash(CharacterProfile player, CharacterProfile opponent)
        {
            var sb = new StringBuilder();
            AppendCharacterData(sb, "P", player);
            AppendCharacterData(sb, "O", opponent);
            
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            
            var hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                hex.AppendFormat("{0:x2}", b);
                
            return hex.ToString().Substring(0, 8);
        }

        private static string BuildPrompt(CharacterProfile player, CharacterProfile opponent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyze the following matchup between two characters in a dating RPG.");
            sb.AppendLine("Produce a brief 3-paragraph output exactly matching this format:");
            sb.AppendLine("## Matchup Analysis");
            sb.AppendLine();
            sb.AppendLine("**[PlayerName]** (Level [X], [Archetypes]): [3-4 sentences on their strongest lane, % chance, and shadow risks.]");
            sb.AppendLine();
            sb.AppendLine("**[OpponentName]** (Level [X], [Archetypes]): [3-4 sentences on their best defense, shadow effects, and vulnerabilities.]");
            sb.AppendLine();
            sb.AppendLine("**Prediction:** [2-3 sentences predicting how the match will play out based on stats and shadows.]");
            sb.AppendLine();
            sb.AppendLine("Here is the data:");
            
            sb.AppendLine();
            AppendCharacterData(sb, "Player", player);
            sb.AppendLine();
            AppendCharacterData(sb, "Opponent", opponent);
            
            sb.AppendLine();
            sb.AppendLine("DC Reference (Player attacking, Opponent defending):");
            sb.AppendLine("Stat | Player Mod | Opponent Defends | DC | Success %");
            foreach (var stat in new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness })
            {
                int atkMod = player.Stats.GetEffective(stat);
                int dc = opponent.Stats.GetDefenceDC(stat);
                int need = dc - atkMod;
                int pct = Math.Max(0, Math.Min(100, (21 - need) * 5));
                sb.AppendLine($"{stat} | {atkMod:+#;-#;0} | {StatBlock.DefenceTable[stat]} ({opponent.Stats.GetEffective(StatBlock.DefenceTable[stat]):+#;-#;0}) | {dc} | {pct}%");
            }
            
            return sb.ToString();
        }

        private static void AppendCharacterData(StringBuilder sb, string label, CharacterProfile character)
        {
            sb.AppendLine($"--- {label}: {character.DisplayName} ---");
            sb.AppendLine($"Level: {character.Level}");
            sb.AppendLine($"Bio: {character.Bio}");
            
            sb.AppendLine("Stats:");
            foreach (var stat in new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness })
            {
                sb.AppendLine($"- {stat}: {character.Stats.GetEffective(stat):+#;-#;0}");
            }
            
            sb.AppendLine("Shadows:");
            foreach (var shadow in new[] { ShadowStatType.Dread, ShadowStatType.Fixation, ShadowStatType.Denial, ShadowStatType.Madness })
            {
                sb.AppendLine($"- {shadow}: {character.Stats.GetShadow(shadow)}");
            }
        }
    }
}
