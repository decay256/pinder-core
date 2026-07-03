using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;

namespace Pinder.LlmAdapters.Tests
{
    public class OptionsConsumptionGuardTests
    {
        private static readonly Dictionary<string, string> Allowlist = new();

        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string dataDir = Path.Combine(dir, "data");
                string srcDir = Path.Combine(dir, "src");
                if (Directory.Exists(dataDir) && Directory.Exists(srcDir))
                {
                    return Path.GetFullPath(dir);
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root in any ancestor of the test binary.");
        }

        [Fact]
        public void VerifyNoOrphanOptionFields()
        {
            string repoRoot = FindRepoRoot();
            string srcPath = Path.Combine(repoRoot, "src");

            var assembly = typeof(PinderLlmAdapterOptions).Assembly;
            var typesToGuard = new[]
            {
                assembly.GetType("Pinder.LlmAdapters.PinderLlmAdapterOptions")
                    ?? throw new Exception("Could not find PinderLlmAdapterOptions"),
                assembly.GetType("Pinder.LlmAdapters.Anthropic.AnthropicOptions")
                    ?? throw new Exception("Could not find AnthropicOptions")
            };

            // Recursively get all *.cs files under src/
            var csFiles = Directory.GetFiles(srcPath, "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToList();

            var orphans = new List<string>();

            foreach (var type in typesToGuard)
            {
                // Find declaring file of the options class under src/
                string declaringFile = Path.GetFullPath(
                    Directory.GetFiles(srcPath, $"{type.Name}.cs", SearchOption.AllDirectories)
                    .FirstOrDefault() ?? throw new FileNotFoundException($"Declaring file for {type.Name} not found under src/"));

                // Enumerate all public instance properties
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var prop in properties)
                {
                    // Skip if allowlisted (by property name or ClassName.PropertyName)
                    if (Allowlist.ContainsKey(prop.Name) || Allowlist.ContainsKey($"{type.Name}.{prop.Name}"))
                    {
                        continue;
                    }

                    // Scan ALL *.cs files under src/ EXCLUDING that property's own declaring file
                    bool hasReference = false;
                    string pattern = $@"\b{Regex.Escape(prop.Name)}\b";

                    foreach (var file in csFiles)
                    {
                        if (file.Equals(declaringFile, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // Read the content of the file
                        string content = File.ReadAllText(file);
                        if (Regex.IsMatch(content, pattern))
                        {
                            hasReference = true;
                            break;
                        }
                    }

                    if (!hasReference)
                    {
                        orphans.Add($"{type.Name}.{prop.Name}");
                    }
                }
            }

            if (orphans.Count > 0)
            {
                var errorMessages = orphans.Select(orphan =>
                    $"{orphan}: public option field with no production consumer — this is the #1287 dead-code pattern; wire it or remove it.");
                
                Assert.Fail(string.Join(Environment.NewLine, errorMessages));
            }
        }
    }
}
