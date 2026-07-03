using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using Pinder.Core.Progression;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;

namespace Pinder.Rules.Tests
{
    public class XpDocumentationAlignmentTests
    {
        private static string GetDocumentationPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var docPath = Path.Combine(dir.FullName, "docs", "modules", "xp-progression.md");
                if (File.Exists(docPath))
                {
                    return docPath;
                }
                dir = dir.Parent;
            }
            throw new FileNotFoundException("Could not find docs/modules/xp-progression.md");
        }

        [Fact]
        public void Test_LevelThresholds_MatchCodeLevelTable()
        {
            string docPath = GetDocumentationPath();
            var lines = File.ReadAllLines(docPath);
            var inThresholdTable = false;
            var levels = new List<(int level, int xp, int rollBonus, int buildPoints, int itemSlots)>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("| Level | XP Required |"))
                {
                    inThresholdTable = true;
                    continue;
                }
                if (inThresholdTable)
                {
                    if (!trimmed.StartsWith("|"))
                    {
                        if (levels.Count > 0)
                        {
                            break; // finished reading table
                        }
                        continue;
                    }
                    if (trimmed.Contains("---|") || trimmed.Contains("Level | XP Required"))
                    {
                        continue; // skip separator/header
                    }
                    var parts = trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && int.TryParse(parts[0].Trim(), out int level))
                    {
                        int xp = int.Parse(parts[1].Trim());
                        
                        // Parse roll bonus, handling '+' sign
                        string bonusStr = parts[2].Trim().Replace("+", "");
                        int rollBonus = int.Parse(bonusStr);
                        
                        // Parse build points, cleaning up any parentheses or words like (prestige reset) or (12 at creation)
                        string bpStr = parts[3].Trim();
                        if (bpStr.Contains(" "))
                        {
                            bpStr = bpStr.Substring(0, bpStr.IndexOf(" ")).Trim();
                        }
                        int buildPoints = int.Parse(bpStr);

                        int itemSlots = int.Parse(parts[4].Trim());

                        levels.Add((level, xp, rollBonus, buildPoints, itemSlots));
                    }
                }
            }

            Assert.NotEmpty(levels);

            foreach (var item in levels)
            {
                // 1. Assert XP threshold maps to the correct level in LevelTable
                Assert.Equal(item.level, LevelTable.GetLevel(item.xp, Pinder.LlmAdapters.GameDefinition.PinderDefaults));
                if (item.xp > 0)
                {
                    Assert.Equal(item.level - 1, LevelTable.GetLevel(item.xp - 1, Pinder.LlmAdapters.GameDefinition.PinderDefaults));
                }

                // 2. Assert roll bonus matches
                Assert.Equal(item.rollBonus, LevelTable.GetBonus(item.level, Pinder.LlmAdapters.GameDefinition.PinderDefaults));

                // 3. Assert build points matches
                Assert.Equal(item.buildPoints, LevelTable.GetBuildPointsForLevel(item.level, Pinder.LlmAdapters.GameDefinition.PinderDefaults));

                // 4. Assert item slots matches
                Assert.Equal(item.itemSlots, LevelTable.GetItemSlots(item.level, Pinder.LlmAdapters.GameDefinition.PinderDefaults));
            }
        }

        [Fact]
        public void Test_RiskTierMultipliers_MatchCodeSessionXpRecorder()
        {
            string docPath = GetDocumentationPath();
            var lines = File.ReadAllLines(docPath);
            var inRiskTable = false;
            var riskMultipliers = new Dictionary<string, double>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("| RiskTier | Multiplier |"))
                {
                    inRiskTable = true;
                    continue;
                }
                if (inRiskTable)
                {
                    if (!trimmed.StartsWith("|"))
                    {
                        if (riskMultipliers.Count > 0)
                        {
                            break;
                        }
                        continue;
                    }
                    if (trimmed.Contains("---|") || trimmed.Contains("RiskTier | Multiplier"))
                    {
                        continue;
                    }
                    var parts = trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        string tierName = parts[0].Trim();
                        string multStr = parts[1].Trim().ToLowerInvariant().Replace("×", "").Replace("x", "");
                        if (double.TryParse(multStr, out double mult))
                        {
                            riskMultipliers[tierName] = mult;
                        }
                    }
                }
            }

            Assert.NotEmpty(riskMultipliers);

            // Access internal SessionXpRecorder via Reflection
            var recorderType = typeof(XpLedger).Assembly.GetType("Pinder.Core.Conversation.SessionXpRecorder");
            Assert.NotNull(recorderType);

            foreach (var kvp in riskMultipliers)
            {
                var tierName = kvp.Key;
                var documentedMultiplier = kvp.Value;

                // Parse to RiskTier enum
                if (Enum.TryParse(tierName, out RiskTier riskTier))
                {
                    var ledger = new XpLedger();
                    var recorder = Activator.CreateInstance(recorderType, ledger, Pinder.LlmAdapters.GameDefinition.PinderDefaults);
                    var applyMethod = recorderType.GetMethod("ApplyRiskTierMultiplier", new Type[] { typeof(int), typeof(RiskTier) });
                    Assert.NotNull(applyMethod);

                    // Test multiple base XP values to assert exact multiplier and rounding behavior
                    int[] testBaseXpValues = { 2, 5, 10, 15, 100 };
                    foreach (var baseXp in testBaseXpValues)
                    {
                        int expected = (int)Math.Round(baseXp * documentedMultiplier);
                        var result = applyMethod.Invoke(recorder, new object[] { baseXp, riskTier });
                        Assert.NotNull(result);
                        int actual = (int)result;
                        Assert.Equal(expected, actual);
                    }
                }
                else
                {
                    Assert.Fail($"Documented RiskTier '{tierName}' could not be parsed as RiskTier enum.");
                }
            }
        }

        [Fact]
        public void Test_EndOfGameMultipliers_MatchCodeSessionXpRecorder()
        {
            string docPath = GetDocumentationPath();
            var lines = File.ReadAllLines(docPath);
            var inEndTable = false;
            var docEndGameXp = new Dictionary<string, double>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("| Outcome | XP |"))
                {
                    inEndTable = true;
                    continue;
                }
                if (inEndTable)
                {
                    if (!trimmed.StartsWith("|"))
                    {
                        if (docEndGameXp.Count > 0)
                        {
                            break;
                        }
                        continue;
                    }
                    if (trimmed.Contains("---|") || trimmed.Contains("Outcome | XP"))
                    {
                        continue;
                    }
                    var parts = trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        string outcomeKey = parts[0].Trim();
                        string valStr = parts[1].Trim().ToLowerInvariant().Replace("×", "").Replace("x", "");
                        if (double.TryParse(valStr, out double val))
                        {
                            var individualKeys = outcomeKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var key in individualKeys)
                            {
                                docEndGameXp[key.Trim()] = val;
                            }
                        }
                    }
                }
            }

            Assert.NotEmpty(docEndGameXp);

            // Access internal SessionXpRecorder via Reflection
            var recorderType = typeof(XpLedger).Assembly.GetType("Pinder.Core.Conversation.SessionXpRecorder");
            Assert.NotNull(recorderType);

            foreach (var kvp in docEndGameXp)
            {
                string outcomeStr = kvp.Key;
                double documentedVal = kvp.Value;

                if (Enum.TryParse(outcomeStr, out GameOutcome outcome))
                {
                    // Setup ledger with 100 base XP
                    var ledger = new XpLedger();
                    ledger.Record("BaseXP", 100);

                    var recorder = Activator.CreateInstance(recorderType, ledger, Pinder.LlmAdapters.GameDefinition.PinderDefaults);
                    var recordEndMethod = recorderType.GetMethod("RecordEndOfGameXp", new Type[] { typeof(GameOutcome) });
                    Assert.NotNull(recordEndMethod);

                    recordEndMethod.Invoke(recorder, new object[] { outcome });

                    // Retrieve multiplier used from the ledger ratio: TotalXp / 100.0
                    double codeMultiplier = ledger.TotalXp / 100.0;

                    // This is expected to fail on the current documentation because
                    // markdown says DateSecured = 50 and Unmatched = 5 (flat values),
                    // whereas code multiplier uses DateSecured = 3.0x and Unmatched = 1.0x.
                    Assert.Equal(documentedVal, codeMultiplier);
                }
                else
                {
                    Assert.Fail($"Documented Outcome '{outcomeStr}' could not be parsed as GameOutcome enum.");
                }
            }
        }
    }
}
