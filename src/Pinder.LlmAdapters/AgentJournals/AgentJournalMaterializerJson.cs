using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.LlmAdapters.AgentJournals
{
    public static class AgentJournalMaterializerJson
    {
        public static string Serialize(AgentJournalMaterializationResult result)
            => AgentJournalJson.Serialize(result);

        public static AgentJournalMaterializationResult Deserialize(string json)
            => AgentJournalJson.Deserialize<AgentJournalMaterializationResult>(json);
    }
}
