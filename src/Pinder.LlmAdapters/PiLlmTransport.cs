using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pi.AI;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters.Pi
{
    /// <summary>
    /// Adapts Pinder's prompt transport contracts to Pi's provider-neutral model API.
    /// Pinder retains prompt construction and response parsing; Pi owns provider execution.
    /// </summary>
    public sealed class PiLlmTransport : ILlmTransport, IConversationLlmTransport, IStreamingLlmTransport,
        IStructuredLlmTransport, IStructuredConversationLlmTransport, ITokenUsageProvider
    {
        private readonly Model _model;
        private readonly Func<Model, Context, ModelsSimpleStreamOptions, Task<AssistantMessage>> _completeAsync;
        private readonly Func<Model, Context, ModelsSimpleStreamOptions, AssistantMessageEventStream> _stream;
        private readonly Func<string?, ModelsSimpleStreamOptions>? _optionsFactory;
        private readonly Func<long> _timestampMilliseconds;
        private readonly Action<AssistantMessage, string?>? _responseObserver;
        private readonly object _usageSync = new object();
        private long _inputTokens;
        private long _outputTokens;
        private long _cacheReadTokens;
        private long _cacheWriteTokens;
        private long _callCount;

        public bool SupportsConversationMessages => true;

        public bool SupportsStructuredConversationMessages => true;

        public PiLlmTransport(
            ModelsCollection models,
            Model model,
            Func<string?, ModelsSimpleStreamOptions>? optionsFactory = null,
            Action<AssistantMessage, string?>? responseObserver = null)
            : this(
                model,
                (selectedModel, context, options) => models.CompleteSimpleAsync(selectedModel, context, options),
                (selectedModel, context, options) => models.StreamSimple(selectedModel, context, options),
                optionsFactory,
                () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                responseObserver)
        {
            if (models == null) throw new ArgumentNullException(nameof(models));
        }

        internal PiLlmTransport(
            Model model,
            Func<Model, Context, ModelsSimpleStreamOptions, Task<AssistantMessage>> completeAsync,
            Func<string?, ModelsSimpleStreamOptions>? optionsFactory = null,
            Func<long>? timestampMilliseconds = null,
            Action<AssistantMessage, string?>? responseObserver = null)
            : this(model, completeAsync, (_, __, ___) =>
                throw new InvalidOperationException("No Pi stream delegate was configured."),
                optionsFactory, timestampMilliseconds, responseObserver)
        {
        }

        internal PiLlmTransport(
            Model model,
            Func<Model, Context, ModelsSimpleStreamOptions, Task<AssistantMessage>> completeAsync,
            Func<Model, Context, ModelsSimpleStreamOptions, AssistantMessageEventStream> stream,
            Func<string?, ModelsSimpleStreamOptions>? optionsFactory = null,
            Func<long>? timestampMilliseconds = null,
            Action<AssistantMessage, string?>? responseObserver = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _completeAsync = completeAsync ?? throw new ArgumentNullException(nameof(completeAsync));
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _optionsFactory = optionsFactory;
            _timestampMilliseconds = timestampMilliseconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _responseObserver = responseObserver;
        }

        public async Task<string> SendAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int? maxTokens = null,
            string? phase = null,
            CancellationToken ct = default)
        {
            return await SendConversationAsync(
                systemPrompt,
                Array.Empty<Pinder.Core.Conversation.ConversationMessage>(),
                userMessage,
                temperature,
                maxTokens,
                phase,
                ct).ConfigureAwait(false);
        }

        public async Task<string> SendConversationAsync(
            string systemPrompt,
            IReadOnlyList<Pinder.Core.Conversation.ConversationMessage> priorMessages,
            string userMessage,
            double temperature = 0.9,
            int? maxTokens = null,
            string? phase = null,
            CancellationToken cancellationToken = default)
        {
            Context context = CreateContext(systemPrompt, priorMessages, userMessage);
            var responseStatus = new ResponseStatusCapture();
            ModelsSimpleStreamOptions options = CreateOptions(
                phase, temperature, maxTokens, cancellationToken, responseStatus);
            AssistantMessage response = await _completeAsync(_model, context, options).ConfigureAwait(false);
            Observe(response, phase);
            EnsureSuccess(response, cancellationToken, false, responseStatus);

            string text = TextUtilities.ContentText(response.Content);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Pi model response contained no text content.");
            return text;
        }

        public async IAsyncEnumerable<string> SendStreamAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int? maxTokens = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default,
            string? phase = null)
        {
            Context context = CreateContext(systemPrompt, userMessage);
            var responseStatus = new ResponseStatusCapture();
            ModelsSimpleStreamOptions options = CreateOptions(
                phase, temperature, maxTokens, cancellationToken, responseStatus);
            AssistantMessageEventStream stream = _stream(_model, context, options);

            while (true)
            {
                EventReadResult<AssistantMessageEvent> read = await stream.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!read.HasValue) break;
                if (read.Value is TextDeltaEvent delta && delta.Delta.Length > 0) yield return delta.Delta;
            }

            AssistantMessage response = await stream.Result().ConfigureAwait(false);
            Observe(response, phase);
            EnsureSuccess(response, cancellationToken, true, responseStatus);
        }

        public async Task<StructuredLlmResponse> SendStructuredAsync(
            StructuredLlmRequest request,
            CancellationToken ct = default)
            => await SendStructuredConversationAsync(
                request,
                Array.Empty<Pinder.Core.Conversation.ConversationMessage>(),
                ct).ConfigureAwait(false);

        public async Task<StructuredLlmResponse> SendStructuredConversationAsync(
            StructuredLlmRequest request,
            IReadOnlyList<Pinder.Core.Conversation.ConversationMessage> priorMessages,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Context context = CreateContext(request.SystemPrompt, priorMessages, request.UserMessage);
            string toolName = NormalizeToolName(request.SchemaName);
            context.Tools = new List<Tool>
            {
                new Tool
                {
                    Name = toolName,
                    Description = "Return the requested structured result.",
                    Parameters = PiJsonSchemaParser.Parse(request.JsonSchema),
                    ConstrainedSampling = new JsonSchemaConstrainedSamplingConfig("require")
                }
            };

            var responseStatus = new ResponseStatusCapture();
            ModelsSimpleStreamOptions options = CreateOptions(
                request.Phase, request.Temperature, request.MaxTokens, cancellationToken, responseStatus);
            options.Extra["toolChoice"] = "required";
            var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> item in request.Metadata) metadata[item.Key] = item.Value;
            metadata["schemaName"] = request.SchemaName;
            metadata["schemaVersion"] = request.SchemaVersion;
            options.Metadata = metadata;

            AssistantMessage response = await _completeAsync(_model, context, options).ConfigureAwait(false);
            Observe(response, request.Phase);
            EnsureSuccess(response, cancellationToken, false, responseStatus);

            ToolCall? toolCall = response.Content.OfType<ToolCall>()
                .FirstOrDefault(call => string.Equals(call.Name, toolName, StringComparison.Ordinal));
            if (toolCall != null)
            {
                return new StructuredLlmResponse(
                    JsonSerializer.Serialize(toolCall.Arguments),
                    response.Provider.Value,
                    response.ResponseModel ?? response.Model,
                    usedNativeStructuredOutput: true,
                    metadata: request.Metadata,
                    validationMode: "pi_required_tool");
            }

            string text = TextUtilities.ContentText(response.Content);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Pi structured response contained neither a tool call nor text.");
            return new StructuredLlmResponse(
                text,
                response.Provider.Value,
                response.ResponseModel ?? response.Model,
                usedNativeStructuredOutput: false,
                metadata: request.Metadata,
                validationMode: "pi_text_fallback");
        }

        public SessionTokenUsage GetSessionUsage()
        {
            lock (_usageSync)
            {
                return new SessionTokenUsage
                {
                    InputTokens = ClampToInt(_inputTokens),
                    OutputTokens = ClampToInt(_outputTokens),
                    CacheReadInputTokens = ClampToInt(_cacheReadTokens),
                    CacheCreationInputTokens = ClampToInt(_cacheWriteTokens),
                    CallCount = ClampToInt(_callCount)
                };
            }
        }

        private Context CreateContext(string systemPrompt, string userMessage)
            => CreateContext(
                systemPrompt,
                Array.Empty<Pinder.Core.Conversation.ConversationMessage>(),
                userMessage);

        private Context CreateContext(
            string systemPrompt,
            IReadOnlyList<Pinder.Core.Conversation.ConversationMessage> priorMessages,
            string userMessage)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (priorMessages == null) throw new ArgumentNullException(nameof(priorMessages));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));
            var messages = new List<Message>(priorMessages.Count + 1);
            foreach (Pinder.Core.Conversation.ConversationMessage message in priorMessages)
            {
                if (message.Role == Pinder.Core.Conversation.ConversationMessage.UserRole)
                {
                    messages.Add(new UserMessage(message.Content, _timestampMilliseconds()));
                }
                else if (message.Role == Pinder.Core.Conversation.ConversationMessage.AssistantRole)
                {
                    messages.Add(new AssistantMessage(
                        new IAssistantMessageContent[] { new TextContent(message.Content) },
                        _model.Api,
                        _model.Provider,
                        _model.Id,
                        Usage.Zero,
                        StopReason.Stop,
                        _timestampMilliseconds()));
                }
                else
                {
                    throw new ArgumentException(
                        $"Unsupported conversation role '{message.Role}'.",
                        nameof(priorMessages));
                }
            }
            messages.Add(new UserMessage(userMessage, _timestampMilliseconds()));
            return new Context
            {
                SystemPrompt = systemPrompt,
                Messages = messages
            };
        }

        private ModelsSimpleStreamOptions CreateOptions(
            string? phase,
            double temperature,
            int? maxTokens,
            CancellationToken cancellationToken,
            ResponseStatusCapture responseStatus)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModelsSimpleStreamOptions options = _optionsFactory?.Invoke(phase) ?? new ModelsSimpleStreamOptions();
            options.Temperature = temperature;
            options.MaxTokens = maxTokens;
            options.CancellationToken = cancellationToken;
            ResponseObserver? existingObserver = options.OnResponse;
            options.OnResponse = async (response, model, observerCancellationToken) =>
            {
                responseStatus.Set(response);
                if (existingObserver != null)
                    await existingObserver(response, model, observerCancellationToken).ConfigureAwait(false);
            };
            return options;
        }

        private void Observe(AssistantMessage response, string? phase)
        {
            lock (_usageSync)
            {
                _inputTokens += response.Usage.Input;
                _outputTokens += response.Usage.Output;
                _cacheReadTokens += response.Usage.CacheRead;
                _cacheWriteTokens += response.Usage.CacheWrite;
                _callCount++;
            }
            try { _responseObserver?.Invoke(response, phase); }
            catch { }
        }

        private static void EnsureSuccess(
            AssistantMessage response,
            CancellationToken cancellationToken,
            bool streaming,
            ResponseStatusCapture responseStatus)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (response.StopReason == StopReason.Aborted)
                throw new OperationCanceledException(response.ErrorMessage ?? "Pi model request was aborted.", cancellationToken);
            if (response.StopReason != StopReason.Error) return;
            if (responseStatus.StatusCode.HasValue)
            {
                HttpStatusCode statusCode = (HttpStatusCode)responseStatus.StatusCode.Value;
                LlmFailureKind failureKind = Classify(statusCode);
                string statusMessage = failureKind == LlmFailureKind.RateLimited
                    ? $"Pi model request was rate limited (HTTP {(int)statusCode})."
                    : $"Pi model request failed (HTTP {(int)statusCode}).";
                throw new LlmTransportException(statusMessage, failureKind, statusCode, responseStatus.RetryAfter);
            }
            string message = response.ErrorMessage ?? "Pi model request failed.";
            if (streaming) throw new LlmTransportException(message);
            throw new InvalidOperationException(message);
        }

        private static LlmFailureKind Classify(HttpStatusCode statusCode)
        {
            if ((int)statusCode == 429) return LlmFailureKind.RateLimited;
            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
                return LlmFailureKind.Unauthorized;
            if (statusCode == HttpStatusCode.NotFound) return LlmFailureKind.ModelNotFound;
            return LlmFailureKind.Network;
        }

        private static string NormalizeToolName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "emit_result";
            char[] result = value.Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-'
                ? character : '_').ToArray();
            string normalized = new string(result).Trim('_');
            return normalized.Length == 0 ? "emit_result" : normalized;
        }

        private static int ClampToInt(long value)
        {
            if (value <= 0) return 0;
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private sealed class ResponseStatusCapture
        {
            public int? StatusCode { get; private set; }
            public TimeSpan? RetryAfter { get; private set; }

            public void Set(ProviderResponse response)
            {
                StatusCode = response.Status;
                if (!TryGetHeader(response.Headers, "retry-after", out string? value)) return;
                if (int.TryParse(value, out int seconds) && seconds >= 0)
                    RetryAfter = TimeSpan.FromSeconds(seconds);
                else if (DateTimeOffset.TryParse(value, out DateTimeOffset date))
                    RetryAfter = date <= DateTimeOffset.UtcNow ? TimeSpan.Zero : date - DateTimeOffset.UtcNow;
            }

            private static bool TryGetHeader(
                IReadOnlyDictionary<string, string> headers,
                string name,
                out string? value)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    if (!string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
                    value = header.Value;
                    return true;
                }
                value = null;
                return false;
            }
        }
    }
}
