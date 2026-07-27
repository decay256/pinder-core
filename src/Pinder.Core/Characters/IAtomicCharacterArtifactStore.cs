using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Optional storage capability for byte-exact optimistic character writes.
    /// Implementations must make the revision comparison and replacement one
    /// atomic storage-boundary operation.
    /// </summary>
    public interface IAtomicCharacterArtifactStore
    {
        Task<CharacterArtifact?> ReadArtifactAsync(
            string characterId,
            CancellationToken ct = default);

        Task<CharacterArtifact> CompareExchangeArtifactAsync(
            string characterId,
            string expectedRevision,
            byte[] replacementContent,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Optional diagnostic enumeration capability. Artifact-level failures are
    /// returned alongside valid ids; transport/store-wide failures still throw.
    /// </summary>
    public interface ICharacterStoreDiagnosticEnumerator
    {
        Task<CharacterStoreEnumeration> EnumerateArtifactsAsync(
            CancellationToken ct = default);
    }

    public sealed class CharacterArtifact
    {
        public CharacterArtifact(string characterId, byte[] content)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("Character id must not be blank.", nameof(characterId));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            if (content.Length == 0)
                throw new ArgumentException("Artifact content must not be empty.", nameof(content));
            CharacterId = characterId;
            Revision = CharacterArtifactRevisions.Compute(content);
        }

        public string CharacterId { get; }
        public byte[] Content { get; }
        public string Revision { get; }
    }

    public sealed class CharacterArtifactRevisionConflictException : InvalidOperationException
    {
        public CharacterArtifactRevisionConflictException(
            string expectedRevision,
            string activeRevision)
            : base("Character artifact revision changed before atomic replacement.")
        {
            ExpectedRevision = expectedRevision;
            ActiveRevision = activeRevision;
        }

        public string ExpectedRevision { get; }
        public string ActiveRevision { get; }
    }

    public sealed class CharacterArtifactAtomicStoreUnavailableException : NotSupportedException
    {
        public CharacterArtifactAtomicStoreUnavailableException()
            : base("Character store does not provide atomic raw-artifact compare-and-swap.")
        {
        }
    }

    public static class CharacterArtifactRevisions
    {
        public static string Compute(byte[] content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            using (var sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(content);
                return BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    public sealed class CharacterStoreEnumeration
    {
        public CharacterStoreEnumeration(
            IReadOnlyList<string> characterIds,
            IReadOnlyList<CharacterStoreDiagnostic> diagnostics)
        {
            CharacterIds = characterIds ?? throw new ArgumentNullException(nameof(characterIds));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlyList<string> CharacterIds { get; }
        public IReadOnlyList<CharacterStoreDiagnostic> Diagnostics { get; }
    }

    public sealed class CharacterStoreDiagnostic
    {
        public CharacterStoreDiagnostic(
            string sourceId,
            string code,
            string message,
            string? characterId = null)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            CharacterId = characterId;
        }

        public string SourceId { get; }
        public string Code { get; }
        public string Message { get; }
        public string? CharacterId { get; }
    }

    public static class CharacterStoreDiagnosticCodes
    {
        public const string MalformedArtifact = "malformed_artifact";
        public const string UnreadableArtifact = "unreadable_artifact";
        public const string AccessDenied = "artifact_access_denied";
        public const string DuplicateCharacterId = "duplicate_character_id";
    }
}
