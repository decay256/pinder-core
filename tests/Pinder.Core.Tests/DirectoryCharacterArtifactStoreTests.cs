using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Characters")]
    public sealed class DirectoryCharacterArtifactStoreTests : IDisposable
    {
        private const string CharacterId = "550e8400-e29b-41d4-a716-446655440000";
        private readonly string _directory;

        public DirectoryCharacterArtifactStoreTests()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "directory-character-artifact-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }

        [Fact]
        public async Task ReadAndRestore_PreserveExactStoredBytesIncludingUnknownFields()
        {
            byte[] original = Encoding.UTF8.GetBytes(DefinitionJson(
                CharacterId,
                "Original",
                "\"future_extension\":{\"nested\":[1,2,3]},\r\n  "));
            string path = Path.Combine(_directory, "original.json");
            await File.WriteAllBytesAsync(path, original);

            var store = new DirectoryCharacterStore(_directory);
            var captured = await store.ReadArtifactAsync(CharacterId);

            Assert.NotNull(captured);
            Assert.Equal(original, captured!.Content);

            byte[] migrated = Encoding.UTF8.GetBytes(DefinitionJson(CharacterId, "Migrated"));
            var written = await store.CompareExchangeArtifactAsync(
                CharacterId,
                captured.Revision,
                migrated);
            await store.CompareExchangeArtifactAsync(
                CharacterId,
                written.Revision,
                captured.Content);

            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }

        [Fact]
        public async Task CompareExchange_AcrossStoreInstances_AllowsOnlyOneWriter()
        {
            string path = Path.Combine(_directory, "shared.json");
            await File.WriteAllTextAsync(path, DefinitionJson(CharacterId, "Original"));

            var first = new DirectoryCharacterStore(_directory);
            var second = new DirectoryCharacterStore(_directory);
            var captured = await first.ReadArtifactAsync(CharacterId);
            Assert.NotNull(captured);

            byte[] firstWrite = Encoding.UTF8.GetBytes(DefinitionJson(CharacterId, "First"));
            byte[] secondWrite = Encoding.UTF8.GetBytes(DefinitionJson(CharacterId, "Second"));

            async Task<Exception?> Attempt(
                DirectoryCharacterStore writer,
                byte[] content)
            {
                try
                {
                    await writer.CompareExchangeArtifactAsync(
                        CharacterId,
                        captured!.Revision,
                        content);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            Exception?[] outcomes = await Task.WhenAll(
                Attempt(first, firstWrite),
                Attempt(second, secondWrite));

            Assert.Single(outcomes, outcome => outcome is null);
            var conflict = Assert.IsType<CharacterArtifactRevisionConflictException>(
                Assert.Single(outcomes, outcome => outcome is not null));
            Assert.Equal(captured!.Revision, conflict.ExpectedRevision);
            byte[] active = (await second.ReadArtifactAsync(CharacterId))!.Content;
            Assert.True(active.SequenceEqual(firstWrite) || active.SequenceEqual(secondWrite));
        }

        [Fact]
        public async Task EnumerateArtifacts_ReturnsValidIdsAndPerArtifactDiagnostics()
        {
            const string duplicateId = "8fd79168-06d1-47d7-b131-e575c6a25727";
            await File.WriteAllTextAsync(
                Path.Combine(_directory, "valid.json"),
                DefinitionJson(CharacterId, "Valid"));
            await File.WriteAllTextAsync(
                Path.Combine(_directory, "duplicate-a.json"),
                DefinitionJson(duplicateId, "Duplicate A"));
            await File.WriteAllTextAsync(
                Path.Combine(_directory, "duplicate-b.json"),
                DefinitionJson(duplicateId, "Duplicate B"));
            await File.WriteAllTextAsync(
                Path.Combine(_directory, "malformed.json"),
                "{\"character_id\":");

            var store = new DirectoryCharacterStore(_directory);
            CharacterStoreEnumeration result = await store.EnumerateArtifactsAsync();

            Assert.Equal(new[] { CharacterId }, result.CharacterIds);
            Assert.Equal(3, result.Diagnostics.Count);
            Assert.Contains(result.Diagnostics, item =>
                item.SourceId == "malformed.json"
                && item.Code == CharacterStoreDiagnosticCodes.MalformedArtifact);
            Assert.Equal(
                2,
                result.Diagnostics.Count(item =>
                    item.Code == CharacterStoreDiagnosticCodes.DuplicateCharacterId
                    && item.CharacterId == duplicateId));
            Assert.Equal(new[] { CharacterId }, await store.ListIdsAsync());
        }

        private static string DefinitionJson(
            string characterId,
            string name,
            string extension = "")
            => "{\r\n" +
               "  \"schema_version\": 2,\r\n" +
               $"  \"character_id\": \"{characterId}\",\r\n" +
               $"  \"name\": \"{name}\",\r\n" +
               "  \"gender_identity\": \"they/them\",\r\n" +
               "  \"bio\": \"test\",\r\n" +
               "  \"level\": 1,\r\n" +
               "  \"items\": [],\r\n" +
               "  \"anatomy\": {},\r\n" +
               "  \"allocation\": {\r\n" +
               "    \"spent\": {\"charm\":1,\"rizz\":1,\"honesty\":1,\"chaos\":1,\"wit\":1,\"self_awareness\":1},\r\n" +
               "    \"unspent\": 0,\r\n" +
               "    \"shadows\": {}\r\n" +
               "  },\r\n" +
               extension +
               "  \"consolidated_personality\": \"personality\"\r\n" +
               "}\r\n";
    }
}
