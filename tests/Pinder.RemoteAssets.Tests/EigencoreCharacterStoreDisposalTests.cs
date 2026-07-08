using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.RemoteAssets;
using Xunit;

namespace Pinder.RemoteAssets.Tests
{
    public class EigencoreCharacterStoreDisposalTests
    {
        private const string AssetId = "59aa20f2-46d6-4adc-89c1-6ea17f815020";

        [Fact]
        public void IRemoteCharacterStore_Exposes_Disposal_Contract()
        {
            Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(IRemoteCharacterStore)));
            Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(EigencoreCharacterStore)));
        }

        [Fact]
        public async Task Dispose_Is_Idempotent_And_Does_Not_Dispose_Injected_Handler()
        {
            var handler = new DisposalTrackingHandler();
            var store = new EigencoreCharacterStore(MakeConfig(handler));

            store.Dispose();
            store.Dispose();

            Assert.False(handler.WasDisposed);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ExistsAsync(AssetId));
        }

        private static Configuration MakeConfig(HttpMessageHandler handler)
        {
            return new Configuration(
                baseUrl: new Uri("https://eigencore.test/api/v1"),
                httpMessageHandler: handler,
                authTokenProvider: _ => Task.FromResult("test-token"),
                payloadParser: _ => new CharacterDefinition(
                    schemaVersion: 2,
                    characterId: Guid.Parse(AssetId),
                    name: "Stub",
                    genderIdentity: "they/them",
                    bio: "stub",
                    level: 1,
                    items: Array.Empty<string>(),
                    anatomy: new System.Collections.Generic.Dictionary<string, float>(),
                    allocation: new AllocationBlock(
                        new System.Collections.Generic.Dictionary<StatType, int>(),
                        0,
                        new System.Collections.Generic.Dictionary<ShadowStatType, int>())));
        }

        private sealed class DisposalTrackingHandler : HttpMessageHandler
        {
            public bool WasDisposed { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("The disposed store should fail before sending HTTP.");
            }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
