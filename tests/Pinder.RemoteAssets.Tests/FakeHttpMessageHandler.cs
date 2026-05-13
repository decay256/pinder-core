using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.RemoteAssets.Tests
{
    /// <summary>
    /// Test-only <see cref="HttpMessageHandler"/> driven by an enqueued
    /// list of canned responses + a delegate factory. Records every
    /// request the wrapper issued so test assertions can inspect
    /// URL, method, and headers.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders =
            new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responders.Enqueue(responder);
        }

        public void Enqueue(HttpResponseMessage response)
        {
            _responders.Enqueue(_ => response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_responders.Count == 0)
                throw new InvalidOperationException(
                    $"FakeHttpMessageHandler received an unexpected request: {request.Method} {request.RequestUri}");
            var responder = _responders.Dequeue();
            var resp = responder(request);
            resp.RequestMessage = request;
            return Task.FromResult(resp);
        }
    }
}
