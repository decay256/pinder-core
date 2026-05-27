using System;
using System.Collections.Generic;

namespace Pinder.Core.Text
{
    /// <summary>
    /// Holds the final built prompt text along with its substring annotations.
    /// </summary>
    public sealed class PromptTraceResult
    {
        /// <summary>
        /// The fully compiled final prompt string.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// The list of substring spans tracking keys and source files.
        /// </summary>
        public IReadOnlyList<AnnotatedSpan> Spans { get; }

        /// <summary>
        /// Creates a new instance of <see cref="PromptTraceResult"/>.
        /// </summary>
        public PromptTraceResult(string text, IReadOnlyList<AnnotatedSpan> spans)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Spans = spans ?? throw new ArgumentNullException(nameof(spans));
        }

        public override string ToString() => Text;
    }
}
