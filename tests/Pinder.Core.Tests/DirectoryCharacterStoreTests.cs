using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Round-trip + edge-case tests for <see cref="DirectoryCharacterStore"/>
    /// (issue #816).
    /// </summary>
    [Trait("Category", "Characters")]
    public class DirectoryCharacterStoreTests : IDisposable
    {
        private readonly string _tmpDir;

        public DirectoryCharacterStoreTests()
        {
            _tmpDir = Path.Combine(
                Path.GetTempPath(),
                "directory-character-store-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmpDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tmpDir))
                    Directory.Delete(_tmpDir, recursive: true);
            }
            catch
            {
                // best-effort cleanup; tests should not fail because of teardown.
            }
        }

        private static CharacterDefinition NewDefinition(
            string idHex = "550e8400-e29b-41d4-a716-446655440000",
            string name = "TestChar",
            int level = 1)
        {
            var spent = new Dictionary<StatType, int>
            {
                [StatType.Charm] = 1,
                [StatType.Rizz] = 1,
                [StatType.Honesty] = 1,
                [StatType.Chaos] = 1,
                [StatType.Wit] = 1,
                [StatType.SelfAwareness] = 1,
            };
            var shadows = new Dictionary<ShadowStatType, int>();
            foreach (ShadowStatType s in Enum.GetValues(typeof(ShadowStatType)))
                shadows[s] = 0;

            return new CharacterDefinition(
                schemaVersion: 1,
                characterId: Guid.Parse(idHex),
                name: name,
                genderIdentity: "they/them",
                bio: "test bio",
                level: level,
                items: new List<string>(),
                anatomy: new Dictionary<string, string>(),
                allocation: new AllocationBlock(spent, 0, shadows));
        }

        [Fact]
        public async Task EmptyDirectory_ListIds_ReturnsEmpty()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            var ids = await store.ListIdsAsync();
            Assert.Empty(ids);
        }

        [Fact]
        public async Task NonexistentDirectory_ListIds_ReturnsEmpty()
        {
            var store = new DirectoryCharacterStore(Path.Combine(_tmpDir, "no-such-subdir"));
            var ids = await store.ListIdsAsync();
            Assert.Empty(ids);
        }

        [Fact]
        public async Task SaveLoadRoundTrip_PreservesIdentityAndContent()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            var def = NewDefinition(name: "Roundtripper", level: 4);

            await store.SaveAsync(def);

            var loaded = await store.LoadAsync(def.CharacterId.ToString("D"));

            Assert.NotNull(loaded);
            Assert.Equal(def.CharacterId, loaded!.CharacterId);
            Assert.Equal(def.Name, loaded.Name);
            Assert.Equal(def.Level, loaded.Level);
        }

        [Fact]
        public async Task Save_OverwritesByCharacterId_KeepsExistingFilename()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            var def1 = NewDefinition(name: "Original");
            await store.SaveAsync(def1);

            string[] filesBefore = Directory.GetFiles(_tmpDir, "*.json");
            Assert.Single(filesBefore);

            // Save again with same id but different name; should overwrite
            // the same file rather than creating a second one.
            var def2 = NewDefinition(name: "Renamed");
            await store.SaveAsync(def2);

            string[] filesAfter = Directory.GetFiles(_tmpDir, "*.json");
            Assert.Single(filesAfter);
            Assert.Equal(filesBefore[0], filesAfter[0]);

            var loaded = await store.LoadAsync(def2.CharacterId.ToString("D"));
            Assert.Equal("Renamed", loaded!.Name);
        }

        [Fact]
        public async Task SaveTwoDifferentCharacters_WithSameName_CollidesIntoTwoFiles()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            var a = NewDefinition(idHex: "11111111-1111-4111-8111-111111111111", name: "Twin");
            var b = NewDefinition(idHex: "22222222-2222-4222-8222-222222222222", name: "Twin");

            await store.SaveAsync(a);
            await store.SaveAsync(b);

            string[] files = Directory.GetFiles(_tmpDir, "*.json");
            Assert.Equal(2, files.Length);

            // Both characters survive and are reachable by id.
            var loadedA = await store.LoadAsync(a.CharacterId.ToString("D"));
            var loadedB = await store.LoadAsync(b.CharacterId.ToString("D"));
            Assert.NotNull(loadedA);
            Assert.NotNull(loadedB);
            Assert.NotEqual(loadedA!.CharacterId, loadedB!.CharacterId);

            // Filenames differ — at least one is the disambiguated form.
            var names = files.Select(Path.GetFileName).ToArray();
            Assert.Contains("twin.json", names, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(names, n =>
                n!.StartsWith("twin-", StringComparison.OrdinalIgnoreCase) &&
                n.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ListIds_ReflectsSaveAndDelete()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            var a = NewDefinition(idHex: "11111111-1111-4111-8111-111111111111", name: "A");
            var b = NewDefinition(idHex: "22222222-2222-4222-8222-222222222222", name: "B");

            await store.SaveAsync(a);
            await store.SaveAsync(b);

            var idsAfterSaves = (await store.ListIdsAsync()).OrderBy(s => s).ToArray();
            Assert.Equal(new[] {
                "11111111-1111-4111-8111-111111111111",
                "22222222-2222-4222-8222-222222222222",
            }, idsAfterSaves);

            bool deleted = await store.DeleteAsync(a.CharacterId.ToString("D"));
            Assert.True(deleted);

            var idsAfterDelete = await store.ListIdsAsync();
            Assert.Single(idsAfterDelete);
            Assert.Equal("22222222-2222-4222-8222-222222222222", idsAfterDelete[0]);

            Assert.False(await store.ExistsAsync(a.CharacterId.ToString("D")));
            Assert.True(await store.ExistsAsync(b.CharacterId.ToString("D")));
        }

        [Fact]
        public async Task Delete_NonexistentId_ReturnsFalse()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            bool deleted = await store.DeleteAsync("00000000-0000-4000-8000-000000000000");
            Assert.False(deleted);
        }

        [Fact]
        public async Task Load_NonexistentId_ReturnsNull()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            var loaded = await store.LoadAsync("00000000-0000-4000-8000-000000000000");
            Assert.Null(loaded);
        }

        [Fact]
        public async Task IndexResolvesByCharacterId_NotByFilename()
        {
            // Place a character file with a deliberately misleading
            // filename (slug != character_id-derived name) and verify the
            // store still finds it by the in-file character_id.
            var def = NewDefinition(idHex: "11111111-1111-4111-8111-111111111111", name: "Real");
            string written = CharacterDefinitionWriter.Write(def);
            File.WriteAllText(Path.Combine(_tmpDir, "totally-different-slug.json"), written);

            var store = new DirectoryCharacterStore(_tmpDir);
            var loaded = await store.LoadAsync("11111111-1111-4111-8111-111111111111");

            Assert.NotNull(loaded);
            Assert.Equal("Real", loaded!.Name);
        }

        [Fact]
        public async Task Index_IgnoresCharacterSchemaJson()
        {
            // The repo ships data/characters/character-schema.json next to
            // the character files. The store must not try to parse it as a
            // character.
            string schemaContent = "{\"$schema\": \"http://json-schema.org/draft-07/schema#\", \"type\": \"object\"}";
            File.WriteAllText(Path.Combine(_tmpDir, "character-schema.json"), schemaContent);

            var def = NewDefinition();
            await new DirectoryCharacterStore(_tmpDir).SaveAsync(def);

            var ids = await new DirectoryCharacterStore(_tmpDir).ListIdsAsync();
            Assert.Single(ids);
            Assert.Equal(def.CharacterId.ToString("D"), ids[0]);
        }

        [Fact]
        public async Task Index_IgnoresMalformedJsonFiles()
        {
            // A stray invalid file in the directory must not blow up the
            // whole store.
            File.WriteAllText(Path.Combine(_tmpDir, "garbage.json"), "{ this is not json");

            var def = NewDefinition();
            await new DirectoryCharacterStore(_tmpDir).SaveAsync(def);

            var ids = await new DirectoryCharacterStore(_tmpDir).ListIdsAsync();
            Assert.Single(ids);
        }

        [Fact]
        public async Task ConcurrentSaves_AllSucceed()
        {
            // Basic concurrency: two tasks racing SaveAsync against the
            // same store should both land. The per-instance lock
            // serialises file writes; the test passes when both ids show
            // up in the index.
            var store = new DirectoryCharacterStore(_tmpDir);

            var defs = Enumerable.Range(0, 8).Select(i =>
                NewDefinition(
                    idHex: $"{i:x8}-1111-4111-8111-111111111111",
                    name: "Racer-" + i)).ToArray();

            await Task.WhenAll(defs.Select(d => store.SaveAsync(d)));

            var ids = (await store.ListIdsAsync()).OrderBy(s => s).ToArray();
            Assert.Equal(8, ids.Length);
            foreach (var d in defs)
                Assert.Contains(d.CharacterId.ToString("D"), ids);
        }

        [Fact]
        public async Task SaveAsync_NullDef_Throws()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!));
        }

        [Fact]
        public async Task LoadAsync_EmptyId_Throws()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(""));
        }

        [Fact]
        public async Task DeleteAsync_EmptyId_Throws()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(""));
        }

        [Fact]
        public async Task ExistsAsync_EmptyId_Throws()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            await Assert.ThrowsAsync<ArgumentException>(() => store.ExistsAsync(""));
        }

        [Fact]
        public async Task Cancellation_SaveAsync_Throws()
        {
            var store = new DirectoryCharacterStore(_tmpDir);
            var def = NewDefinition();
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(def, cts.Token));
        }

        [Fact]
        public void Slugify_EmptyAndWhitespace_ReturnsEmpty()
        {
            Assert.Equal("", DirectoryCharacterStore.Slugify(""));
            Assert.Equal("", DirectoryCharacterStore.Slugify("   "));
        }

        [Fact]
        public void Slugify_StripsPunctuationAndPathSeparators()
        {
            Assert.Equal("brick-haus", DirectoryCharacterStore.Slugify("Brick_haus"));
            Assert.Equal("zyx-444", DirectoryCharacterStore.Slugify("Zyx_444"));
            Assert.Equal("velvet-void", DirectoryCharacterStore.Slugify("Velvet/Void"));
            Assert.Equal("foo-bar", DirectoryCharacterStore.Slugify("foo!!!bar"));
            Assert.Equal("test", DirectoryCharacterStore.Slugify("///test///"));
        }

        [Fact]
        public async Task RealStarterFiles_ListIdsFindsAllEight()
        {
            // Wire the store at the repo's data/characters directory and
            // confirm we see all eight starter characters by their UUIDs.
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "data", "characters")))
                    break;
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir);

            var store = new DirectoryCharacterStore(Path.Combine(dir!, "data", "characters"));
            var ids = await store.ListIdsAsync();

            Assert.Equal(8, ids.Count);
            Assert.All(ids, id => Assert.True(Guid.TryParseExact(id, "D", out _)));
        }
    }
}
