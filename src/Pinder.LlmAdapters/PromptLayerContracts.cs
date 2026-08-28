using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters
{
    public enum PromptContractRoleScope { PlayerAvatar, Datee, SharedEngine, RoleNeutral }
    public enum PromptContractLayer { IdentityPersonality, Backstory, TextingStyle, ResponsePlan, PerformanceRule, OutputContract }
    public enum PromptContractAuthority { Behavior, SurfaceStyle, CurrentMove, Knowledge, OutputShape }
    public enum PromptContractKnowledge { None, PublicVisibleConversation, SameCharacterPrivate, CounterpartPrivate }

    public sealed class PromptLayerContract
    {
        public PromptLayerContract(
            string key,
            string phase,
            PromptContractRoleScope role,
            PromptContractLayer layer,
            PromptContractAuthority authority,
            PromptContractKnowledge knowledge,
            bool hard)
        {
            Key = key;
            Phase = phase;
            RoleScope = role;
            Layer = layer;
            Authority = authority;
            Knowledge = knowledge;
            HardAuthority = hard;
        }

        public string Key { get; }
        public string Phase { get; }
        public PromptContractRoleScope RoleScope { get; }
        public PromptContractLayer Layer { get; }
        public PromptContractAuthority Authority { get; }
        public PromptContractKnowledge Knowledge { get; }
        public bool HardAuthority { get; }
    }

    /// <summary>
    /// Runtime-active keys derived from concrete catalog lookups and annotated builders,
    /// independently of the ownership registry.
    /// </summary>
    public static class PromptRuntimeKeyInventory
    {
        private static readonly string[] BuilderOnlyFixedKeys = Keys(
            "game_master_prompt player_avatar_role_description datee_role_description " +
            "player-profile datee-profile datee-prompt texting_style_runtime_framing " +
            "steering_prompt horniness_prompt success_improvement_prompt_template " +
            "overlay_prompt_templates.horniness_overlay.system " +
            "overlay_prompt_templates.horniness_overlay.user " +
            "overlay_prompt_templates.horniness_overlay.user_with_archetype " +
            "overlay_prompt_templates.trap_overlay.system " +
            "overlay_prompt_templates.trap_overlay.user " +
            "overlay_prompt_templates.trap_overlay.user_with_archetype " +
            "overlay_prompt_templates.failure_corruption.system " +
            "overlay_prompt_templates.failure_corruption.user " +
            "overlay_prompt_templates.failure_corruption.user_with_archetype " +
            "overlay_prompt_templates.shadow_corruption.system " +
            "overlay_prompt_templates.shadow_corruption.user " +
            "overlay_prompt_templates.shadow_corruption.user_with_archetype " +
            "active-archetype-directive");

        private static readonly string[] SuccessTierKeys =
        {
            "clean", "strong", "critical", "exceptional", "nat20",
        };

        public static IReadOnlyCollection<string> ActiveKeys
        {
            get
            {
                var keys = new List<string>(PromptCatalog.RuntimeActiveKeys);
                keys.AddRange(BuilderOnlyFixedKeys);
                foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                {
                    foreach (string tier in SuccessTierKeys)
                    {
                        keys.Add("delivery_instructions." + StatKey(stat) + "." + tier);
                    }
                }
                return keys.Distinct(StringComparer.Ordinal).ToArray();
            }
        }

        internal static IEnumerable<string> DeliveryInstructionKeys()
            => ActiveKeys.Where(key => key.StartsWith("delivery_instructions.", StringComparison.Ordinal));

        private static string StatKey(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm: return "charm";
                case StatType.Rizz: return "rizz";
                case StatType.Honesty: return "honesty";
                case StatType.Chaos: return "chaos";
                case StatType.Wit: return "wit";
                case StatType.SelfAwareness: return "sa";
                default: throw new InvalidOperationException("Unsupported stat '" + stat + "'.");
            }
        }

        private static string[] Keys(string value)
            => value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public sealed class PromptContractRegistry
    {
        private readonly IReadOnlyDictionary<string, PromptLayerContract> _contracts;

        public PromptContractRegistry(IEnumerable<PromptLayerContract> contracts)
        {
            if (contracts == null) throw new ArgumentNullException(nameof(contracts));
            var map = new Dictionary<string, PromptLayerContract>(StringComparer.Ordinal);
            foreach (PromptLayerContract contract in contracts)
            {
                if (map.ContainsKey(contract.Key))
                    throw new ArgumentException("Duplicate prompt contract key: " + contract.Key, nameof(contracts));
                map.Add(contract.Key, contract);
            }
            _contracts = map;
        }

        public bool TryGet(string key, out PromptLayerContract contract)
            => _contracts.TryGetValue(key, out contract!);

        public IEnumerable<PromptLayerContract> Entries => _contracts.Values;

        public void ValidateCompleteness(PromptCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var active = new HashSet<string>(PromptRuntimeKeyInventory.ActiveKeys, StringComparer.Ordinal);
            foreach (string key in active.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!_contracts.ContainsKey(key))
                    throw RegistryFailure("prompt_contract.registry.missing", catalog, key);
            }
            foreach (string key in _contracts.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!active.Contains(key))
                    throw RegistryFailure("prompt_contract.registry.obsolete", catalog, key);
            }
        }

        public static PromptContractRegistry CreateDefault()
        {
            var contracts = new List<PromptLayerContract>();
            Add(contracts, SynthesisBehaviorKeys, LlmPhase.Synthesis, PromptContractRoleScope.RoleNeutral,
                PromptContractLayer.IdentityPersonality, PromptContractAuthority.Behavior, PromptContractKnowledge.None, true);
            Add(contracts, SynthesisBackstoryKeys, LlmPhase.Synthesis, PromptContractRoleScope.RoleNeutral,
                PromptContractLayer.Backstory, PromptContractAuthority.Behavior, PromptContractKnowledge.None, false);
            Add(contracts, PlanKeys, LlmPhase.OpponentResponse, PromptContractRoleScope.Datee,
                PromptContractLayer.ResponsePlan, PromptContractAuthority.CurrentMove, PromptContractKnowledge.SameCharacterPrivate, true);
            Add(contracts, DateeGameplayKeys, "any", PromptContractRoleScope.Datee,
                PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior, PromptContractKnowledge.PublicVisibleConversation, false);
            Add(contracts, SharedGameplayKeys, "any", PromptContractRoleScope.SharedEngine,
                PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior, PromptContractKnowledge.None, false);
            Add(contracts, OutputKeys, "any", PromptContractRoleScope.SharedEngine,
                PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, true);

            var emotional = new List<string>(EmotionalFixedKeys);
            foreach (InterestState state in Enum.GetValues(typeof(InterestState)))
                emotional.Add(EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(state));
            foreach (string transition in EmotionalReactionPromptCatalog.RelationshipTransitionKeys)
                emotional.Add(EmotionalReactionPromptCatalog.GetRelationshipTransitionInstructionKey(transition));
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                foreach (string outcome in EmotionalReactionPromptCatalog.OutcomeKeys)
                    emotional.Add(EmotionalReactionPromptCatalog.GetEventMeaningKey(stat, outcome));
            Add(contracts, emotional.Distinct(StringComparer.Ordinal), "any", PromptContractRoleScope.SharedEngine,
                PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior, PromptContractKnowledge.SameCharacterPrivate, false);

            contracts.Add(Contract("texting_style_runtime_framing", "any", PromptContractRoleScope.SharedEngine,
                PromptContractLayer.TextingStyle, PromptContractAuthority.SurfaceStyle, PromptContractKnowledge.None, false));
            contracts.Add(Contract("game_master_prompt", "any", PromptContractRoleScope.SharedEngine,
                PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior, PromptContractKnowledge.None, false));
            contracts.Add(Contract("datee_role_description", "any", PromptContractRoleScope.Datee,
                PromptContractLayer.IdentityPersonality, PromptContractAuthority.Behavior, PromptContractKnowledge.PublicVisibleConversation, false));
            contracts.Add(Contract("player_avatar_role_description", "any", PromptContractRoleScope.PlayerAvatar,
                PromptContractLayer.IdentityPersonality, PromptContractAuthority.Behavior, PromptContractKnowledge.PublicVisibleConversation, false));
            contracts.Add(Contract("datee-profile", "any", PromptContractRoleScope.Datee,
                PromptContractLayer.IdentityPersonality, PromptContractAuthority.Behavior, PromptContractKnowledge.SameCharacterPrivate, false));
            contracts.Add(Contract("player-profile", "any", PromptContractRoleScope.PlayerAvatar,
                PromptContractLayer.IdentityPersonality, PromptContractAuthority.Behavior, PromptContractKnowledge.SameCharacterPrivate, false));
            contracts.Add(Contract("datee-prompt", "any", PromptContractRoleScope.SharedEngine,
                PromptContractLayer.IdentityPersonality, PromptContractAuthority.Behavior, PromptContractKnowledge.PublicVisibleConversation, false));
            contracts.Add(Contract("active-archetype-directive", "any", PromptContractRoleScope.SharedEngine,
                PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior, PromptContractKnowledge.SameCharacterPrivate, false));

            Add(contracts, Keys("steering_prompt"), LlmPhase.Steering, PromptContractRoleScope.PlayerAvatar,
                PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior, PromptContractKnowledge.PublicVisibleConversation, false);
            Add(contracts, Keys("horniness_prompt"), LlmPhase.HorninessOverlay, PromptContractRoleScope.PlayerAvatar,
                PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior, PromptContractKnowledge.PublicVisibleConversation, false);
            Add(contracts, Keys("success_improvement_prompt_template"), LlmPhase.Delivery, PromptContractRoleScope.PlayerAvatar,
                PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior, PromptContractKnowledge.PublicVisibleConversation, false);
            Add(contracts, PromptRuntimeKeyInventory.DeliveryInstructionKeys(), LlmPhase.Delivery, PromptContractRoleScope.PlayerAvatar,
                PromptContractLayer.PerformanceRule, PromptContractAuthority.Behavior, PromptContractKnowledge.PublicVisibleConversation, false);
            AddOverlayContracts(contracts, "horniness_overlay", LlmPhase.HorninessOverlay);
            AddOverlayContracts(contracts, "trap_overlay", LlmPhase.TrapOverlay);
            AddOverlayContracts(contracts, "failure_corruption", LlmPhase.Delivery);
            AddOverlayContracts(contracts, "shadow_corruption", LlmPhase.ShadowCorruption);

            return new PromptContractRegistry(contracts);
        }

        private static void AddOverlayContracts(List<PromptLayerContract> contracts, string overlay, string phase)
        {
            Add(
                contracts,
                Keys(
                    "overlay_prompt_templates." + overlay + ".system " +
                    "overlay_prompt_templates." + overlay + ".user " +
                    "overlay_prompt_templates." + overlay + ".user_with_archetype"),
                phase,
                PromptContractRoleScope.PlayerAvatar,
                PromptContractLayer.PerformanceRule,
                PromptContractAuthority.Behavior,
                PromptContractKnowledge.PublicVisibleConversation,
                false);
        }

        private static PromptLayerContract Contract(
            string key,
            string phase,
            PromptContractRoleScope role,
            PromptContractLayer layer,
            PromptContractAuthority authority,
            PromptContractKnowledge knowledge,
            bool hard)
            => new PromptLayerContract(key, phase, role, layer, authority, knowledge, hard);

        private static void Add(
            List<PromptLayerContract> target,
            IEnumerable<string> keys,
            string phase,
            PromptContractRoleScope role,
            PromptContractLayer layer,
            PromptContractAuthority authority,
            PromptContractKnowledge knowledge,
            bool hard)
        {
            foreach (string key in keys)
                target.Add(Contract(key, phase, role, layer, authority, knowledge, hard));
        }

        private static string[] Keys(string value)
            => value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private static PromptLayerContractException RegistryFailure(string code, PromptCatalog catalog, string key)
        {
            PromptEntry? entry = catalog.TryGet(key);
            return new PromptLayerContractException(
                code,
                null,
                PromptContractRoleScope.RoleNeutral,
                key,
                "system_prompt",
                entry?.SourceFile,
                entry?.SourceLine.HasValue == true ? "line:" + entry.SourceLine.Value : null,
                null);
        }

        private static readonly string[] SynthesisBehaviorKeys = Keys(
            "personality_consolidation personality-consolidation-repair-surface-style diagnosis " +
            "diagnosis-repair-json diagnosis-repair-field character_generate");
        private static readonly string[] SynthesisBackstoryKeys = Keys(
            "backstory backstory_consolidation bio stake dramatic_arc outfit");
        private static readonly string[] PlanKeys = Keys(
            "datee-response-plan-performance datee-response-plan-reconciliation");
        private static readonly string[] OutputKeys = Keys(
            "dialogue-options-structured-json-instruction");
        private static readonly string[] DateeGameplayKeys = Keys(
            "datee-response-instruction datee-transition-directive datee-shadow-state-heading " +
            "datee-reaction-fumble datee-reaction-misfire datee-reaction-trope-trap " +
            "datee-reaction-catastrophe datee-reaction-legendary " +
            "datee-horniness-reaction-below-threshold datee-horniness-reaction-high-interest " +
            "datee-horniness-tier-intensity-fumble datee-horniness-tier-intensity-misfire " +
            "datee-horniness-tier-intensity-trope-trap datee-horniness-tier-intensity-catastrophe " +
            "interest-narrative-unmatched interest-narrative-bored interest-narrative-lukewarm " +
            "interest-narrative-interested interest-narrative-very-into-it " +
            "interest-narrative-almost-there interest-narrative-date-secured " +
            "resistance-unmatched resistance-bored resistance-lukewarm resistance-interested " +
            "resistance-very-into-it resistance-almost-there resistance-date-secured engine-datee-block");
        private static readonly string[] SharedGameplayKeys = Keys(
            "dialogue-options-instruction interest-beat-instruction interest-beat-above15 " +
            "interest-beat-below8 interest-beat-date-secured interest-beat-unmatched interest-beat-generic " +
            "pivot-directive cold-opener-rule stake-coverage-summary stake-coverage-untouched-directive " +
            "stake-coverage-all-referenced-directive player-transition-directive cognitive-subtext-directive " +
            "stateful-previous-context-heading stateful-current-turn-heading engine-state-hfi-line engine-state-tor-line " +
            "engine-state-cognitive-subtext-line engine-state-transition-target-line engine-state-transition-style-line " +
            "conversation-history-heading conversation-history-empty shadow-state-heading shadow-taint-madness " +
            "shadow-taint-despair shadow-taint-denial shadow-taint-fixation shadow-taint-dread shadow-taint-overthinking " +
            "engine-options-block");
        private static readonly string[] EmotionalFixedKeys = Keys(
            "emotional-reaction-compiled-wrapper emotional-reaction-compiled-session-wrapper " +
            "emotional-reaction-character-success-wrapper emotional-reaction-character-failure-wrapper " +
            "emotional-reaction-history-line emotional-reaction-history-empty emotional-reaction-performance-direction " +
            "emotional-reaction-director emotional-reaction-director-system-wrapper " +
            "emotional-reaction-previous-direction-line emotional-reaction-previous-direction-empty " +
            "datee-response-repetition-repair emotional-reaction-director-repair-contract " +
            "emotional-reaction-director-repair-drafted-chat-reply " +
            "emotional-reaction-director-repair-response-posture-omits-primary-emotion " +
            "emotional-reaction-director-repair-unsupported-primary-emotion character-emotional-primary-emotions " +
            "character-emotional-status-context character-emotional-status-unavailable character-emotional-hfi-low " +
            "character-emotional-hfi-high character-emotional-tor-low character-emotional-tor-high " +
            "avatar-emotional-director-system-wrapper avatar-emotional-director-input avatar-emotional-performance-direction");
    }
}
