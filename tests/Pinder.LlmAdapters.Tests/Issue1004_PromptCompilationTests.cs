using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue1004_PromptCompilationTests
    {
        private const string ValidProfile = "anatomy: tier-3 / asset: leather";

        private static List<string> CreateValidBackstoryFacts()
        {
            return Enumerable.Range(1, SessionSystemPromptBuilder.RequiredBackstoryFactCount)
                .Select(i => $"fact-{i}")
                .ToList();
        }

        private static List<string> CreateValidPsychologicalStakes()
        {
            return Enumerable.Range(1, SessionSystemPromptBuilder.RequiredPsychologicalStakeCount)
                .Select(i => $"stake-{i}")
                .ToList();
        }

        private static PromptCompilationInput CreateValidInput()
        {
            return new PromptCompilationInput(
                ValidProfile,
                CreateValidBackstoryFacts(),
                CreateValidPsychologicalStakes()
            );
        }

        [Fact]
        public void CompilePrompt_AllThreeTokensReplaced_OutputContainsNoneOfTokensAndContainsExpandedValues()
        {
            // Arrange
            string template = "Profile:\n{character_profile}\n\nFacts:\n{backstory_facts}\n\nStakes:\n{psychological_stakes}";
            var input = CreateValidInput();

            // Act
            string result = SessionSystemPromptBuilder.CompilePrompt(template, input);

            // Assert
            Assert.DoesNotContain(SessionSystemPromptBuilder.CharacterProfileToken, result);
            Assert.DoesNotContain(SessionSystemPromptBuilder.BackstoryFactsToken, result);
            Assert.DoesNotContain(SessionSystemPromptBuilder.PsychologicalStakesToken, result);

            Assert.Contains(ValidProfile, result);
            Assert.Contains("1. fact-1", result);
            Assert.Contains("20. fact-20", result);
            Assert.Contains("1. stake-1", result);
            Assert.Contains("15. stake-15", result);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\n")]
        public void CompilePrompt_ThrowsPromptCompilationException_WhenCharacterProfileIsEmptyOrWhitespace(string invalidProfile)
        {
            // Arrange
            string template = "Profile:\n{character_profile}";
            var input = new PromptCompilationInput(
                invalidProfile,
                CreateValidBackstoryFacts(),
                CreateValidPsychologicalStakes()
            );

            // Act & Assert
            Assert.Throws<PromptCompilationException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(template, input));
        }

        [Fact]
        public void CompilePrompt_ThrowsPromptCompilationException_WhenBackstoryFactsCountIs19()
        {
            // Arrange
            string template = "Facts:\n{backstory_facts}";
            var facts = Enumerable.Range(1, 19).Select(i => $"fact-{i}").ToList();
            var input = new PromptCompilationInput(ValidProfile, facts, CreateValidPsychologicalStakes());

            // Act & Assert
            Assert.Throws<PromptCompilationException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(template, input));
        }

        [Fact]
        public void CompilePrompt_ThrowsPromptCompilationException_WhenBackstoryFactsCountIs21()
        {
            // Arrange
            string template = "Facts:\n{backstory_facts}";
            var facts = Enumerable.Range(1, 21).Select(i => $"fact-{i}").ToList();
            var input = new PromptCompilationInput(ValidProfile, facts, CreateValidPsychologicalStakes());

            // Act & Assert
            Assert.Throws<PromptCompilationException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(template, input));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void CompilePrompt_ThrowsPromptCompilationException_WhenBackstoryFactIsBlankOrWhitespaceOrNull(string invalidFact)
        {
            // Arrange
            string template = "Facts:\n{backstory_facts}";
            var facts = CreateValidBackstoryFacts();
            facts[5] = invalidFact!; // Replace one fact with invalid value
            var input = new PromptCompilationInput(ValidProfile, facts, CreateValidPsychologicalStakes());

            // Act & Assert
            Assert.Throws<PromptCompilationException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(template, input));
        }

        [Fact]
        public void CompilePrompt_ThrowsPromptCompilationException_WhenPsychologicalStakesCountIs14()
        {
            // Arrange
            string template = "Stakes:\n{psychological_stakes}";
            var stakes = Enumerable.Range(1, 14).Select(i => $"stake-{i}").ToList();
            var input = new PromptCompilationInput(ValidProfile, CreateValidBackstoryFacts(), stakes);

            // Act & Assert
            Assert.Throws<PromptCompilationException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(template, input));
        }

        [Fact]
        public void CompilePrompt_ThrowsPromptCompilationException_WhenPsychologicalStakesCountIs16()
        {
            // Arrange
            string template = "Stakes:\n{psychological_stakes}";
            var stakes = Enumerable.Range(1, 16).Select(i => $"stake-{i}").ToList();
            var input = new PromptCompilationInput(ValidProfile, CreateValidBackstoryFacts(), stakes);

            // Act & Assert
            Assert.Throws<PromptCompilationException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(template, input));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void CompilePrompt_ThrowsPromptCompilationException_WhenPsychologicalStakeIsBlankOrWhitespaceOrNull(string invalidStake)
        {
            // Arrange
            string template = "Stakes:\n{psychological_stakes}";
            var stakes = CreateValidPsychologicalStakes();
            stakes[5] = invalidStake!; // Replace one stake with invalid value
            var input = new PromptCompilationInput(ValidProfile, CreateValidBackstoryFacts(), stakes);

            // Act & Assert
            Assert.Throws<PromptCompilationException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(template, input));
        }

        [Fact]
        public void CompilePrompt_ThrowsPromptCompilationException_WhenTemplateContainsUnknownToken()
        {
            // Arrange
            string template = "Profile: {character_profile} and mystery: {mystery_token}";
            var input = CreateValidInput();

            // Act & Assert
            Assert.Throws<PromptCompilationException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(template, input));
        }

        [Fact]
        public void CompilePrompt_Determinism_CompilingIdenticalInputTwiceYieldsByteIdenticalOutput()
        {
            // Arrange
            string template = "Profile:\n{character_profile}\n\nFacts:\n{backstory_facts}\n\nStakes:\n{psychological_stakes}";
            var input1 = CreateValidInput();
            var input2 = CreateValidInput();

            // Act
            string result1 = SessionSystemPromptBuilder.CompilePrompt(template, input1);
            string result2 = SessionSystemPromptBuilder.CompilePrompt(template, input2);

            // Assert
            Assert.Equal(result1, result2);
        }

        [Fact]
        public void CompilePrompt_ThrowsArgumentNullException_OnNullTemplate()
        {
            // Arrange
            var input = CreateValidInput();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(null!, input));
        }

        [Fact]
        public void CompilePrompt_ThrowsArgumentNullException_OnNullInput()
        {
            // Arrange
            string template = "Profile:\n{character_profile}";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.CompilePrompt(template, null!));
        }

        [Fact]
        public void PromptCompilationInput_Ctor_ThrowsArgumentNullException_WhenNullArgsPassed()
        {
            // Assert
            Assert.Throws<ArgumentNullException>(() =>
                new PromptCompilationInput(null!, CreateValidBackstoryFacts(), CreateValidPsychologicalStakes()));

            Assert.Throws<ArgumentNullException>(() =>
                new PromptCompilationInput(ValidProfile, null!, CreateValidPsychologicalStakes()));

            Assert.Throws<ArgumentNullException>(() =>
                new PromptCompilationInput(ValidProfile, CreateValidBackstoryFacts(), null!));
        }

        private const string ExpectedGoldenCompiled = @"PROFILE:
