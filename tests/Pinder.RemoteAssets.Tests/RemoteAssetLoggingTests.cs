using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.RemoteAssets.Exceptions;
using Xunit;

namespace Pinder.RemoteAssets.Tests
{
    public sealed class RemoteAssetLoggingTests
    {
        private const string AssetId = "59aa20f2-46d6-4adc-89c1-6ea17f815020";

        [Fact]
        public async Task ExistsAsync_Logs_Structured_NotFound_Boundary()
        {
            var loggerFactory = new CapturingLoggerFactory();
            var (store, handler) = Make(loggerFactory);
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(""),
            });

            bool exists = await store.ExistsAsync(AssetId);

            Assert.False(exists);
            var complete = Assert.Single(
                loggerFactory.Entries,
                e => (string?)e.StateValue("RemoteAssetOperation") == "remote_asset.exists"
                     && (string?)e.StateValue("Outcome") == "not_found");
            Assert.Equal(AssetId, complete.StateValue("AssetId"));
            Assert.Equal(404, complete.StateValue("StatusCode"));
            Assert.NotNull(complete.StateValue("ElapsedMs"));
            Assert.DoesNotContain(
                loggerFactory.Entries,
                e => e.Message.Contains("test-token", StringComparison.OrdinalIgnoreCase)
                     || e.Message.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task PublishAsync_Logs_Bytes_Status_And_AssetKind()
        {
            var loggerFactory = new CapturingLoggerFactory();
            var (store, handler) = Make(loggerFactory);
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new ByteArrayContent(CannedPublishResponseBody()),
            });

            CharacterAssetMetadata result = await store.PublishAsync(
                StubDef(),
                new CharacterAssetMetadata(
                    characterId: AssetId,
                    ownerId: "user:test",
                    tags: Array.Empty<string>(),
                    isPublic: true,
                    createdAt: DateTimeOffset.MinValue,
                    updatedAt: DateTimeOffset.MinValue));

            Assert.Equal(AssetId, result.CharacterId);
            var complete = Assert.Single(
                loggerFactory.Entries,
                e => (string?)e.StateValue("RemoteAssetOperation") == "remote_asset.publish"
                     && (string?)e.StateValue("Outcome") == "success");
            Assert.Equal(AssetId, complete.StateValue("AssetId"));
            Assert.Equal(CharacterAssetMetadata.AssetKindCharacterV1, complete.StateValue("AssetKind"));
            Assert.Equal(201, complete.StateValue("StatusCode"));
            Assert.True((int?)complete.StateValue("MetadataBytes") > 0);
            Assert.True((int?)complete.StateValue("PayloadBytes") > 0);
        }

        [Fact]
        public async Task QueryAsync_Logs_Failure_Status_Without_ResponseBody()
        {
            var loggerFactory = new CapturingLoggerFactory();
            var (store, handler) = Make(loggerFactory);
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("server body should not be copied into log state"),
            });

            await Assert.ThrowsAsync<RemoteAssetServerException>(
                () => store.QueryAsync(new CharacterAssetQuery(assetKind: CharacterAssetMetadata.AssetKindCharacterV1)));

            var failure = Assert.Single(
                loggerFactory.Entries,
                e => (string?)e.StateValue("RemoteAssetOperation") == "remote_asset.query"
                     && (string?)e.StateValue("Outcome") == "failure");
            Assert.Equal(500, failure.StateValue("StatusCode"));
            Assert.Equal("RemoteAssetServerException", failure.StateValue("ExceptionType"));
            Assert.DoesNotContain("server body should not be copied", failure.Message);
            Assert.DoesNotContain(failure.State, kv => kv.Value is string value && value.Contains("server body should not be copied"));
        }

        private static (EigencoreCharacterStore store, FakeHttpMessageHandler handler) Make(
            ILoggerFactory loggerFactory)
        {
            var handler = new FakeHttpMessageHandler();
            var config = new Configuration(
                baseUrl: new Uri("https://eigencore.test/api/v1"),
                httpMessageHandler: handler,
                authTokenProvider: _ => Task.FromResult("test-token"),
                payloadParser: _ => StubDef(),
                defaultRetryAfter: TimeSpan.FromMilliseconds(1),
                payloadSerializer: _ => Encoding.UTF8.GetBytes("{\"schema_version\":1,\"character_id\":\"" + AssetId + "\"}"),
                loggerFactory: loggerFactory);
            return (new EigencoreCharacterStore(config), handler);
        }

        private static CharacterDefinition StubDef()
        {
            return new CharacterDefinition(
                schemaVersion: 2,
                characterId: Guid.Parse(AssetId),
                name: "Stub",
                genderIdentity: "they/them",
                bio: "stub",
                level: 1,
                items: Array.Empty<string>(),
                anatomy: new Dictionary<string, float>(),
                allocation: new AllocationBlock(
                    new Dictionary<StatType, int>(),
                    0,
                    new Dictionary<ShadowStatType, int>()));
        }

        private static byte[] CannedPublishResponseBody()
        {
            const string json =
                "{" +
                "\"asset_kind\":\"character/v1\"," +
                "\"asset_id\":\"" + AssetId + "\"," +
                "\"owner_id\":\"user:test\"," +
                "\"is_public\":true," +
                "\"tags\":[]," +
                "\"created_at\":\"2026-01-02T03:04:05+00:00\"," +
                "\"updated_at\":\"2026-01-02T03:04:06+00:00\"" +
                "}";
            return Encoding.UTF8.GetBytes(json);
        }

        private sealed class CapturingLoggerFactory : ILoggerFactory
        {
            private readonly List<CapturedLogEntry> _entries = new List<CapturedLogEntry>();

            public IReadOnlyList<CapturedLogEntry> Entries
            {
                get
                {
                    lock (_entries) return _entries.ToList();
                }
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new CapturingLogger(categoryName, _entries);
            }

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly List<CapturedLogEntry> _entries;

            public CapturingLogger(string category, List<CapturedLogEntry> entries)
            {
                _category = category;
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var structured = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var pair in pairs)
                    {
                        structured[pair.Key] = pair.Value;
                    }
                }

                var entry = new CapturedLogEntry(
                    logLevel,
                    _category,
                    formatter(state, exception),
                    structured);
                lock (_entries) _entries.Add(entry);
            }
        }

        private sealed class CapturedLogEntry
        {
            public CapturedLogEntry(
                LogLevel level,
                string category,
                string message,
                IReadOnlyDictionary<string, object?> state)
            {
                Level = level;
                Category = category;
                Message = message;
                State = state;
            }

            public LogLevel Level { get; }
            public string Category { get; }
            public string Message { get; }
            public IReadOnlyDictionary<string, object?> State { get; }

            public object? StateValue(string key)
            {
                return State.TryGetValue(key, out object? value) ? value : null;
            }
        }
    }
}
