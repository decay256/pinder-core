using System;
using System.IO;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests.Prompts
{
    [Trait("Category", "Prompts")]
    public class Issue811LengthDoctrineFragmentTests
    {
        private static string RepoRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir, "data")) &&
                        Directory.Exists(Path.Combine(dir, "src")))
                        return dir;
                    dir = Directory.GetParent(dir)?.FullName;
                }
                throw new InvalidOperationException("Cannot find repo root from " + AppContext.BaseDirectory);
            }
        }

        [Fact]
        public void ProductionData_HasNo80WordFloors_InAnatomyOrItems()
        {
            // Arrange
            string anatomyJson = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "anatomy", "anatomy-parameters.json"));
            string itemsJson = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "items", "starter-items.json"));

            // Assert
            Assert.DoesNotContain("minimum 80 words per message, no exceptions", anatomyJson, StringComparison.Ordinal);
            Assert.DoesNotContain("minimum 80 words per message, no exceptions", itemsJson, StringComparison.Ordinal);
        }
    }
}
