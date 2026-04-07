using System;
using System.IO;
using Xunit;

namespace Pinder.Core.Tests
{
    public class DataArchitectureDocsTests
    {
        private static string GetProjectRoot()
        {
            var currentDir = Directory.GetCurrentDirectory();
            while (currentDir != null)
            {
                if (Directory.Exists(Path.Combine(currentDir, "docs")))
                {
                    return currentDir;
                }
                currentDir = Directory.GetParent(currentDir)?.FullName;
            }
            throw new Exception("Could not find project root");
        }

        // What: 1. docs/data-architecture.md exists and covers all five sections
        // Mutation: Fails if docs/data-architecture.md does not exist or lacks one of the required headings.
        [Fact]
        public void DataArchitectureDoc_ExistsAndContainsFiveSections()
        {
            var root = GetProjectRoot();
            var docPath = Path.Combine(root, "docs", "data-architecture.md");
            Assert.True(File.Exists(docPath), $"Expected file not found: {docPath}");

            var content = File.ReadAllText(docPath);
            Assert.Contains("Two-tier data model", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Configuration data files", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Extensibility model", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("What the LLM receives", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("How to extend", content, StringComparison.OrdinalIgnoreCase);
        }

        // What: 2. Consistent with current code (verify paths and loader class names)
        // Mutation: Fails if the document is missing required class names or paths.
        [Fact]
        public void DataArchitectureDoc_ContainsRequiredPathsAndClasses()
        {
            var root = GetProjectRoot();
            var docPath = Path.Combine(root, "docs", "data-architecture.md");
            var content = File.ReadAllText(docPath);

            var requiredTerms = new[]
            {
                "Pinder.Rules.RuleBook",
                "JsonItemRepository",
                "GameDefinition.LoadFrom",
                "SessionSystemPromptBuilder",
                "CharacterAssembler",
                "data/game-definition.yaml",
                "data/traps/traps.json",
                "data/items/starter-items.json",
                "rules/extracted/rules-v3-enriched.yaml",
                "archetypes/", // edge case
                "anatomy/" // edge case
            };

            foreach (var term in requiredTerms)
            {
                Assert.Contains(term, content);
            }
        }

        // What: 3. Cross-referenced from docs/architecture.md
        // Mutation: Fails if docs/architecture.md does not link to docs/data-architecture.md.
        [Fact]
        public void ArchitectureDoc_CrossReferencesDataArchitectureDoc()
        {
            var root = GetProjectRoot();
            var docPath = Path.Combine(root, "docs", "architecture.md");
            Assert.True(File.Exists(docPath), $"Expected file not found: {docPath}");

            var content = File.ReadAllText(docPath);
            Assert.Contains("data-architecture.md", content);
        }
    }
}
