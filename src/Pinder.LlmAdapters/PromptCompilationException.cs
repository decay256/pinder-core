using System;

namespace Pinder.LlmAdapters
{
    public sealed class PromptCompilationException : Exception
    {
        public PromptCompilationException(string message) : base(message)
        {
        }
    }
}
