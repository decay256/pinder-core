using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.Core.Conversation;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Text;
using Pinder.LlmAdapters;
using Xunit;
namespace Pinder.LlmAdapters.Tests
{
[Collection(StaticWiringCollection.Name)]
public sealed class Issue1427_PromptLayerContractTests
{
[Fact]
public void Default_registry_covers_the_active_runtime_catalog()
{
var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
PromptContractRegistry.CreateDefault().ValidateCompleteness(catalog);
}
[Fact]
public void Conflicting_hard_authorities_fail_with_provenance()
{
var registry = new PromptContractRegistry(new[]
{
new PromptLayerContract("one", "opponent_response", PromptContractRoleScope.Datee, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, true),
new PromptLayerContract("two", "opponent_response", PromptContractRoleScope.Datee, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, true),
});
var ex = Assert.Throws<PromptLayerContractException>(() => PromptContractLinter.Validate(
"opponent_response", PromptContractRoleScope.Datee, registry, Catalog(),
new[] { Document("one", "one"), Document("two", "two") }));
Assert.Equal("prompt_contract.authority.conflict", ex.ViolationCode);
Assert.Equal("two", ex.PromptKey);
Assert.Equal("one", ex.ConflictingKey);
Assert.NotNull(ex.SourceSpan);
}
[Fact]
public void Unregistered_configured_section_fails_with_its_annotated_range()
{
var registry = new PromptContractRegistry(Array.Empty<PromptLayerContract>());
var source = new AgentJournalSourceIdentity(
AgentJournalSourceKind.Configuration,
"data/prompts/templates.yaml",
"unregistered-section",
revision: "test");
var document = AnnotatedInvocationDocument.Create(
"test.unregistered",
AgentJournalInputRole.User,
"test",
"PRIVATE_FACT_CONTENT_1427",
new[]
{
new AgentJournalProvenanceRange(
"test.unregistered", 0, 10, AgentJournalRangeKind.Configured,
AgentJournalRedactionClass.None, source),
});
var ex = Assert.Throws<PromptLayerContractException>(() => PromptContractLinter.Validate(
"opponent_response", PromptContractRoleScope.Datee, registry, Catalog(), new[] { document }));
Assert.Equal("prompt_contract.registry.missing", ex.ViolationCode);
Assert.Equal("unregistered-section", ex.PromptKey);
Assert.Equal("user_template", ex.Field);
Assert.Equal("data/prompts/templates.yaml", ex.SourcePath);
Assert.Equal("utf16:0:10", ex.SourceSpan);
Assert.Contains("Register the annotated source", ex.RemediationSummary, StringComparison.Ordinal);
Assert.Contains("remediation=", ex.Message, StringComparison.Ordinal);
Assert.DoesNotContain("PRIVATE_FACT_CONTENT_1427", ex.Message + ex.RemediationSummary, StringComparison.Ordinal);
}
[Fact]
public void Invalid_admin_personality_template_is_rejected_with_source_location()
{
string temporaryRoot = Path.Combine(Path.GetTempPath(), "pinder-1427-prompts-" + Guid.NewGuid().ToString("N"));
var previous = PromptTemplates.Catalog;
try
{
CopyDirectory(FindPromptsRoot(), temporaryRoot);
string path = Path.Combine(temporaryRoot, "personality_consolidation.yaml");
File.WriteAllText(
path,
File.ReadAllText(path).Replace(
"Output plain prose only, 5-8 compact sentences. No markdown, no headings, no JSON.",
"They reject rules requiring emojis and end every reply with an ellipsis.",
StringComparison.Ordinal));
PromptCatalog prior = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
PromptTemplates.Catalog = prior;
var invalid = PromptCatalog.LoadFromDirectory(temporaryRoot);
var ex = Assert.Throws<PromptLayerContractException>(() => PromptTemplates.Catalog = invalid);
Assert.Equal("prompt_contract.personality.surface_style", ex.ViolationCode);
Assert.Equal("personality_consolidation", ex.PromptKey);
Assert.EndsWith("personality_consolidation.yaml", ex.SourcePath!);
Assert.NotNull(ex.SourceSpan);
Assert.Same(prior, PromptTemplates.Catalog);
}
finally
{
PromptTemplates.Catalog = previous;
if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
}
}
[Fact]
public void Admin_catalog_accepts_explicit_style_anti_mandate()
{
string temporaryRoot = Path.Combine(Path.GetTempPath(), "pinder-1427-negated-prompts-" + Guid.NewGuid().ToString("N"));
var previous = PromptTemplates.Catalog;
try
{
CopyDirectory(FindPromptsRoot(), temporaryRoot);
string path = Path.Combine(temporaryRoot, "personality_consolidation.yaml");
File.WriteAllText(
path,
File.ReadAllText(path).Replace(
"Output plain prose only, 5-8 compact sentences. No markdown, no headings, no JSON.",
"They reject instructions to keep replies clipped.",
StringComparison.Ordinal));
PromptCatalog valid = PromptCatalog.LoadFromDirectory(temporaryRoot);
PromptTemplates.Catalog = valid;
Assert.Same(valid, PromptTemplates.Catalog);
}
finally
{
PromptTemplates.Catalog = previous;
if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
}
}
[Fact]
public void Delivery_registry_and_emitter_use_canonical_key_for_every_stat_and_success_tier()
{
string deliveryPath = Path.GetFullPath(Path.Combine(FindPromptsRoot(), "..", "delivery-instructions.yaml"));
StatDeliveryInstructions instructions = StatDeliveryInstructions.LoadFrom(File.ReadAllText(deliveryPath));
PromptCatalog catalog = Catalog();
PromptContractRegistry registry = PromptContractRegistry.CreateDefault();
string[] tiers = { "clean", "strong", "critical", "exceptional", "nat20" };
foreach (StatType stat in Enum.GetValues(typeof(StatType)))
{
string statKey = stat == StatType.SelfAwareness ? "sa" : stat.ToString().ToLowerInvariant();
foreach (string tier in tiers)
{
string expected = "delivery_instructions." + statKey + "." + tier;
var context = new SuccessImprovementContext(
"player", "Datee", "Player", "hello", stat, tier,
new[] { (Sender: "Datee", Text: "hello") });
GameRunPromptDocumentPair? documents = GameRunPromptDocumentBuilder.BuildSuccessImprovementDocuments(
context, instructions, GameDefinition.PinderDefaults, catalog);
Assert.NotNull(documents);
Assert.Contains(documents!.User.Ranges, range => range.Source.KeyPath == expected);
Assert.Contains(expected, PromptRuntimeKeyInventory.ActiveKeys);
Assert.True(registry.TryGet(expected, out _), expected);
}
}
Assert.DoesNotContain(
PromptRuntimeKeyInventory.ActiveKeys,
key => key.StartsWith("delivery_instructions.self_awareness.", StringComparison.Ordinal));
}
[Fact]
public void Production_reconciliation_document_reports_yaml_field_and_line()
{
PromptCatalog catalog = Catalog();
PromptEntry entry = catalog.RequireCompleteEntry("datee-response-plan-reconciliation", "missing");
GameRunPromptDocumentPair documents = GameRunPromptDocumentBuilder.BuildReconciliationDocuments(
entry,
new Dictionary<string, string>
{
["candidate_plan_json"] = "{}",
["allowed_movements"] = "advance",
["allowed_conversational_moves"] = "react",
["allowed_stage_orders"] = "advance",
});
var registry = new PromptContractRegistry(new[]
{
new PromptLayerContract("datee-response-plan-reconciliation", "opponent_response", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.ResponsePlan, PromptContractAuthority.CurrentMove, PromptContractKnowledge.SameCharacterPrivate, true),
});
var ex = Assert.Throws<PromptLayerContractException>(() => PromptContractLinter.Validate(
"opponent_response", PromptContractRoleScope.Datee, registry, catalog,
new[] { documents.System, documents.User }));
Assert.Equal("prompt_contract.role.mismatch", ex.ViolationCode);
Assert.Equal("system_prompt", ex.Field);
Assert.EndsWith(".yaml", ex.SourcePath!);
Assert.StartsWith("line:", ex.SourceSpan!);
}
[Fact]
public void Registry_rejects_obsolete_entries_bidirectionally()
{
PromptCatalog catalog = Catalog();
PromptContractRegistry standard = PromptContractRegistry.CreateDefault();
var obsolete = new PromptLayerContract(
"obsolete-key", "any", PromptContractRoleScope.SharedEngine,
PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior,
PromptContractKnowledge.None, false);
var registry = new PromptContractRegistry(standard.Entries.Concat(new[] { obsolete }));
var ex = Assert.Throws<PromptLayerContractException>(() => registry.ValidateCompleteness(catalog));
Assert.Equal("prompt_contract.registry.obsolete", ex.ViolationCode);
Assert.Equal("obsolete-key", ex.PromptKey);
}
[Fact]
public void Synthesis_contract_is_rejected_in_gameplay_phase()
{
var registry = new PromptContractRegistry(new[]
{
new PromptLayerContract(
"personality_consolidation", "synthesis", PromptContractRoleScope.RoleNeutral,
PromptContractLayer.IdentityPersonality, PromptContractAuthority.Behavior,
PromptContractKnowledge.None, true),
});
var ex = Assert.Throws<PromptLayerContractException>(() => PromptContractLinter.Validate(
"opponent_response", PromptContractRoleScope.Datee, registry, Catalog(),
new[] { Document("personality_consolidation", "behavior") }));
Assert.Equal("prompt_contract.phase.mismatch", ex.ViolationCode);
Assert.Equal("synthesis", ex.ConflictingKey);
}
[Fact]
public void Reconciliation_typed_fact_and_output_conflicts_fail_before_provider_boundary()
{
PromptCatalog catalog = Catalog();
PromptEntry entry = catalog.RequireCompleteEntry("datee-response-plan-reconciliation", "missing");
GameRunPromptDocumentPair documents = GameRunPromptDocumentBuilder.BuildReconciliationDocuments(
entry,
new Dictionary<string, string>
{
["candidate_plan_json"] = "{}",
["allowed_movements"] = "advance",
["allowed_conversational_moves"] = "react",
["allowed_stage_orders"] = "advance",
});
Guid datee = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
Guid player = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
var privatePlayerFact = new OwnedPromptFactV1(
player,
ConversationParticipantRole.PlayerAvatar,
PromptFactVisibility.PrivateToSubject,
PromptFactSourceKind.CognitiveSubtext,
PromptFactSourceIds.CognitiveSubtext(player, 4),
"private pressure");
RoleFactAccessDecision rejectedFact = RoleFactAccessPolicy.Decide(
new RoleFactAccessRequest(datee, ConversationParticipantRole.Datee, privatePlayerFact));
var factContract = new PromptProviderContract(
PromptProviderOperation.DateeResponsePlanReconciliation,
PromptContractRoleScope.Datee,
new[] { documents.System, documents.User },
new[] { rejectedFact });
var factError = Assert.Throws<PromptLayerContractException>(() =>
PromptProviderContractValidator.Validate(
"opponent_response", documents.System.Text, documents.User.Text, factContract,
DateeResponsePlanStructuredContract.SchemaName + ":" + DateeResponsePlanStructuredContract.SchemaVersion,
catalog, PromptContractRegistry.CreateDefault()));
Assert.Equal("prompt_contract.fact_access.conflict", factError.ViolationCode);

var outputContract = new PromptProviderContract(
PromptProviderOperation.DateeResponsePlanReconciliation,
PromptContractRoleScope.Datee,
new[] { documents.System, documents.User });
var outputError = Assert.Throws<PromptLayerContractException>(() =>
PromptProviderContractValidator.Validate(
"opponent_response", documents.System.Text, documents.User.Text, outputContract,
"datee_performance:datee_performance.v1",
catalog, PromptContractRegistry.CreateDefault()));
Assert.Equal("prompt_contract.output.conflict", outputError.ViolationCode);
Assert.Equal(
DateeResponsePlanStructuredContract.SchemaName + ":" + DateeResponsePlanStructuredContract.SchemaVersion,
outputError.ConflictingKey);
}
[Fact]
public void Missing_provider_contract_fails_closed_without_synthetic_documents()
{
PromptCatalog catalog = Catalog();
var ex = Assert.Throws<PromptLayerContractException>(() =>
PromptProviderContractValidator.Validate(
"delivery", "system", "user", null, null, catalog, PromptContractRegistry.CreateDefault()));
Assert.Equal("prompt_contract.provider_contract.missing", ex.ViolationCode);
}
[Fact]
public async Task Production_adapter_conflict_makes_zero_provider_calls_and_preserves_history()
{
PromptCatalog catalog = Catalog();
PromptContractRegistry standard = PromptContractRegistry.CreateDefault();
PromptLayerContract Replace(PromptLayerContract contract) =>
contract.Key == "dialogue-options-instruction" || contract.Key == "engine-options-block"
? new PromptLayerContract(contract.Key, contract.Phase, contract.RoleScope, contract.Layer, PromptContractAuthority.CurrentMove, contract.Knowledge, true)
: contract;
var transport = new CountingTransport();
var options = new PinderLlmAdapterOptions
{
GameDefinition = GameDefinition.PinderDefaults,
PromptCatalog = catalog,
PromptContractRegistry = new PromptContractRegistry(standard.Entries.Select(Replace)),
};
var history = new[] { (Sender: "DATEE", Text: "hello") };
var context = new DialogueContext("", "", history, "hello", Array.Empty<string>(), 10, playerName: "Player", dateeName: "Datee", availableStats: new[] { StatType.Charm, StatType.Honesty });
await Assert.ThrowsAsync<PromptLayerContractException>(() => new PinderLlmAdapter(transport, options).GetDialogueOptionsAsync(context));
Assert.Equal(0, transport.Calls);
Assert.Single(history);
Assert.Equal("hello", history[0].Text);
}
private sealed class CountingTransport : ILlmTransport
{
public int Calls { get; private set; }
public Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int? maxTokens = null, string? phase = null, CancellationToken ct = default)
{ Calls++; return Task.FromResult(string.Empty); }
}
private static AnnotatedInvocationDocument Document(string key, string text)
{
var source = new AgentJournalSourceIdentity(
AgentJournalSourceKind.Configuration,
"data/prompts/templates.yaml",
key,
revision: "test");
return AnnotatedInvocationDocument.Create(
"test." + key,
AgentJournalInputRole.User,
"test",
text,
new[] { new AgentJournalProvenanceRange("test." + key, 0, text.Length, AgentJournalRangeKind.Configured, AgentJournalRedactionClass.None, source) });
}
private static string FindPromptsRoot()
{
string directory = AppContext.BaseDirectory;
for (int i = 0; i < 12; i++)
{
string candidate = Path.Combine(directory, "data", "prompts");
if (Directory.Exists(candidate)) return candidate;
string? parent = Directory.GetParent(directory)?.FullName;
if (parent == null) break;
directory = parent;
}
throw new DirectoryNotFoundException("Unable to locate data/prompts.");
}
private static PromptCatalog Catalog() => PromptCatalog.LoadFromDirectory(FindPromptsRoot());
private static void CopyDirectory(string source, string destination)
{
Directory.CreateDirectory(destination);
foreach (string file in Directory.GetFiles(source))
File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
foreach (string directory in Directory.GetDirectories(source))
CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
}
}
}
