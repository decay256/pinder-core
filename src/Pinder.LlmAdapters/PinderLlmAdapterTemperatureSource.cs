using System;

namespace Pinder.LlmAdapters
{
    internal enum PinderLlmAdapterPhase
    {
        DialogueOptions,
        DateeResponse,
        InterestChangeBeat,
        OverlayRewrite,
        SuccessImprovement,
        SteeringQuestion,
        HorninessQuestion,
    }

    internal sealed class PinderLlmAdapterTemperatureSource
    {
        private readonly PinderLlmAdapterOptions _options;

        public PinderLlmAdapterTemperatureSource(PinderLlmAdapterOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public double For(PinderLlmAdapterPhase phase)
        {
            switch (phase)
            {
                case PinderLlmAdapterPhase.DialogueOptions:
                    return _options.DialogueOptionsTemperature ?? LlmPhaseTemperatures.DialogueOptions;
                case PinderLlmAdapterPhase.DateeResponse:
                    return _options.DateeResponseTemperature ?? LlmPhaseTemperatures.DateeResponse;
                case PinderLlmAdapterPhase.InterestChangeBeat:
                    return _options.Temperature;
                case PinderLlmAdapterPhase.OverlayRewrite:
                    return _options.DeliveryTemperature ?? LlmPhaseTemperatures.OverlayRewrite;
                case PinderLlmAdapterPhase.SuccessImprovement:
                    return LlmPhaseTemperatures.SuccessImprovement;
                case PinderLlmAdapterPhase.SteeringQuestion:
                    return LlmPhaseTemperatures.SteeringQuestion;
                case PinderLlmAdapterPhase.HorninessQuestion:
                    return LlmPhaseTemperatures.HorninessQuestion;
                default:
                    throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown Pinder LLM adapter phase.");
            }
        }
    }
}
