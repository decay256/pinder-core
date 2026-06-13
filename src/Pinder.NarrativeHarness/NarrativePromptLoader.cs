using System;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Loads the editable narrative-testbed prompt from
    /// <c>data/prompts/narrative.yaml</c>. Resolves the file the SAME way the
    /// rest of the harness locates data (<see cref="HarnessDataLocator"/> —
    /// honors PINDER_DATA_PATH, walks up from the base dir, tolerates casing).
    ///
    /// The YAML carries a single top-level scalar key <c>narrative_prompt</c>
    /// holding a multi-line block. This loader reads that value verbatim; it is
    /// purely additive and nothing in the harness consumes it yet.
    /// </summary>
    public static class NarrativePromptLoader
    {
        private const string RelativePath = "data/prompts/narrative.yaml";
        private const string KeyName = "narrative_prompt";

        /// <summary>
        /// Resolve and read the narrative prompt using the harness base dir
        /// (<see cref="AppContext.BaseDirectory"/>).
        /// </summary>
        public static string Load() => Load(AppContext.BaseDirectory);

        /// <summary>
        /// Resolve and read the narrative prompt, starting the data search from
        /// <paramref name="baseDir"/>.
        /// </summary>
        /// <exception cref="FileNotFoundException">
        /// Thrown with locator context when narrative.yaml cannot be found.
        /// </exception>
        public static string Load(string baseDir)
        {
            string relative = Path.Combine("data", "prompts", "narrative.yaml");
            string? path = HarnessDataLocator.FindDataFile(baseDir, relative);
            if (path == null)
            {
                throw new FileNotFoundException(
                    $"Could not find {RelativePath} on the harness locator paths " +
                    $"(searched from baseDir '{baseDir}', honoring PINDER_DATA_PATH). " +
                    "The editable narrative prompt is required to seed the datee's " +
                    "== CONVERSATION ARC == slot.");
            }

            string text = File.ReadAllText(path);

            string value = ParseNarrativePrompt(text, path);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(
                    $"'{KeyName}' in {path} is missing or empty. It must hold a " +
                    "non-empty multi-line narrative prompt.");
            }

            return value;
        }

        private static string ParseNarrativePrompt(string yamlText, string path)
        {
            var stream = new YamlStream();
            using (var reader = new StringReader(yamlText))
            {
                stream.Load(reader);
            }

            if (stream.Documents.Count == 0)
            {
                throw new InvalidDataException(
                    $"{path} is empty or contains no YAML document.");
            }

            if (!(stream.Documents[0].RootNode is YamlMappingNode root))
            {
                throw new InvalidDataException(
                    $"{path} root must be a mapping with a top-level '{KeyName}' key.");
            }

            foreach (var entry in root.Children)
            {
                if (entry.Key is YamlScalarNode k && k.Value == KeyName)
                {
                    if (entry.Value is YamlScalarNode v)
                        return v.Value ?? string.Empty;

                    throw new InvalidDataException(
                        $"'{KeyName}' in {path} must be a scalar (multi-line) value.");
                }
            }

            throw new InvalidDataException(
                $"{path} is missing the required top-level key '{KeyName}'.");
        }
    }
}
