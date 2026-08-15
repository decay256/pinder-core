using System;
using System.Collections.Generic;
using Pinder.Core.Text;

namespace Pinder.Core.Tests.AgentJournals
{
    internal sealed class PromptTraceSourceIdentityTestResolver : IPromptTraceSourceIdentityResolver
    {
        private readonly IReadOnlyDictionary<string, string> _mappings;

        public PromptTraceSourceIdentityTestResolver(IReadOnlyDictionary<string, string> mappings)
        {
            _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        }

        public bool TryResolve(string? annotatedSourceFile, out string? sourceId)
        {
            if (annotatedSourceFile != null && _mappings.TryGetValue(annotatedSourceFile, out string value))
            {
                sourceId = value;
                return true;
            }
            sourceId = null;
            return false;
        }

        public static PromptTraceSourceIdentityTestResolver Map(string sourceFile, string sourceId)
            => new PromptTraceSourceIdentityTestResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [sourceFile] = sourceId,
                });

        public static PromptTraceSourceIdentityTestResolver Empty { get; }
            = new PromptTraceSourceIdentityTestResolver(
                new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
