using System.Collections.Generic;

namespace Pinder.Rules
{
    /// <summary>
    /// POCO representing a single rule entry from enriched YAML.
    /// </summary>
    public sealed class RuleEntry
    {
        public string Id { get; set; } = "";
        public string Section { get; set; } = "";
        public string Title { get; set; } = "";
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, object>? Condition { get; set; }
        public Dictionary<string, object>? Outcome { get; set; }
    }
}
