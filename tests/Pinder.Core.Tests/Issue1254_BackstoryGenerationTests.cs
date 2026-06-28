using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.SessionSetup;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.Core.Tests.Characters
{
    public class Issue1254_BackstoryGenerationTests
    {
        private class TestLlmTransport : ILlmTransport
        {
            public string LastSystemPrompt { get; private set; } = string.Empty;
            public string LastUserMessage { get; private set; } = string.Empty;
            public double LastTemperature { get; private set; }
            public int LastMaxTokens { get; private set; }
            public string ResponseToReturn { get; set; } = string.Empty;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken cancellationToken = default)
            {
                LastSystemPrompt = systemPrompt;
                LastUserMessage = userMessage;
                LastTemperature = temperature;
                LastMaxTokens = maxTokens;
                return Task.FromResult(ResponseToReturn);
            }
        }

        [Fact]
        [Trait("Category", "Characters")]
        public async Task BackstoryGenerator_CallsTransport_WithCorrectArguments_AndParsesResponse()
        {
            // Arrange
            var transport = new TestLlmTransport();
            var generator = new LlmBackstoryGenerator(transport);

            var validJson = "{" +
                "\"age_and_demographics\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"birthplace_and_origin\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"childhood_milieu\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"parental_dynamics\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"early_education_scars\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"higher_education\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"formative_intimacies\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"career_debut\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"current_profession\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"financial_hygiene\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"domestic_milieu\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"social_circle\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"recent_ex\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"career_low\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"delusional_plan\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"hyperfixations\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"ideological_posture\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"digital_footprint\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"physical_dysmorphia\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}," +
                "\"dependencies\": {\"BioLie\": \"a\", \"TragicReality\": \"b\"}" +
                "}";
            
            transport.ResponseToReturn = validJson;

            // Act
            var result = await generator.GenerateAsync(
                "Chad",
                "He/Him",
                "I am cool.",
                new[] { "Rich family", "Small hands (anatomy band modifier)" }
            );

            // Assert
            Assert.NotNull(result);
            Assert.Equal(20, result.Count);
            Assert.Contains("Chad", transport.LastUserMessage);
            Assert.Contains("He/Him", transport.LastUserMessage);
            Assert.Contains("Rich family", transport.LastUserMessage);
            Assert.Contains("Small hands (anatomy band modifier)", transport.LastUserMessage);
            Assert.Equal(0.7, transport.LastTemperature); // Default from Options
        }

        [Fact]
        [Trait("Category", "Characters")]
        public void BackstoryValidator_Validates20CategoriesCorrectly()
        {
            // Arrange
            var facts = new Dictionary<string, BackstoryFact>();
            foreach (var cat in BackstoryValidator.RequiredCategories)
            {
                facts[cat] = new BackstoryFact { BioLie = "Lie", TragicReality = "Truth" };
            }

            // Act
            bool isValid = BackstoryValidator.Validate(facts);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        [Trait("Category", "Characters")]
        public void BackstoryValidator_RejectsMissingCategories()
        {
            // Arrange
            var facts = new Dictionary<string, BackstoryFact>();
            for(int i = 0; i < 19; i++)
            {
                facts[BackstoryValidator.RequiredCategories[i]] = new BackstoryFact { BioLie = "Lie", TragicReality = "Truth" };
            }

            // Act
            bool isValid = BackstoryValidator.Validate(facts);

            // Assert
            Assert.False(isValid);
        }
    }
}