anatomy: tier-3 / asset: leather

FACTS:
1. fact-1
2. fact-2
3. fact-3
4. fact-4
5. fact-5
6. fact-6
7. fact-7
8. fact-8
9. fact-9
10. fact-10
11. fact-11
12. fact-12
13. fact-13
14. fact-14
15. fact-15
16. fact-16
17. fact-17
18. fact-18
19. fact-19
20. fact-20

STAKES:
1. stake-1
2. stake-2
3. stake-3
4. stake-4
5. stake-5
6. stake-6
7. stake-7
8. stake-8
9. stake-9
10. stake-10
11. stake-11
12. stake-12
13. stake-13
14. stake-14
15. stake-15";

        [Fact]
        public void CompilePrompt_ByteEqualityGolden_AssertsMatchExactly()
        {
            // Arrange
            string template = @"PROFILE:
{character_profile}

FACTS:
{backstory_facts}

STAKES:
{psychological_stakes}";

            var input = CreateValidInput();

            // Act
            string actual = SessionSystemPromptBuilder.CompilePrompt(template, input);

            // Assert
            string expectedNormalized = ExpectedGoldenCompiled.Replace("\r\n", "\n").Trim();
            string actualNormalized = actual.Replace("\r\n", "\n").Trim();

            Assert.Equal(expectedNormalized, actualNormalized);
        }
    }
}
