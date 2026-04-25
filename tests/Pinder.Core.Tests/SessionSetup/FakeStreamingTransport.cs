using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Tests.SessionSetup
{
    /// <summary>
    /// Test double for <see cref="IStreamingLlmTransport"/>. Yields a
    /// preconfigured sequence of fragments, optionally throwing a transport
    /// failure mid-stream or honouring cancellation between yields.
    /// </summary>
    internal sealed class FakeStreamingTransport : IStreamingLlmTransport
    {
        private readonly IReadOnlyList<string> _fragments;
        private readonly int? _throwAfterIndex;
        private readonly Exception? _throwException;
        private readonly bool _throwOnOpen;

        public string? LastSystemPrompt { get; private set; }
        public string? LastUserMessage { get; private set; }
        public double? LastTemperature { get; private set; }
        public int? LastMaxTokens { get; private set; }
        public int FragmentsYielded { get; private set; }

        public FakeStreamingTransport(IEnumerable<string> fragments)
        {
            _fragments = new List<string>(fragments);
        }

        private FakeStreamingTransport(
            IEnumerable<string> fragments,
            int? throwAfterIndex,
            Exception? throwException,
            bool throwOnOpen)
        {
            _fragments = new List<string>(fragments);
            _throwAfterIndex = throwAfterIndex;
            _throwException = throwException;
            _throwOnOpen = throwOnOpen;
        }

        public static FakeStreamingTransport ThatThrowsAfter(
            IEnumerable<string> fragments, int afterIndex, Exception ex) =>
            new FakeStreamingTransport(fragments, afterIndex, ex, throwOnOpen: false);

        public static FakeStreamingTransport ThatThrowsOnOpen(Exception ex) =>
            new FakeStreamingTransport(Array.Empty<string>(), throwAfterIndex: null, ex, throwOnOpen: true);

        public IAsyncEnumerable<string> SendStreamAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int maxTokens = 1024,
            CancellationToken cancellationToken = default)
        {
            LastSystemPrompt = systemPrompt;
            LastUserMessage = userMessage;
            LastTemperature = temperature;
            LastMaxTokens = maxTokens;

            if (_throwOnOpen)
                throw _throwException!;

            return Stream(cancellationToken);
        }

        private async IAsyncEnumerable<string> Stream(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (int i = 0; i < _fragments.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_throwAfterIndex.HasValue && i == _throwAfterIndex.Value)
                    throw _throwException!;

                yield return _fragments[i];
                FragmentsYielded++;

                // Yield asynchronously so cancellation between fragments is observable.
                await Task.Yield();
            }
        }
    }
}
