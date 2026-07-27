using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Pinder.Core.Tests
{
    public class AssemblyVersioningTests
    {
        private static readonly Version ExpectedAssemblyVersion = ReadCanonicalAssemblyVersion();

        [Fact]
        public void CoreAssembly_HasCorrectVersion()
        {
            var version = typeof(Pinder.Core.Conversation.TurnResult).Assembly.GetName().Version;
            Assert.Equal(ExpectedAssemblyVersion, version);
        }

        [Fact]
        public void RulesAssembly_HasCorrectVersion()
        {
            var version = typeof(Pinder.Rules.RuleBook).Assembly.GetName().Version;
            Assert.Equal(ExpectedAssemblyVersion, version);
        }

        [Fact]
        public void LlmAdaptersAssembly_HasCorrectVersion()
        {
            var version = typeof(Pinder.LlmAdapters.PinderLlmAdapter).Assembly.GetName().Version;
            Assert.Equal(ExpectedAssemblyVersion, version);
        }

        [Theory]
        [InlineData("1.2.3", 1, 2, 3, 0)]
        [InlineData("9.8.7-preview.1", 9, 8, 7, 0)]
        public void AssemblyVersionGuard_DerivesFromCanonicalPackageVersion(
            string packageVersion,
            int major,
            int minor,
            int build,
            int revision)
        {
            var expected = new Version(major, minor, build, revision);

            Assert.Equal(expected, DeriveAssemblyVersion(packageVersion));
        }

        private static Version ReadCanonicalAssemblyVersion()
        {
            var propsPath = FindDirectoryBuildProps();
            var document = XDocument.Load(propsPath);
            var versionElement = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Version");

            Assert.True(versionElement != null, $"Could not find <Version> in {propsPath}.");

            return DeriveAssemblyVersion(versionElement!.Value.Trim());
        }

        private static Version DeriveAssemblyVersion(string packageVersion)
        {
            var stableVersion = packageVersion.Split(new[] { '-', '+' }, 2)[0];

            Assert.True(
                Version.TryParse(stableVersion, out var semanticVersion) &&
                semanticVersion.Major >= 0 &&
                semanticVersion.Minor >= 0 &&
                semanticVersion.Build >= 0,
                $"Directory.Build.props <Version> must be at least major.minor.patch; got '{packageVersion}'.");

            return new Version(
                semanticVersion.Major,
                semanticVersion.Minor,
                semanticVersion.Build,
                0);
        }

        private static string FindDirectoryBuildProps()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "Directory.Build.props");

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate Directory.Build.props from the test output directory.",
                AppContext.BaseDirectory);
        }
    }
}
