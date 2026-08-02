using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pi.AI;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters.Pi
{
    /// <summary>
    /// Adapts Pinder's existing prompt transport boundary to Pi's provider-neutral model API.
    /// Pinder retains prompt construction and response parsing; Pi owns provider execution.
    /// </summary>
    public sealed class PiLlmTransport : ILlmTransport
    {
        private readonly Model _model;
        private readonly Func<Model, Context, ModelsSimpleStreamOptions, Task<AssistantMessage>> _completeAsync;
        private readonly Func<string?, ModelsSimpleStreamOptions>? _optionsFactory;
        private readonly Func<long> _timestampMilliseconds;

        public PiLlmTransport(
            ModelsCollection models,
            Model model,
            Func<string?, ModelsSimpleStreamOptions>? optionsFactory = null)
            : this(
                model,
                (selectedModel, context, options) => models.CompleteSimpleAsync(selectedModel, context, options),
                optionsFactory,
                () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            if (models == null) throw new ArgumentNullException(nameof(models));
        }

        internal PiLlmTransport(
            Model model,
            Func<Model, Context, ModelsSimpleStreamOptions, Task<AssistantMessage>> completeAsync,
            Func<string?, ModelsSimpleStreamOptions>? optionsFactory = null,
            Func<long>? timestampMilliseconds = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _completeAsync = completeAsync ?? throw new ArgumentNullException(nameof(completeAsync));
            _optionsFactory = optionsFactory;
            _timestampMilliseconds = timestampMilliseconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public async Task<string> SendAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int maxTokens = 1024,
            string? phase = null,
            CancellationToken ct = default)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));
            ct.ThrowIfCancellationRequested();

            var context = new Context
            {
                SystemPrompt = systemPrompt,
                Messages = new List<Message>
                {
                    new UserMessage(userMessage, _timestampMilliseconds())
                }
            };

            ModelsSimpleStreamOptions options = _optionsFactory?.Invoke(phase) ?? new ModelsSimpleStreamOptions();
            options.Temperature = temperature;
            options.MaxTokens = maxTokens;
            options.CancellationToken = ct;

            AssistantMessage response = await _completeAsync(_model, context, options).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (response.StopReason == StopReason.Aborted)
            {
                throw new OperationCanceledException(response.ErrorMessage ?? "Pi model request was aborted.", ct);
            }

            if (response.StopReason == StopReason.Error)
            {
                throw new InvalidOperationException(response.ErrorMessage ?? "Pi model request failed.");
            }

            string text = TextUtilities.ContentText(response.Content);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Pi model response contained no text content.");
            }

            return text;
        }
    }
}
