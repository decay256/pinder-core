using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class AgentJournalCredentialShapeSecurityTests
    {
        [Theory]
        [InlineData("gh" + "p_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")]
        [InlineData("xo" + "xb-1234567890-abcdefghijklmnop")]
        [InlineData("AK" + "IAIOSFODNN7EXAMPLE")]
        public void SourceId_RejectsProviderCredentialShapesWithDeterministicCode(string credential)
        {
            AgentJournalValidationResult result = Validate(credential, "prompt-editor-target");

            Assert.Contains(result.Errors, error =>
                error.Code == AgentJournalValidator.CredentialShapedSourceIdentifier
                && error.Path.EndsWith(".source_id"));
        }

        [Theory]
        [InlineData("gh" + "p_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")]
        [InlineData("xo" + "xb-1234567890-abcdefghijklmnop")]
        [InlineData("AK" + "IAIOSFODNN7EXAMPLE")]
        public void EditorTarget_RejectsProviderCredentialShapesWithDeterministicCode(string credential)
        {
            AgentJournalValidationResult result = Validate("prompt.catalog", credential);

            Assert.Contains(result.Errors, error =>
                error.Code == AgentJournalValidator.CredentialShapedSourceIdentifier
                && error.Path.EndsWith(".editor_target_id"));
        }

        private static AgentJournalValidationResult Validate(string sourceId, string editorTargetId)
        {
            var source = new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Configuration,
                sourceId,
                "prompt.key",
                editorTargetId: editorTargetId);
            var range = new AgentJournalProvenanceRange(
                "doc.system",
                0,
                3,
                AgentJournalRangeKind.Configured,
                AgentJournalRedactionClass.SafeMetadata,
                source);
            var document = new AgentJournalInputDocument(
                "doc.system",
                AgentJournalInputRole.System,
                "abc",
                new[] { range });
            return AgentJournalValidator.Validate(
                AgentJournalTestRecords.Invocation(documents: new[] { document }));
        }
    }
}
