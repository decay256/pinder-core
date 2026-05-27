namespace Pinder.Core.Prompts
{
    /// <summary>
    /// Holds the lookup result of a structural prompt fragment from the yaml catalog,
    /// containing both its content and its source file location.
    /// </summary>
    public sealed class StructuralPromptResult
    {
        /// <summary>
        /// The raw text content of the prompt fragment.
        /// </summary>
        public string? Content { get; }

        /// <summary>
        /// The source file from which this prompt was loaded (e.g., "data/prompts/structural.yaml").
        /// </summary>
        public string? SourceFile { get; }

        /// <summary>
        /// Creates a new instance of <see cref="StructuralPromptResult"/>.
        /// </summary>
        public StructuralPromptResult(string? content, string? sourceFile)
        {
            Content = content;
            SourceFile = sourceFile;
        }
    }
}
