using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.LlmAdapters
{
    public enum PromptContractRoleScope
    {
        RoleNeutral,
        SharedEngine,
        PlayerAvatar,
        Datee,
    }

    public enum PromptContractLayer
    {
        SystemLaw,
        OutputContract,
        StateFrame,
        IdentityPersonality,
        TextingStyle,
        BackstoryGrounding,
        DramaticSteering,
        EmotionalDirection,
        ResponsePlan,
        DynamicNarrative,
    }

    public enum PromptContractAuthority
    {
        SystemTonalLaw,
        OutputShape,
        StateSnapshot,
        BehaviorMotives,
        SurfaceStyle,
        BackstoryFacts,
        PacingRules,
        EmotionalTension,
        CurrentMove,
        ContextualReaction,
    }

    public enum PromptContractKnowledge
    {
        None,
        SharedPublic,
        SameCharacterPrivate,
        CounterpartPrivate,
    }

    public sealed class PromptLayerContract
    {
        public PromptLayerContract(
            string key,
            string phase,
            PromptContractRoleScope roleScope,
            PromptContractLayer layer,
            PromptContractAuthority authority,
            PromptContractKnowledge knowledge,
            bool hardAuthority)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Phase = phase ?? throw new ArgumentNullException(nameof(phase));
            RoleScope = roleScope;
            Layer = layer;
            Authority = authority;
            Knowledge = knowledge;
            HardAuthority = hardAuthority;
        }

        public string Key { get; }
        public string Phase { get; }
        public PromptContractRoleScope RoleScope { get; }
        public PromptContractLayer Layer { get; }
        public PromptContractAuthority Authority { get; }
        public PromptContractKnowledge Knowledge { get; }
        public bool HardAuthority { get; }
    }

    public sealed class PromptContractRegistry
    {
        private readonly IReadOnlyDictionary<string, PromptLayerContract> _contracts;

        public PromptContractRegistry(IEnumerable<PromptLayerContract> contracts)
        {
            if (contracts == null) throw new ArgumentNullException(nameof(contracts));
            var dict = new Dictionary<string, PromptLayerContract>(StringComparer.Ordinal);
            foreach (var c in contracts)
            {
                dict[c.Key] = c;
            }
            _contracts = dict;
        }

        public bool TryGet(string key, out PromptLayerContract? contract)
        {
            if (_contracts.TryGetValue(key, out var found))
            {
                contract = found;
                return true;
            }
            if (key.StartsWith("delivery_instructions.", StringComparison.Ordinal) && _contracts.TryGetValue("delivery_instructions", out var delivery))
            {
                contract = delivery;
                return true;
            }
            contract = null;
            return false;
        }

        public void ValidateCompleteness(PromptCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            foreach (string activeKey in PromptCatalog.RuntimeActiveKeys)
            {
                if (!_contracts.ContainsKey(activeKey))
                {
                    var entry = catalog.TryGet(activeKey);
                    throw new PromptLayerContractException(
                        "prompt_contract.registry.missing",
                        null,
                        PromptContractRoleScope.RoleNeutral,
                        activeKey,
                        "registered_contract",
                        entry?.SourceFile,
                        entry?.SourceLine.HasValue == true ? "line:" + entry.SourceLine.Value : null,
                        null);
                }
            }
        }

        public static PromptContractRegistry CreateDefault()
        {
            var list = new List<PromptLayerContract>();

            void Add(string key, string phase, PromptContractRoleScope roleScope, PromptContractLayer layer, PromptContractAuthority authority, PromptContractKnowledge knowledge, bool hardAuthority = false)
            {
                list.Add(new PromptLayerContract(key, phase, roleScope, layer, authority, knowledge, hardAuthority));
            }

            Add("The Bio Responder", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Bot / Scammer", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Breadcrumber", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Copy-Paste Machine", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The DTF Opener", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Exploding Nice Guy", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Ghost", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Hey Opener", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Instagram Recruiter", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Love Bomber", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The One-Word Replier", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Oversharer", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Peacock", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Philosopher", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Pickup Line Spammer", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Player", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Pun Troll", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Slow Fader", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Sniper", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Wall of Text", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("The Zombie", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("active-archetype-directive", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("avatar-emotional-director-input", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("avatar-emotional-director-system-wrapper", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("avatar-emotional-performance-direction", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("backstory", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.BackstoryGrounding, PromptContractAuthority.BackstoryFacts, PromptContractKnowledge.SameCharacterPrivate, false);
            Add("backstory_consolidation", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.BackstoryGrounding, PromptContractAuthority.BackstoryFacts, PromptContractKnowledge.SameCharacterPrivate, false);
            Add("bio", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("character-emotional-hfi-high", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("character-emotional-hfi-low", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("character-emotional-primary-emotions", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("character-emotional-status-context", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("character-emotional-status-unavailable", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("character-emotional-tor-high", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("character-emotional-tor-low", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("character_card_framing", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("character_data_framing", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("character_generate", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("cognitive-subtext-directive", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("cold-opener-rule", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DramaticSteering, PromptContractAuthority.PacingRules, PromptContractKnowledge.None, false);
            Add("conversation-history-empty", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("conversation-history-heading", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("datee-horniness-reaction-below-threshold", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-horniness-reaction-high-interest", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-horniness-tier-intensity-catastrophe", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-horniness-tier-intensity-fumble", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-horniness-tier-intensity-misfire", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-horniness-tier-intensity-trope-trap", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-prompt", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("datee-reaction-catastrophe", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-reaction-fumble", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-reaction-legendary", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-reaction-misfire", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-reaction-trope-trap", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-response-instruction", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-response-plan-performance", "any", PromptContractRoleScope.Datee, PromptContractLayer.ResponsePlan, PromptContractAuthority.CurrentMove, PromptContractKnowledge.SameCharacterPrivate, true);
            Add("datee-response-plan-reconciliation", "any", PromptContractRoleScope.Datee, PromptContractLayer.ResponsePlan, PromptContractAuthority.CurrentMove, PromptContractKnowledge.SameCharacterPrivate, false);
            Add("datee-response-repetition-repair", "any", PromptContractRoleScope.Datee, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("datee-shadow-state-heading", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("datee-transition-directive", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DramaticSteering, PromptContractAuthority.PacingRules, PromptContractKnowledge.None, false);
            Add("datee_role_description", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("default-clean", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("default-critical", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("default-exceptional", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("default-medium-rule", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("default-register-instruction", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("default-strong", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("default-test", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("delivery_instruction", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("delivery_instructions", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("diagnosis", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SameCharacterPrivate, false);
            Add("diagnosis-repair-field", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SameCharacterPrivate, false);
            Add("diagnosis-repair-json", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SameCharacterPrivate, false);
            Add("dialogue-options-instruction", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, false);
            Add("dialogue-options-structured-json-instruction", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, true);
            Add("dramatic_arc", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("emotional-reaction-character-failure-wrapper", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-character-success-wrapper", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-compiled-session-wrapper", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-compiled-wrapper", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-director", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-director-repair-contract", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-director-repair-drafted-chat-reply", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-director-repair-response-posture-omits-primary-emotion", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-director-repair-unsupported-primary-emotion", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-director-system-wrapper", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-catastrophe", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-clean", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-critical", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-exceptional", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-fumble", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-misfire", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-nat1", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-nat20", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-strong", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-chaos-trope_trap", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-catastrophe", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-clean", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-critical", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-exceptional", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-fumble", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-misfire", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-nat1", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-nat20", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-strong", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-charm-trope_trap", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-catastrophe", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-clean", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-critical", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-exceptional", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-fumble", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-misfire", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-nat1", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-nat20", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-strong", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-honesty-trope_trap", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-catastrophe", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-clean", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-critical", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-exceptional", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-fumble", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-misfire", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-nat1", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-nat20", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-strong", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-rizz-trope_trap", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-catastrophe", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-clean", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-critical", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-exceptional", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-fumble", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-misfire", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-nat1", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-nat20", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-strong", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-self-awareness-trope_trap", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-catastrophe", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-clean", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-critical", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-exceptional", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-fumble", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-misfire", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-nat1", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-nat20", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-strong", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-event-wit-trope_trap", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-history-empty", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-history-line", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-interest-almost-there", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-interest-bored", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-interest-date-secured", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-interest-interested", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-interest-lukewarm", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-interest-unmatched", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-interest-very-into-it", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-performance-direction", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-previous-direction-empty", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-previous-direction-line", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-transition-damaged", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-transition-preserved", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-transition-strengthened", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("emotional-reaction-transition-transformed", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.EmotionalDirection, PromptContractAuthority.EmotionalTension, PromptContractKnowledge.None, false);
            Add("engine-datee-block", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("engine-options-block", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("engine-state-cognitive-subtext-line", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("engine-state-hfi-line", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("engine-state-tor-line", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("engine-state-transition-style-line", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("engine-state-transition-target-line", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("game_master_prompt", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("horniness_prompt", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("interest-beat-above15", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-beat-below8", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-beat-date-secured", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-beat-generic", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-beat-instruction", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-beat-unmatched", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-narrative-almost-there", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-narrative-bored", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-narrative-date-secured", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-narrative-interested", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-narrative-lukewarm", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-narrative-unmatched", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("interest-narrative-very-into-it", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("outfit", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.SharedPublic, false);
            Add("overlay-model-comparison-brick-personality", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, false);
            Add("overlay-model-comparison-catastrophe-overlay", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, false);
            Add("overlay-model-comparison-delivery-system", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, false);
            Add("overlay-model-comparison-delivery-user", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, false);
            Add("overlay-model-comparison-game-context", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, false);
            Add("overlay-model-comparison-overlay-user", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, false);
            Add("overlay-model-comparison-strong-success-instruction", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, false);
            Add("personality-consolidation-repair-surface-style", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.None, false);
            Add("personality_consolidation", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.IdentityPersonality, PromptContractAuthority.BehaviorMotives, PromptContractKnowledge.None, false);
            Add("pivot-directive", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DramaticSteering, PromptContractAuthority.PacingRules, PromptContractKnowledge.None, false);
            Add("player-transition-directive", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DramaticSteering, PromptContractAuthority.PacingRules, PromptContractKnowledge.None, false);
            Add("player_avatar_role_description", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("player_role_description", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("resistance-almost-there", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("resistance-bored", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("resistance-date-secured", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("resistance-interested", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("resistance-lukewarm", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("resistance-unmatched", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("resistance-very-into-it", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("shadow-state-heading", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("shadow-taint-denial", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("shadow-taint-despair", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("shadow-taint-dread", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("shadow-taint-fixation", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("shadow-taint-madness", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("shadow-taint-overthinking", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.StateFrame, PromptContractAuthority.StateSnapshot, PromptContractKnowledge.None, false);
            Add("sim_agent", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_active_traps_none", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_failure_rules", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_icon_callback", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_icon_combo", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_icon_tell", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_icon_weakness", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_modifier_advantage", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_modifier_disadvantage", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_momentum_note_plus2", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_momentum_note_plus3", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_momentum_rules_threshold", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_option_ev_row", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_option_shadow_row", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_option_summary_row", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_option_text_row", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_personality_block", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_recent_history_block", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_recent_history_row", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_risk_tier_bold", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_risk_tier_hard", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_risk_tier_medium", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_risk_tier_safe", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_impact_danger", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_impact_honesty_benefit", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_impact_risk_t1", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_impact_risk_t2", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_impact_separator", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_status_row", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_status_unavailable", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_warning_approaching_t1", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_warning_t1", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_warning_t2", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_shadow_warning_t3", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("sim_agent_success_rules", "any", PromptContractRoleScope.PlayerAvatar, PromptContractLayer.DynamicNarrative, PromptContractAuthority.ContextualReaction, PromptContractKnowledge.None, false);
            Add("stake", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DramaticSteering, PromptContractAuthority.PacingRules, PromptContractKnowledge.None, false);
            Add("stake-coverage-all-referenced-directive", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DramaticSteering, PromptContractAuthority.PacingRules, PromptContractKnowledge.None, false);
            Add("stake-coverage-summary", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DramaticSteering, PromptContractAuthority.PacingRules, PromptContractKnowledge.None, false);
            Add("stake-coverage-untouched-directive", "any", PromptContractRoleScope.SharedEngine, PromptContractLayer.DramaticSteering, PromptContractAuthority.PacingRules, PromptContractKnowledge.None, false);
            Add("stateful-current-turn-heading", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("stateful-previous-context-heading", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("steering_prompt", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("success_improvement_prompt_template", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("texting_style_runtime_framing", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);
            Add("texting_style_soft_framing", "any", PromptContractRoleScope.RoleNeutral, PromptContractLayer.SystemLaw, PromptContractAuthority.SystemTonalLaw, PromptContractKnowledge.None, false);

            return new PromptContractRegistry(list);
        }
    }
}
