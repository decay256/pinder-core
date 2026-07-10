using System;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    internal static class LlmOptionalTextGeneration
    {
        internal enum CancellationBehavior
        {
            ReturnEmpty,
            Throw
        }

        public static async Task<string> RunAsync(
            string generatorName,
            ILlmTransport transport,
            string systemPrompt,
            string userMessage,
            PromptEntry entry,
            string phase,
            double configuredTemperature,
            double defaultTemperature,
            int configuredMaxTokens,
            int defaultMaxTokens,
            Action<SetupGenerationResult>? onDegraded,
            CancellationBehavior cancellationBehavior,
            CancellationToken cancellationToken = default,
            bool passCancellationTokenToTransport = false)
        {
            if (generatorName == null) throw new ArgumentNullException(nameof(generatorName));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (phase == null) throw new ArgumentNullException(nameof(phase));

            try
            {
                double temperature = configuredTemperature != defaultTemperature
                    ? configuredTemperature
                    : entry.Temperature!.Value;
                int maxTokens = configuredMaxTokens != defaultMaxTokens
                    ? configuredMaxTokens
                    : entry.MaxTokens!.Value;

                CancellationToken sendCancellationToken = passCancellationTokenToTransport
                    ? cancellationToken
                    : default;

                string response = await transport
                    .SendAsync(
                        systemPrompt,
                        userMessage,
                        temperature,
                        maxTokens,
                        phase: phase,
                        ct: sendCancellationToken)
                    .ConfigureAwait(false);

                string trimmed = (response ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    onDegraded?.Invoke(
                        SetupGenerationResult.DegradedFailure(generatorName, "empty_output"));
                }

                return trimmed;
            }
            catch (OperationCanceledException)
            {
                if (cancellationBehavior == CancellationBehavior.ReturnEmpty)
                {
                    return string.Empty;
                }

                throw;
            }
            catch (Exception)
            {
                if (onDegraded != null)
                {
                    onDegraded.Invoke(
                        SetupGenerationResult.DegradedFailure(generatorName, "transport_error"));
                    return string.Empty;
                }

                throw;
            }
        }
    }
}
