using System;
using System.Linq;
using Pinder.Core.Data;
using Xunit;

namespace Pinder.Core.Tests
{
    public class JsonTimingRepositoryTests
    {
        private const string SampleJson = @"[
            {
                ""id"": ""eager-texter"",
                ""baseDelayMinutes"": 3,
                ""varianceMultiplier"": 0.4,
                ""drySpellProbability"": 0.05,
                ""readReceipt"": ""shows""
            },
            {
                ""id"": ""chill-responder"",
                ""baseDelayMinutes"": 15,
                ""varianceMultiplier"": 0.6,
                ""drySpellProbability"": 0.1,
                ""readReceipt"": ""hides""
            }
        ]";

        [Fact]
        public void LoadsProfiles_CorrectCount()
        {
            var repo = new JsonTimingRepository(SampleJson);
            Assert.Equal(2, repo.GetAll().Count());
        }

        [Fact]
        public void GetProfile_ById_ReturnsCorrectValues()
        {
            var repo = new JsonTimingRepository(SampleJson);
            var profile = repo.GetProfile("eager-texter");

            Assert.NotNull(profile);
            Assert.Equal(3, profile!.BaseDelayMinutes);
            Assert.Equal(0.4f, profile.VarianceMultiplier, precision: 2);
            Assert.Equal(0.05f, profile.DrySpellProbability, precision: 3);
            Assert.Equal("shows", profile.ReadReceipt);
        }

        [Fact]
        public void GetProfile_NotFound_ReturnsNull()
        {
            var repo = new JsonTimingRepository(SampleJson);
            Assert.Null(repo.GetProfile("nonexistent"));
        }

        [Fact]
        public void Constructor_NotArray_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => new JsonTimingRepository(@"{ ""key"": 1 }"));
        }

        [Fact]
        public void Constructor_NullJson_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => new JsonTimingRepository(null!));
        }

        [Fact]
        public void DefaultReadReceipt_IsNeutral()
        {
            var json = @"[{ ""id"": ""test"", ""baseDelayMinutes"": 5, ""varianceMultiplier"": 0.0, ""drySpellProbability"": 0.0 }]";
            var repo = new JsonTimingRepository(json);
            var profile = repo.GetProfile("test");

            Assert.NotNull(profile);
            Assert.Equal("neutral", profile!.ReadReceipt);
        }
    }
}
