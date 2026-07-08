using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests.Characters
{
    /// <summary>
    /// Contract tests for <see cref="IRemoteCharacterStore"/> exercised
    /// against the in-memory fake. These also verify that
    /// <see cref="IRemoteCharacterStore"/> is a drop-in
    /// <see cref="ICharacterStore"/> per issue #817.
    /// </summary>
    [Trait("Category", "Characters")]
    public class IRemoteCharacterStoreContractTests
    {
        private static CharacterDefinition NewDef(string idHex, string name = "Tester")
        {
            var spent = new Dictionary<StatType, int>
            {
                [StatType.Charm] = 1, [StatType.Rizz] = 1, [StatType.Honesty] = 1,
                [StatType.Chaos] = 1, [StatType.Wit] = 1, [StatType.SelfAwareness] = 1,
            };
            var shadows = new Dictionary<ShadowStatType, int>();
            foreach (ShadowStatType s in Enum.GetValues(typeof(ShadowStatType)))
                shadows[s] = 0;
            return new CharacterDefinition(
                schemaVersion: 2,
                characterId: Guid.Parse(idHex),
                name: name,
                genderIdentity: "they/them",
                bio: "test bio",
                level: 1,
                items: Array.Empty<string>(),
                anatomy: new Dictionary<string, float>(),
                allocation: new AllocationBlock(spent, 0, shadows));
        }

        private static CharacterAssetMetadata NewMeta(
            string idHex,
            IReadOnlyList<string>? tags = null,
            bool isPublic = true,
            string ownerId = "owner:test")
        {
            return new CharacterAssetMetadata(
                characterId: idHex,
                ownerId: ownerId,
                tags: tags ?? Array.Empty<string>(),
                isPublic: isPublic,
                createdAt: DateTimeOffset.MinValue,
                updatedAt: DateTimeOffset.MinValue);
        }

        [Fact]
        public void IRemoteCharacterStore_IsAlsoAnICharacterStore()
        {
            // Drop-in compatibility: a caller that already takes an
            // ICharacterStore must compile when handed an
            // IRemoteCharacterStore. Issue #817 treats this as a contract.
            using IRemoteCharacterStore remote = new InMemoryRemoteCharacterStore();
            ICharacterStore baseShape = remote;
            Assert.NotNull(baseShape);
            Assert.True(typeof(ICharacterStore).IsAssignableFrom(typeof(IRemoteCharacterStore)));
            Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(IRemoteCharacterStore)));
        }

        [Fact]
        public async Task PublishAsync_ThenLoadAsync_RoundTripsTheDefinition()
        {
            using var store = new InMemoryRemoteCharacterStore();
            var def = NewDef("11111111-1111-4111-8111-111111111111", name: "Round_Trip");

            var stamped = await store.PublishAsync(def, NewMeta("11111111-1111-4111-8111-111111111111"));
            Assert.NotEqual(DateTimeOffset.MinValue, stamped.CreatedAt);

            var loaded = await store.LoadAsync(def.CharacterId.ToString("D"));
            Assert.NotNull(loaded);
            Assert.Equal(def.CharacterId, loaded!.CharacterId);
            Assert.Equal("Round_Trip", loaded.Name);
        }

        [Fact]
        public async Task PublishAsync_StampsCreatedAtAndUpdatedAt_ServerSide()
        {
            using var store = new InMemoryRemoteCharacterStore();
            var def = NewDef("11111111-1111-4111-8111-111111111111");

            // Client supplies MinValue; server replaces.
            var clientMeta = NewMeta("11111111-1111-4111-8111-111111111111");
            Assert.Equal(DateTimeOffset.MinValue, clientMeta.CreatedAt);
            Assert.Equal(DateTimeOffset.MinValue, clientMeta.UpdatedAt);

            var stamped = await store.PublishAsync(def, clientMeta);

            Assert.NotEqual(DateTimeOffset.MinValue, stamped.CreatedAt);
            Assert.NotEqual(DateTimeOffset.MinValue, stamped.UpdatedAt);
        }

        [Fact]
        public async Task PublishAsync_PreservesTagsAndVisibilityFromClient()
        {
            using var store = new InMemoryRemoteCharacterStore();
            var def = NewDef("11111111-1111-4111-8111-111111111111");
            var meta = NewMeta(
                "11111111-1111-4111-8111-111111111111",
                tags: new[] { "official-pack", "starter" },
                isPublic: true);

            var stamped = await store.PublishAsync(def, meta);

            Assert.Equal(new[] { "official-pack", "starter" }, stamped.Tags.ToArray());
            Assert.True(stamped.IsPublic);
        }

        [Fact]
        public async Task PublishAsync_ReplacesOwnerId_WithServerIdentity()
        {
            // The server uses its authenticated identity; the client cannot
            // claim arbitrary owner ids. Verified by stamping the OwnerId
            // via SimulatedServiceOwnerId in the fake.
            using var store = new InMemoryRemoteCharacterStore { SimulatedServiceOwnerId = "service:authoritative" };
            var def = NewDef("11111111-1111-4111-8111-111111111111");
            var meta = NewMeta("11111111-1111-4111-8111-111111111111", ownerId: "client:lying");

            var stamped = await store.PublishAsync(def, meta);

            Assert.Equal("service:authoritative", stamped.OwnerId);
        }

        [Fact]
        public async Task QueryAsync_FiltersByTags_AllMustMatch()
        {
            using var store = new InMemoryRemoteCharacterStore();
            await store.PublishAsync(
                NewDef("11111111-1111-4111-8111-111111111111", "A"),
                NewMeta("11111111-1111-4111-8111-111111111111", tags: new[] { "official-pack" }));
            await store.PublishAsync(
                NewDef("22222222-2222-4222-8222-222222222222", "B"),
                NewMeta("22222222-2222-4222-8222-222222222222", tags: new[] { "official-pack", "starter" }));
            await store.PublishAsync(
                NewDef("33333333-3333-4333-8333-333333333333", "C"),
                NewMeta("33333333-3333-4333-8333-333333333333", tags: new[] { "community" }));

            // Single tag filter
            var pageOfficial = await store.QueryAsync(
                new CharacterAssetQuery(tags: new[] { "official-pack" }));
            Assert.Equal(2, pageOfficial.Items.Count);

            // Two-tag filter — only B has both.
            var pageBoth = await store.QueryAsync(
                new CharacterAssetQuery(tags: new[] { "official-pack", "starter" }));
            Assert.Single(pageBoth.Items);
            Assert.Equal("22222222-2222-4222-8222-222222222222", pageBoth.Items[0].CharacterId);

            // No-match tag.
            var pageNone = await store.QueryAsync(
                new CharacterAssetQuery(tags: new[] { "nonexistent" }));
            Assert.Empty(pageNone.Items);
        }

        [Fact]
        public async Task QueryAsync_FiltersByIsPublic()
        {
            using var store = new InMemoryRemoteCharacterStore();
            await store.PublishAsync(
                NewDef("11111111-1111-4111-8111-111111111111"),
                NewMeta("11111111-1111-4111-8111-111111111111", isPublic: true));
            await store.PublishAsync(
                NewDef("22222222-2222-4222-8222-222222222222"),
                NewMeta("22222222-2222-4222-8222-222222222222", isPublic: false));

            var publicPage = await store.QueryAsync(new CharacterAssetQuery(isPublic: true));
            Assert.Single(publicPage.Items);
            Assert.True(publicPage.Items[0].IsPublic);

            var privatePage = await store.QueryAsync(new CharacterAssetQuery(isPublic: false));
            Assert.Single(privatePage.Items);
            Assert.False(privatePage.Items[0].IsPublic);

            var allPage = await store.QueryAsync(new CharacterAssetQuery());
            Assert.Equal(2, allPage.Items.Count);
        }

        [Fact]
        public async Task QueryAsync_PaginationViaCursor_DrainsAllResults()
        {
            using var store = new InMemoryRemoteCharacterStore();
            for (int i = 0; i < 7; i++)
            {
                string id = $"{i:x8}-aaaa-4aaa-8aaa-000000000000";
                await store.PublishAsync(NewDef(id, name: "Char-" + i), NewMeta(id));
            }

            var seenIds = new List<string>();
            string? cursor = null;
            do
            {
                var page = await store.QueryAsync(new CharacterAssetQuery(limit: 3, cursor: cursor));
                seenIds.AddRange(page.Items.Select(m => m.CharacterId));
                cursor = page.NextCursor;
                Assert.True(page.Items.Count <= 3);
            } while (cursor != null);

            Assert.Equal(7, seenIds.Count);
            Assert.Equal(7, seenIds.Distinct().Count());
        }

        [Fact]
        public async Task GetMetadataAsync_ReturnsNullForUnknownId()
        {
            using var store = new InMemoryRemoteCharacterStore();
            var got = await store.GetMetadataAsync("00000000-0000-4000-8000-000000000000");
            Assert.Null(got);
        }

        [Fact]
        public async Task GetMetadataAsync_AfterPublish_ReturnsStampedMetadata()
        {
            using var store = new InMemoryRemoteCharacterStore();
            var def = NewDef("11111111-1111-4111-8111-111111111111");
            var stamped = await store.PublishAsync(def, NewMeta("11111111-1111-4111-8111-111111111111"));

            var got = await store.GetMetadataAsync(def.CharacterId.ToString("D"));
            Assert.NotNull(got);
            Assert.Equal(stamped.CharacterId, got!.CharacterId);
            Assert.Equal(stamped.UpdatedAt, got.UpdatedAt);
        }

        [Fact]
        public void CharacterAssetQuery_LimitClampsToRange()
        {
            Assert.Equal(1, new CharacterAssetQuery(limit: 0).Limit);
            Assert.Equal(1, new CharacterAssetQuery(limit: -5).Limit);
            Assert.Equal(CharacterAssetQuery.MaxLimit,
                new CharacterAssetQuery(limit: CharacterAssetQuery.MaxLimit + 99).Limit);
            Assert.Equal(CharacterAssetQuery.DefaultLimit, new CharacterAssetQuery().Limit);
        }

        [Fact]
        public void CharacterAssetMetadata_RejectsBlankCharacterId()
        {
            Assert.Throws<ArgumentException>(() =>
                new CharacterAssetMetadata(
                    characterId: "",
                    ownerId: "owner",
                    tags: Array.Empty<string>(),
                    isPublic: false,
                    createdAt: DateTimeOffset.UtcNow,
                    updatedAt: DateTimeOffset.UtcNow));
        }

        [Fact]
        public void CharacterAssetMetadata_RequiresTags()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CharacterAssetMetadata(
                    characterId: "11111111-1111-4111-8111-111111111111",
                    ownerId: "owner",
                    tags: null!,
                    isPublic: false,
                    createdAt: DateTimeOffset.UtcNow,
                    updatedAt: DateTimeOffset.UtcNow));
        }

        [Fact]
        public void CharacterAssetMetadata_DefaultAssetKindIsCharacterV1()
        {
            var meta = NewMeta("11111111-1111-4111-8111-111111111111");
            Assert.Equal("character/v1", meta.AssetKind);
            Assert.Equal(CharacterAssetMetadata.AssetKindCharacterV1, meta.AssetKind);
        }
    }
}
