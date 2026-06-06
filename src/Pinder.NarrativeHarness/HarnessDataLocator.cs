using System;
using System.IO;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Self-contained data-file resolver (the production DataFileLocator lives
    /// in the session-runner project, which this tool does not reference). Walks
    /// up from a base dir, honoring PINDER_DATA_PATH first — same contract.
    /// Generates path casing variations for case-sensitive operating systems like Linux.
    /// </summary>
    internal static class HarnessDataLocator
    {
        private const string EnvVarName = "PINDER_DATA_PATH";

        public static string? FindDataFile(string baseDir, string relativePath)
        {
            string[] variations = GetPathVariations(relativePath);

            string? envPath = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (var v in variations)
                {
                    string envCandidate = Path.Combine(envPath!, v);
                    if (File.Exists(envCandidate) || Directory.Exists(envCandidate))
                        return Path.GetFullPath(envCandidate);
                }
            }

            string? dir = baseDir;
            while (dir != null)
            {
                foreach (var v in variations)
                {
                    string candidate = Path.Combine(dir, v);
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }

        private static string[] GetPathVariations(string relativePath)
        {
            string norm = relativePath.Replace('\\', '/');
            string[] segments = norm.Split('/');

            if (segments.Length == 0) return new[] { relativePath };

            var variations = new System.Collections.Generic.List<string> { relativePath };

            if (segments.Length >= 1)
            {
                var segs2 = (string[])segments.Clone();
                segs2[0] = Capitalize(segs2[0]);
                variations.Add(string.Join(Path.DirectorySeparatorChar.ToString(), segs2));

                if (segments.Length >= 2)
                {
                    var segs3 = (string[])segs2.Clone();
                    segs3[1] = Capitalize(segs3[1]);
                    variations.Add(string.Join(Path.DirectorySeparatorChar.ToString(), segs3));

                    var segs4 = (string[])segments.Clone();
                    segs4[1] = Capitalize(segs4[1]);
                    variations.Add(string.Join(Path.DirectorySeparatorChar.ToString(), segs4));
                }
            }

            return variations.ToArray();
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s == "data") return "Data";
            if (s == "characters") return "Characters";
            if (s == "items") return "Items";
            if (s == "anatomy") return "Anatomy";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
