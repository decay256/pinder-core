using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class AgentJournalSourceIdentitySecurityTests
    {
        [Theory]
        [InlineData("/etc/passwd")]
        [InlineData("C:/Users/eigen02/.ssh/id_rsa")]
        [InlineData("data/prompts/templates.yaml")]
        [InlineData("https://example.invalid/secret")]
        public void SourceId_RejectsPathAndUrlValues(string value)
        {
            var result = ValidateSource(sourceId: value);

            Assert.Contains(result.Errors, error =>
                error.Code == AgentJournalValidator.ForbiddenSourceLink
                && error.Path.EndsWith(".source_id"));
        }

        [Theory]
        [InlineData("Bearer-sk-test-secret-token")]
        [InlineData("sk-test-secret-token")]
        [InlineData("session-cookie-value")]
        [InlineData("provider_token_123")]
        public void SourceId_RejectsCredentialLikeValues(string value)
        {
            var result = ValidateSource(sourceId: value);

            Assert.Contains(result.Errors, error =>
                error.Code == AgentJournalValidator.CredentialShapedSourceIdentifier
                && error.Path.EndsWith(".source_id"));
        }

        [Theory]
        [InlineData("https://example.invalid/key", null, null, null, ".key_path", AgentJournalValidator.ForbiddenSourceLink)]
        [InlineData("prompt.key", "../../revision", null, null, ".revision", AgentJournalValidator.ForbiddenSourceLink)]
        [InlineData("prompt.key", "rev-1", "Bearer-sk-secret", null, ".content_hash", AgentJournalValidator.CredentialShapedSourceIdentifier)]
        [InlineData("prompt.key", "rev-1", "sha256:abcdef", "C:/editor/file", ".editor_target_id", AgentJournalValidator.ForbiddenSourceLink)]
        public void EverySourceIdentityField_RejectsForbiddenValues(
            string keyPath,
            string? revision,
            string? contentHash,
            string? editorTargetId,
            string expectedPathSuffix,
            string expectedCode)
        {
            var result = ValidateSource(
                sourceId: "prompt.catalog",
                keyPath: keyPath,
                revision: revision,
                contentHash: contentHash,
                editorTargetId: editorTargetId);

            Assert.Contains(result.Errors, error =>
                error.Code == expectedCode
                && error.Path.EndsWith(expectedPathSuffix));
        }

        [Fact]
        public void OpaqueCatalogIdentifiersAndHash_AreAllowed()
        {
            var result = ValidateSource(
                sourceId: "prompt.catalog",
                keyPath: "dialogue.system-v1",
                revision: "rev-001",
                contentHash: "sha256:abcdef0123456789",
                editorTargetId: "prompt-catalog-dialogue-system");

            Assert.DoesNotContain(result.Errors, error =>
                error.Code == AgentJournalValidator.ForbiddenSourceLink
                || error.Code == AgentJournalValidator.CredentialShapedSourceIdentifier);
        }

        private static AgentJournalValidationResult ValidateSource(
            string sourceId,
            string keyPath = "prompt.key",
            string? revision = "rev-1",
            string? contentHash = "sha256:abcdef",
            string? editorTargetId = "prompt-editor-target")
        {
            var source = new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Configuration,
                sourceId,
                keyPath,
                revision,
                contentHash,
                editorTargetId);
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
