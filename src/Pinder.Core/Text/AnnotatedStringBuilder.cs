using System;
using System.Collections.Generic;
using System.Text;

namespace Pinder.Core.Text
{
    /// <summary>
    /// Custom string builder utility that compiles strings while maintaining
    /// a list of substring spans tracking source files and YAML keys.
    /// </summary>
    public sealed class AnnotatedStringBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly List<AnnotatedSpan> _spans = new List<AnnotatedSpan>();

        /// <summary>
        /// Gets the current length of the compiled string.
        /// </summary>
        public int Length => _sb.Length;

        /// <summary>
        /// Gets the collection of tracked substring spans.
        /// </summary>
        public IReadOnlyList<AnnotatedSpan> Spans => _spans;

        /// <summary>
        /// Append a raw string with no source annotations.
        /// </summary>
        public AnnotatedStringBuilder Append(string? value)
        {
            if (value != null)
            {
                _sb.Append(value);
            }
            return this;
        }

        /// <summary>
        /// Append a raw string followed by a line terminator, with no source annotations.
        /// </summary>
        public AnnotatedStringBuilder AppendLine(string? value)
        {
            if (value != null)
            {
                _sb.AppendLine(value);
            }
            else
            {
                _sb.AppendLine();
            }
            return this;
        }

        /// <summary>
        /// Append a line terminator.
        /// </summary>
        public AnnotatedStringBuilder AppendLine()
        {
            _sb.AppendLine();
            return this;
        }

        /// <summary>
        /// Append a string segment with associated source file and key tracking.
        /// </summary>
        public AnnotatedStringBuilder Append(string? value, string? sourceFile, string? key)
        {
            if (value == null) return this;

            int start = _sb.Length;
            _sb.Append(value);
            int end = _sb.Length;

            if (end > start)
            {
                _spans.Add(new AnnotatedSpan(start, end, sourceFile, key));
            }
            return this;
        }

        /// <summary>
        /// Append a string segment followed by a line terminator, with associated source file and key tracking.
        /// </summary>
        public AnnotatedStringBuilder AppendLine(string? value, string? sourceFile, string? key)
        {
            if (value == null)
            {
                _sb.AppendLine();
                return this;
            }

            int start = _sb.Length;
            _sb.AppendLine(value);
            int end = _sb.Length;

            if (end > start)
            {
                _spans.Add(new AnnotatedSpan(start, end, sourceFile, key));
            }
            return this;
        }

        /// <summary>
        /// Convert the builder to its final compiled string.
        /// </summary>
        public override string ToString()
        {
            return _sb.ToString();
        }
    }
}
