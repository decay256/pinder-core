using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Pi.AI;
using Pi.Agent.Core;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.LlmAdapters.AgentJournals
{
    public sealed class AgentJournalMaterializer
    {
        private readonly PiAgentJournalEntryCodec _customEntryCodec;

        public AgentJournalMaterializer()
            : this(new PiAgentJournalEntryCodec())
        {
        }

        public AgentJournalMaterializer(PiAgentJournalEntryCodec customEntryCodec)
        {
            _customEntryCodec = customEntryCodec ?? throw new ArgumentNullException(nameof(customEntryCodec));
        }

        public async Task<AgentJournalMaterializationResult> MaterializeAsync(
            LlmConversationSessionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            string format = snapshot.Format ?? string.Empty;
            if (!string.Equals(
                format,
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                StringComparison.Ordinal))
            {
                return AgentJournalMaterializationResult.UnsupportedFormat(
                    format,
                    "Only PiAgentSessionV1 Agent Snapshots can be materialized.");
            }

            SessionSnapshot decoded;
            try
            {
                decoded = SessionJsonCodec.DeserializeSnapshot(snapshot.Payload);
            }
            catch (JsonException exception)
            {
                return AgentJournalMaterializationResult.MalformedPayload(format, exception.Message);
            }
            catch (Newtonsoft.Json.JsonException exception)
            {
                return AgentJournalMaterializationResult.MalformedPayload(format, exception.Message);
            }
            catch (ArgumentException exception)
            {
                return AgentJournalMaterializationResult.MalformedPayload(format, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return AgentJournalMaterializationResult.MalformedPayload(format, exception.Message);
            }
            catch (SessionError exception)
            {
                return AgentJournalMaterializationResult.MalformedPayload(format, exception.Message);
            }

            AgentJournalMaterializationResult? validation = ValidateDecodedSnapshot(format, decoded);
            if (validation != null)
            {
                return validation;
            }
            validation = ValidateTree(format, decoded.Entries, activeLeafId: null, validateActiveLeaf: false);
            if (validation != null)
            {
                return validation;
            }

            var store = new InMemorySessionStore();
            try
            {
                await store.RestoreSnapshotAsync(decoded).ConfigureAwait(false);
                var repository = new SessionRepository<SessionMetadata, InMemorySessionCreateOptions, object>(
                    new SessionRepositoryOptions<SessionMetadata, InMemorySessionCreateOptions, object>
                    {
                        Store = store,
                    });
                ISession<SessionMetadata> restored = await repository.OpenAsync(decoded.Metadata).ConfigureAwait(false);
                IReadOnlyList<SessionTreeEntry> entries = await restored.GetEntriesAsync().ConfigureAwait(false);
                string? activeLeafId = await restored.GetLeafIdAsync().ConfigureAwait(false);

                return Project(format, decoded.Metadata, entries, activeLeafId);
            }
            catch (SessionError exception)
            {
                return AgentJournalMaterializationResult.InvalidSnapshot(
                    format,
                    "pi_restore_failed",
                    exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return AgentJournalMaterializationResult.InvalidSnapshot(
                    format,
                    "pi_restore_failed",
                    exception.Message);
            }
            catch (ArgumentException exception)
            {
                return AgentJournalMaterializationResult.InvalidSnapshot(
                    format,
                    "pi_restore_failed",
                    exception.Message);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static AgentJournalMaterializationResult? ValidateDecodedSnapshot(
            string format,
            SessionSnapshot decoded)
        {
            if (decoded == null)
            {
                return AgentJournalMaterializationResult.MalformedPayload(
                    format,
                    "Snapshot payload did not decode to a Pi session snapshot.");
            }
            if (decoded.Metadata == null)
            {
                return AgentJournalMaterializationResult.InvalidSnapshot(
                    format,
                    "missing_metadata",
                    "Snapshot metadata is required.");
            }
            if (string.IsNullOrWhiteSpace(decoded.Metadata.Id))
            {
                return AgentJournalMaterializationResult.InvalidSnapshot(
                    format,
                    "missing_session_id",
                    "Snapshot metadata id is required.");
            }
            if (decoded.Entries == null)
            {
                return AgentJournalMaterializationResult.InvalidSnapshot(
                    format,
                    "missing_entries",
                    "Snapshot entries are required.");
            }
            return null;
        }

        private AgentJournalMaterializationResult Project(
            string format,
            SessionMetadata metadata,
            IReadOnlyList<SessionTreeEntry> entries,
            string? activeLeafId)
        {
            var notices = new List<AgentJournalMaterializationNotice>();
            AgentJournalMaterializationResult? treeValidation = ValidateTree(
                format,
                entries,
                activeLeafId,
                validateActiveLeaf: true);
            if (treeValidation != null)
            {
                return treeValidation;
            }

            Dictionary<string, List<string>> childrenByParent = BuildChildren(entries);
            IReadOnlyList<SessionTreeEntry> orderedEntries = OrderTree(entries, childrenByParent);
            IReadOnlyList<string> activePathEntryIds = BuildActivePath(orderedEntries, activeLeafId);
            var activePath = new HashSet<string>(activePathEntryIds, StringComparer.Ordinal);
            List<NormalizedAgentJournalEntry> normalizedEntries = new List<NormalizedAgentJournalEntry>(orderedEntries.Count);

            for (int i = 0; i < orderedEntries.Count; i++)
            {
                SessionTreeEntry entry = orderedEntries[i];
                IReadOnlyList<string> childEntryIds = childrenByParent.TryGetValue(entry.Id, out List<string>? childIds)
                    ? (IReadOnlyList<string>)childIds
                    : Array.Empty<string>();
                NormalizedAgentJournalCustomEntry? customEntry = ProjectCustomEntry(entry, notices);
                normalizedEntries.Add(new NormalizedAgentJournalEntry(
                    i,
                    entry.Id,
                    NormalizeParentId(entry.ParentId),
                    entry.Type,
                    Kind(entry),
                    entry.Timestamp,
                    childEntryIds,
                    string.Equals(entry.Id, activeLeafId, StringComparison.Ordinal),
                    activePath.Contains(entry.Id),
                    ProjectSemanticMessage(entry),
                    customEntry,
                    lifecycleLabel: null));
            }

            var journal = new NormalizedAgentJournal(
                format,
                metadata.Id,
                metadata.CreatedAt,
                string.IsNullOrEmpty(activeLeafId) ? null : activeLeafId,
                activePathEntryIds,
                normalizedEntries,
                ProjectBranches(orderedEntries, childrenByParent, activePath));
            return AgentJournalMaterializationResult.Materialized(format, journal, notices);
        }

        private static AgentJournalMaterializationResult? ValidateTree(
            string format,
            IReadOnlyList<SessionTreeEntry> entries,
            string? activeLeafId,
            bool validateActiveLeaf)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var byId = new Dictionary<string, SessionTreeEntry>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                SessionTreeEntry entry = entries[i];
                if (entry == null)
                {
                    return AgentJournalMaterializationResult.InvalidSnapshot(
                        format,
                        "null_entry",
                        "Snapshot contains a null entry.");
                }
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    return AgentJournalMaterializationResult.InvalidSnapshot(
                        format,
                        "missing_entry_id",
                        "Every snapshot entry must have an id.");
                }
                if (!ids.Add(entry.Id))
                {
                    return AgentJournalMaterializationResult.InvalidSnapshot(
                        format,
                        "duplicate_entry_id",
                        "Snapshot contains duplicate entry id '" + entry.Id + "'.");
                }
                byId.Add(entry.Id, entry);
            }

            var roots = new List<SessionTreeEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                SessionTreeEntry entry = entries[i];
                string? parentId = NormalizeParentId(entry.ParentId);
                if (parentId == null)
                {
                    roots.Add(entry);
                    continue;
                }
                if (string.Equals(parentId, entry.Id, StringComparison.Ordinal))
                {
                    return AgentJournalMaterializationResult.InvalidSnapshot(
                        format,
                        "self_parentage",
                        "Entry '" + entry.Id + "' cannot be its own parent.");
                }
                if (!ids.Contains(parentId))
                {
                    return AgentJournalMaterializationResult.InvalidSnapshot(
                        format,
                        "invalid_parentage",
                        "Entry '" + entry.Id + "' references missing parent '" + parentId + "'.");
                }
            }

            foreach (SessionTreeEntry entry in entries)
            {
                var path = new HashSet<string>(StringComparer.Ordinal);
                SessionTreeEntry current = entry;
                while (true)
                {
                    if (!path.Add(current.Id))
                    {
                        return AgentJournalMaterializationResult.InvalidSnapshot(
                            format,
                            "cyclic_parentage",
                            "Snapshot parentage contains a cycle involving entry '" + current.Id + "'.");
                    }
                    string? parentId = NormalizeParentId(current.ParentId);
                    if (parentId == null)
                    {
                        break;
                    }
                    current = byId[parentId];
                }
            }

            if (entries.Count > 0 && roots.Count != 1)
            {
                return AgentJournalMaterializationResult.InvalidSnapshot(
                    format,
                    "ambiguous_roots",
                    "A non-empty snapshot must contain exactly one root entry; found "
                        + roots.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ".");
            }

            if (validateActiveLeaf)
            {
                if (entries.Count == 0)
                {
                    if (!string.IsNullOrEmpty(activeLeafId))
                    {
                        return AgentJournalMaterializationResult.InvalidSnapshot(
                            format,
                            "invalid_active_leaf",
                            "An empty snapshot cannot have an active leaf.");
                    }
                }
                else if (activeLeafId == null || activeLeafId.Length == 0 || !byId.ContainsKey(activeLeafId))
                {
                    return AgentJournalMaterializationResult.InvalidSnapshot(
                        format,
                        "invalid_active_leaf",
                        "The active leaf must reference an entry in the snapshot tree.");
                }
                else if (entries.Any(entry => string.Equals(
                    NormalizeParentId(entry.ParentId),
                    activeLeafId,
                    StringComparison.Ordinal)))
                {
                    return AgentJournalMaterializationResult.InvalidSnapshot(
                        format,
                        "invalid_active_leaf",
                        "Active entry '" + activeLeafId + "' is not a tree leaf.");
                }
            }

            return null;
        }

        private static Dictionary<string, List<string>> BuildChildren(IReadOnlyList<SessionTreeEntry> entries)
        {
            var childrenByParent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                SessionTreeEntry entry = entries[i];
                string? parentId = NormalizeParentId(entry.ParentId);
                if (parentId == null) continue;
                if (!childrenByParent.TryGetValue(parentId, out List<string>? children))
                {
                    children = new List<string>();
                    childrenByParent[parentId] = children;
                }
                children.Add(entry.Id);
            }
            Dictionary<string, SessionTreeEntry> byId = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            foreach (List<string> children in childrenByParent.Values)
            {
                children.Sort((left, right) => CompareEntries(byId[left], byId[right]));
            }
            return childrenByParent;
        }

        private static IReadOnlyList<SessionTreeEntry> OrderTree(
            IReadOnlyList<SessionTreeEntry> entries,
            Dictionary<string, List<string>> childrenByParent)
        {
            if (entries.Count == 0)
            {
                return Array.Empty<SessionTreeEntry>();
            }

            Dictionary<string, SessionTreeEntry> byId = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            SessionTreeEntry root = entries.Single(entry => NormalizeParentId(entry.ParentId) == null);
            var ordered = new List<SessionTreeEntry>(entries.Count);
            var pending = new Stack<SessionTreeEntry>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                SessionTreeEntry current = pending.Pop();
                ordered.Add(current);
                if (!childrenByParent.TryGetValue(current.Id, out List<string>? childIds))
                {
                    continue;
                }
                for (int i = childIds.Count - 1; i >= 0; i--)
                {
                    pending.Push(byId[childIds[i]]);
                }
            }
            return ordered;
        }

        private static IReadOnlyList<string> BuildActivePath(
            IReadOnlyList<SessionTreeEntry> entries,
            string? activeLeafId)
        {
            var activePath = new List<string>();
            if (string.IsNullOrEmpty(activeLeafId))
            {
                return activePath;
            }

            Dictionary<string, SessionTreeEntry> byId = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            string? current = activeLeafId;
            while (current != null && current.Length > 0 && byId.TryGetValue(current, out SessionTreeEntry? entry))
            {
                activePath.Add(entry.Id);
                current = NormalizeParentId(entry.ParentId);
            }
            activePath.Reverse();
            return activePath;
        }

        private static int CompareEntries(SessionTreeEntry left, SessionTreeEntry right)
        {
            int timestamp = string.Compare(left.Timestamp, right.Timestamp, StringComparison.Ordinal);
            return timestamp != 0
                ? timestamp
                : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        }

        private NormalizedAgentJournalCustomEntry? ProjectCustomEntry(
            SessionTreeEntry entry,
            ICollection<AgentJournalMaterializationNotice> notices)
        {
            if (!(entry is CustomEntry customEntry))
            {
                return null;
            }

            PiAgentJournalDecodeResult decoded = _customEntryCodec.Decode(customEntry);
            if (decoded.Compatibility.Warning != null)
            {
                notices.Add(new AgentJournalMaterializationNotice(
                    "custom_entry_compatibility",
                    decoded.Compatibility.Warning,
                    entry.Id,
                    decoded.Compatibility.CustomType));
            }

            return new NormalizedAgentJournalCustomEntry(
                customEntry.CustomType,
                decoded.Compatibility,
                decoded.Record as LlmInvocationRecord,
                decoded.Record as LlmResultRecord,
                decoded.Record as MessageLinkRecord);
        }

        private static NormalizedAgentJournalSemanticMessage? ProjectSemanticMessage(SessionTreeEntry entry)
        {
            if (!(entry is MessageEntry messageEntry))
            {
                return null;
            }

            AgentMessage agentMessage = messageEntry.Message;
            if (agentMessage.Message is UserMessage user)
            {
                return new NormalizedAgentJournalSemanticMessage(
                    agentMessage.Role,
                    TextUtilities.ContentText(user.Content),
                    agentMessage.Timestamp);
            }
            if (agentMessage.Message is AssistantMessage assistant)
            {
                return new NormalizedAgentJournalSemanticMessage(
                    agentMessage.Role,
                    TextUtilities.ContentText(assistant.Content),
                    agentMessage.Timestamp);
            }
            if (agentMessage.Custom != null)
            {
                return new NormalizedAgentJournalSemanticMessage(
                    agentMessage.Role,
                    string.Empty,
                    agentMessage.Timestamp);
            }
            return null;
        }

        private static IReadOnlyList<NormalizedAgentJournalBranch> ProjectBranches(
            IReadOnlyList<SessionTreeEntry> orderedEntries,
            Dictionary<string, List<string>> childrenByParent,
            ISet<string> activePath)
        {
            var branches = new List<NormalizedAgentJournalBranch>();
            foreach (SessionTreeEntry entry in orderedEntries)
            {
                if (!childrenByParent.TryGetValue(entry.Id, out List<string>? childIds) || childIds.Count < 2)
                {
                    continue;
                }
                branches.Add(new NormalizedAgentJournalBranch(
                    entry.Id,
                    childIds,
                    childIds.Where(activePath.Contains).ToArray()));
            }
            return branches;
        }

        private static AgentJournalEntryKind Kind(SessionTreeEntry entry)
        {
            if (entry is MessageEntry) return AgentJournalEntryKind.Message;
            if (entry is CustomEntry) return AgentJournalEntryKind.CustomEntry;
            if (entry is CustomMessageEntry) return AgentJournalEntryKind.CustomMessage;
            if (entry is CompactionEntry) return AgentJournalEntryKind.Compaction;
            if (entry is BranchSummaryEntry) return AgentJournalEntryKind.BranchSummary;
            if (entry is LabelEntry) return AgentJournalEntryKind.Label;
            if (entry is ModelChangeEntry) return AgentJournalEntryKind.ModelChange;
            if (entry is ActiveToolsChangeEntry) return AgentJournalEntryKind.ActiveToolsChange;
            if (entry is ThinkingLevelChangeEntry) return AgentJournalEntryKind.ThinkingLevelChange;
            if (entry is SessionInfoEntry) return AgentJournalEntryKind.SessionInfo;
            if (entry is LeafEntry) return AgentJournalEntryKind.Leaf;
            return AgentJournalEntryKind.Unknown;
        }

        private static string? NormalizeParentId(string? parentId)
            => string.IsNullOrEmpty(parentId) ? null : parentId;
    }
}
