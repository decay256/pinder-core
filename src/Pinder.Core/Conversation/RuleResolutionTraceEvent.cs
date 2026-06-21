using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Issue #1219: structured event payload for the
    /// <c>GameSessionConfig.OnRuleResolution</c> callback.
    /// Fired when a game rule value is resolved (either via resolver or hardcoded fallback).
    /// </summary>
    public sealed class RuleResolutionTraceEvent
    {
        /// <summary>
        /// The key of the rule being resolved, e.g. 'momentum_bonus', 'failure_interest_delta',
        /// 'success_interest_delta', 'interest_state', or 'shadow_threshold_level'.
        /// </summary>
        public string RuleKey { get; }

        /// <summary>
        /// The source of the selection: 'resolver' or 'hardcoded_fallback'.
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// Whether a resolver was configured at all.
        /// </summary>
        public bool ResolverConfigured { get; }

        /// <summary>
        /// The resolved numeric value, if applicable.
        /// </summary>
        public int? NumericValue { get; }

        /// <summary>
        /// The resolved state value (e.g. enum name or string), if applicable.
        /// </summary>
        public string? StateValue { get; }

        public RuleResolutionTraceEvent(
            string ruleKey,
            string source,
            bool resolverConfigured,
            int? numericValue = null,
            string? stateValue = null)
        {
            RuleKey = ruleKey ?? throw new ArgumentNullException(nameof(ruleKey));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            ResolverConfigured = resolverConfigured;
            NumericValue = numericValue;
            StateValue = stateValue;
        }
    }
}
