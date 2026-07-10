using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Pinder.Tools.NarrativeHarness;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class GameDefinitionArcInjectorTests
    {
        private const string ArcHeader = "== CONVERSATION ARC ==";

        [Fact]
        public void SentinelDefinition_CoversEveryGameDefinitionConstructorParameter()
        {
            var coveredParameters = new HashSet<string>(StringComparer.Ordinal)
            {
                "name",
                "gameMasterPrompt",
                "playerAvatarRoleDescription",
                "dateeRoleDescription",
                "improvementPrompt",
                "steeringPrompt",
                "horninessPrompt",
                "horninessTimeModifiers",
                "globalDcBias",
                "shadowDcBias",
                "horninessDcBias",
                "archetypesEnabled",
                "maxTurns",
                "maxDialogueOptions",
                "maxDeliveryWords",
                "activeTrapInterestPenalty",
                "hungerForIntimacy",
                "terrorOfRejection",
                "xpFlatAwards",
                "xpSuccessBase",
                "xpRiskMultipliers",
                "xpTerminalMultipliers",
                "progressionXpThresholds",
                "progressionBuildPoints",
                "progressionLevelBonuses",
                "progressionItemSlots",
                "progressionFailurePoolTiers",
                "characterPromptStructure"
            };

            string[] constructorParameters = typeof(GameDefinition)
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .Single()
                .GetParameters()
                .Select(parameter => parameter.Name!)
                .ToArray();

            Assert.Empty(constructorParameters.Except(coveredParameters, StringComparer.Ordinal));
            Assert.Empty(coveredParameters.Except(constructorParameters, StringComparer.Ordinal));
        }

        [Fact]
        public void WithArc_PreservesEveryGameDefinitionFieldExceptGameMasterPrompt()
        {
            GameDefinition baseDef = CreateSentinelDefinition();

            GameDefinition withArc = GameDefinitionArcInjector.WithArc(baseDef, "turn 3 confession arc");

            AssertPropertiesPreservedExceptPrompt(baseDef, withArc);
        }

        [Fact]
        public void WithArc_AppendsTrimmedArcBlockOnceAtEndOfGameMasterPrompt()
        {
            GameDefinition baseDef = CreateSentinelDefinition();

            GameDefinition withArc = GameDefinitionArcInjector.WithArc(
                baseDef,
                "first beat\r\nsecond beat   \r\n   ");

            string expectedPrompt =
                "GM base prompt\n\n"
                + ArcHeader
                + "\n\nfirst beat\r\nsecond beat";

            Assert.Equal(expectedPrompt, withArc.GameMasterPrompt);
            Assert.Equal(1, CountOccurrences(withArc.GameMasterPrompt, ArcHeader));
            Assert.EndsWith("first beat\r\nsecond beat", withArc.GameMasterPrompt, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\t")]
        public void WithArc_BlankArcLeavesPromptUnchangedAndDoesNotAddArcHeader(string arcText)
        {
            GameDefinition baseDef = CreateSentinelDefinition();

            GameDefinition withArc = GameDefinitionArcInjector.WithArc(baseDef, arcText);

            Assert.Equal(baseDef.GameMasterPrompt, withArc.GameMasterPrompt);
            Assert.DoesNotContain(ArcHeader, withArc.GameMasterPrompt, StringComparison.Ordinal);
            AssertPropertiesPreservedExceptPrompt(baseDef, withArc);
        }

        private static GameDefinition CreateSentinelDefinition()
        {
            return new GameDefinition(
                name: "sentinel game",
                gameMasterPrompt: "GM base prompt   ",
                playerAvatarRoleDescription: "player sentinel role",
                dateeRoleDescription: "datee sentinel role",
                improvementPrompt: "improvement sentinel",
                steeringPrompt: "steering sentinel",
                horninessPrompt: "horniness sentinel",
                horninessTimeModifiers: new HorninessTimeModifiers(
                    morning: 1,
                    afternoon: 2,
                    evening: 3,
                    overnight: 4),
                globalDcBias: 5,
                shadowDcBias: -6,
                horninessDcBias: 7,
                archetypesEnabled: true,
                maxTurns: 31,
                maxDialogueOptions: 4,
                maxDeliveryWords: 81,
                activeTrapInterestPenalty: -0.75,
                hungerForIntimacy: 8,
                terrorOfRejection: 9,
                xpFlatAwards: new Dictionary<string, int>
                {
                    ["confession"] = 10
                },
                xpSuccessBase: new Dictionary<string, int>
                {
                    ["dc_low_max"] = 11,
                    ["dc_low_xp"] = 12,
                    ["dc_mid_max"] = 13,
                    ["dc_mid_xp"] = 14,
                    ["dc_high_xp"] = 15
                },
                xpRiskMultipliers: new Dictionary<string, double>
                {
                    ["bold"] = 1.75
                },
                xpTerminalMultipliers: new Dictionary<string, double>
                {
                    ["date_secured"] = 2.25
                },
                progressionXpThresholds: new Dictionary<string, int>
                {
                    ["2"] = 20
                },
                progressionBuildPoints: new Dictionary<string, int>
                {
                    ["3"] = 30
                },
                progressionLevelBonuses: new Dictionary<string, int>
                {
                    ["4"] = 40
                },
                progressionItemSlots: new Dictionary<string, int>
                {
                    ["5"] = 50
                },
                progressionFailurePoolTiers: new Dictionary<string, int>
                {
                    ["severe"] = 60
                },
                characterPromptStructure: new CharacterPromptStructure(
                    characterSpecHeader: "== SENTINEL CHARACTER ==",
                    playerAvatarCharacterTag: "SENTINEL_PLAYER",
                    dateeCharacterTag: "SENTINEL_DATEE"));
        }

        private static void AssertPropertiesPreservedExceptPrompt(
            GameDefinition expected,
            GameDefinition actual)
        {
            var properties = typeof(GameDefinition)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetIndexParameters().Length == 0)
                .Where(property => property.Name != nameof(GameDefinition.GameMasterPrompt))
                .ToArray();

            foreach (PropertyInfo property in properties)
            {
                object? expectedValue = property.GetValue(expected);
                object? actualValue = property.GetValue(actual);

                if (property.PropertyType.IsValueType || property.PropertyType == typeof(string))
                {
                    Assert.Equal(expectedValue, actualValue);
                }
                else
                {
                    Assert.Same(expectedValue, actualValue);
                }
            }
        }

        private static int CountOccurrences(string value, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }
    }
}
