using System;

namespace Pinder.Core.Text
{
    /// <summary>
    /// Represents a tracked substring span within a compiled prompt string.
    /// Tracks the start index, end index, source file path, and YAML key name.
    /// </summary>
    public sealed class AnnotatedSpan
    {
        /// <summary>
        /// Starting 0-based index of the substring in the final compiled string.
        /// </summary>
        public int Start { get; }

        /// <summary>
        /// Ending exclusive index of the substring in the final compiled string.
        /// (i.e. Start + Length).
        /// </summary>
        public int End { get; }

        /// <summary>
        /// Length of the substring span.
        /// </summary>
        public int Length => End - Start;

        /// <summary>
        /// Repo-relative path to the source file (e.g., "data/prompts/structural.yaml").
        /// </summary>
        public string? SourceFile { get; }

        /// <summary>
        /// The key under which the template segment is stored in the source file.
        /// </summary>
        public string? Key { get; }

        /// <summary>
        /// Creates a new instance of <see cref="AnnotatedSpan"/>.
        /// </summary>
        public AnnotatedSpan(int start, int end, string? sourceFile, string? key)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "Start index must be non-negative.");
            if (end < start) throw new ArgumentOutOfRangeException(nameof(end), "End index must be greater than or equal to start index.");

            Start = start;
            End = end;
            SourceFile = sourceFile;
            Key = key;
        }

        public override string ToString()
        {
            return $"[{Start}..{End}] Source: {SourceFile ?? "unknown"} Key: {Key ?? "none"}";
        }
    }
}
