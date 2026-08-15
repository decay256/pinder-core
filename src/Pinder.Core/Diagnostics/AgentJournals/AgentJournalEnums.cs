using System.Runtime.Serialization;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public enum AgentJournalInputRole
    {
        [EnumMember(Value = "system")]
        System,
        [EnumMember(Value = "user")]
        User,
        [EnumMember(Value = "tool")]
        Tool,
    }

    public enum AgentJournalSourceKind
    {
        [EnumMember(Value = "configuration")]
        Configuration,
        [EnumMember(Value = "catalog")]
        Catalog,
        [EnumMember(Value = "runtime_generated")]
        RuntimeGenerated,
    }

    public enum AgentJournalRangeKind
    {
        [EnumMember(Value = "configured")]
        Configured,
        [EnumMember(Value = "runtime_generated")]
        RuntimeGenerated,
    }

    public enum AgentJournalRedactionClass
    {
        [EnumMember(Value = "none")]
        None,
        [EnumMember(Value = "safe_metadata")]
        SafeMetadata,
        [EnumMember(Value = "redacted")]
        Redacted,
    }

    public enum AgentJournalTerminalStatus
    {
        [EnumMember(Value = "succeeded")]
        Succeeded,
        [EnumMember(Value = "failed")]
        Failed,
        [EnumMember(Value = "cancelled")]
        Cancelled,
        [EnumMember(Value = "rejected")]
        Rejected,
    }

    public enum AgentJournalCompatibilityKind
    {
        [EnumMember(Value = "known")]
        Known,
        [EnumMember(Value = "unknown_pinder_version")]
        UnknownPinderVersion,
        [EnumMember(Value = "non_pinder_custom_entry")]
        NonPinderCustomEntry,
        [EnumMember(Value = "invalid")]
        Invalid,
    }
}
