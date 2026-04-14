using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Static catalog of all archetype definitions with their level ranges, tiers,
    /// and behavioral instruction text.
    ///
    /// Tier definitions (from archetypes-enriched.yaml):
    ///   Tier 1 — Low Level  (Levels 1–3)
    ///   Tier 2 — Early Game (Levels 2–6)
    ///   Tier 3 — Mid Game   (Levels 3–9)
    ///   Tier 4 — High Level (Levels 5+)
    ///
    /// Tiers overlap by design. A level-5 character qualifies for tiers 2, 3, and 4.
    /// Archetype selection filters by the character's eligible tiers and picks the
    /// highest-count archetype whose tier is in that eligible set.
    /// </summary>
    public static class ArchetypeCatalog
    {
        // ── Tier level boundaries ─────────────────────────────────────────────

        private const int Tier1Min = 1;  private const int Tier1Max = 3;
        private const int Tier2Min = 2;  private const int Tier2Max = 6;
        private const int Tier3Min = 3;  private const int Tier3Max = 9;
        private const int Tier4Min = 5;  // no upper bound

        // ── Archetype registry ────────────────────────────────────────────────

        private static readonly Dictionary<string, ArchetypeDefinition> _byName;

        static ArchetypeCatalog()
        {
            // (name, minLevel, maxLevel, tier)
            // Level ranges are kept from archetypes-enriched.yaml for backward
            // compatibility; tier drives dominant-archetype filtering.
            var defs = new[]
            {
                new ArchetypeDefinition("The Hey Opener",          1,  3,  1),
                new ArchetypeDefinition("The DTF Opener",          1,  5,  1),
                new ArchetypeDefinition("The One-Word Replier",    1,  5,  1),
                new ArchetypeDefinition("The Wall of Text",        1,  5,  2),
                new ArchetypeDefinition("The Copy-Paste Machine",  2,  5,  2),
                new ArchetypeDefinition("The Pickup Line Spammer", 1,  6,  2),
                new ArchetypeDefinition("The Exploding Nice Guy",  1,  6,  2),
                new ArchetypeDefinition("The Oversharer",          2,  7,  2),
                new ArchetypeDefinition("The Philosopher",         2,  7,  2),
                new ArchetypeDefinition("The Instagram Recruiter", 2,  6,  2),
                new ArchetypeDefinition("The Bot / Scammer",       1,  4,  3),
                new ArchetypeDefinition("The Zombie",              3,  8,  3),
                new ArchetypeDefinition("The Breadcrumber",        4,  9,  3),
                new ArchetypeDefinition("The Love Bomber",         3,  9,  3),
                new ArchetypeDefinition("The Peacock",             3,  8,  3),
                new ArchetypeDefinition("The Slow Fader",          2,  8,  4),
                new ArchetypeDefinition("The Ghost",               1, 10,  4),
                new ArchetypeDefinition("The Player",              5, 10,  4),
                new ArchetypeDefinition("The Sniper",              5, 11,  4),
                new ArchetypeDefinition("The Bio Responder",       4, 11,  4),
            };

            _byName = new Dictionary<string, ArchetypeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in defs)
                _byName[d.Name] = d;

            // ── Register behavior text ────────────────────────────────────────
            // Sourced from rules/extracted/archetypes-enriched.yaml §2 archetype
            // definitions. These are injected into LLM prompts via
            // ActiveArchetype.Directive.

            _behaviors["The Hey Opener"] =
                "Can barely form a sentence. Every message is \"hey\" or \"hi.\" Not malicious — just " +
                "paralysed by fear of rejection and genuinely has no idea what else to say. Believes that " +
                "opening a channel is the hard part. It is not. The digital equivalent of standing in a doorway.\n\n" +
                "*Sample lines:* hey · hi · heyy · sup · what's up";

            _behaviors["The DTF Opener"] =
                "Skips all pretence. Opens with explicitly sexual messages. Expects enthusiasm. Gets blocked. " +
                "Does it again. The internal logic: I want this, therefore saying so clearly is efficient. " +
                "The self-awareness to understand that directness about desire is different from consent to be " +
                "desired is simply absent.\n\n" +
                "*Sample lines:* \"you up?\" · \"wanna come over\" · \"let's skip the small talk\"";

            _behaviors["The One-Word Replier"] =
                "Responds to every thoughtful message with \"haha\", \"lol\", \"yeah\", \"cool\", \"nice.\" " +
                "The human embodiment of read-but-not-engaged energy. Often they're overthinking every response " +
                "and producing nothing as a result. A conversation black hole from which no momentum escapes.\n\n" +
                "*Sample lines:* haha · lol · yeah · cool · nice";

            _behaviors["The Wall of Text"] =
                "The overcorrection from the Hey Opener. First message is a 300-word paragraph covering their " +
                "entire personality, life context, and opening question. Well-intentioned. Overwhelming. Usually " +
                "written in a single anxious sitting and sent before they can reconsider. Signals the same " +
                "anxiety as \"hey\" — just a different expression of it.";

            _behaviors["The Copy-Paste Machine"] =
                "Has a message template. Sends it to everyone. Occasionally swaps in a name or a fake-specific " +
                "detail. Women can always tell. The tell is that nothing in the message actually references " +
                "anything real about their profile. Volume strategy. The human equivalent of a mail merge.";

            _behaviors["The Pickup Line Spammer"] =
                "Opens with a memorised, usually terrible canned pickup line. Knows it's bad. Hopes the " +
                "audacity makes it charming. The fixation on the tactic means they repeat the same approach " +
                "regardless of result. Sometimes delivers a genuinely good line and ruins it with a follow-up.";

            _behaviors["The Exploding Nice Guy"] =
                "Polite and friendly right up until the moment you don't respond fast enough or indicate " +
                "disinterest. Then: immediate pivot to hostility, insults, or passive-aggressive withdrawal. " +
                "The charm was never real — it was transactional. Feels genuinely entitled to a response " +
                "because he was \"nice.\"";

            _behaviors["The Oversharer"] =
                "Trauma dumps in message 3. Shares divorce details, childhood wounds, therapy notes, and the " +
                "full arc of their last relationship before you've asked their last name. Genuinely open and " +
                "well-meaning. Just doesn't have the self-awareness to read the room. The information isn't " +
                "the problem — the timing is everything.";

            _behaviors["The Philosopher"] =
                "Opens with \"what do you think the meaning of life is?\" or a would-you-rather about " +
                "horse-duck size ratios. Intellectualising as a defence mechanism — if the conversation stays " +
                "in the realm of ideas, rejection feels less personal. Uses philosophy as both genuine " +
                "engagement and armour.";

            _behaviors["The Instagram Recruiter"] =
                "Real person. Not here for a date. Here to grow their following. Bio often says \"not here " +
                "much, find me on IG.\" Uses Tinder as free advertising for their OnlyFans, fitness coaching " +
                "brand, or personal brand.";

            _behaviors["The Bot / Scammer"] =
                "Fake profile. Designed to redirect to external platforms, grow an OnlyFans, run crypto scams, " +
                "or steal attention. Responds vaguely to direct questions. Pivots off-platform suspiciously " +
                "fast. The \"too-perfect\" photos are a tell. So is the \"I'm from Cyprus\" thing.";

            _behaviors["The Zombie"] =
                "The Ghost who came back. Disappeared for weeks or months. Resurfaces as if nothing happened. " +
                "High denial means they've genuinely rewritten the absence in their own head. Expects to pick " +
                "up exactly where they left off. Confused when this doesn't work. Usually the most charming " +
                "archetype right up until the moment they ghost again.\n\n" +
                "*Sample lines:* \"hey stranger 👋\" · \"omg I was literally just thinking about you\" · " +
                "\"sorry I disappeared, things got crazy\"";

            _behaviors["The Breadcrumber"] =
                "Sends just enough to keep interest alive without ever committing to anything real. Sporadic " +
                "likes. Random 3am replies. A meme with no context. Never progresses toward an actual date. " +
                "An expert at \"almost.\" Not cruel — just optimising for options without being honest about it.";

            _behaviors["The Love Bomber"] =
                "Overwhelming affection, attention, and declarations on the fastest possible timeline. \"I feel " +
                "like I've known you forever\" after two days. Future-faking at scale. May be genuine anxiety " +
                "presenting as intensity. May be a control tactic. Can't read that this is alarming rather " +
                "than romantic.";

            _behaviors["The Peacock"] =
                "Uses the opening message to establish status. \"As a doctor/lawyer/investor...\" The charm " +
                "and rizz stats are real — these profiles are often well-constructed. The problem is they're " +
                "driven by the dread of not being enough, expressed as evidence that they are.";

            _behaviors["The Slow Fader"] =
                "Never fully ghosts. Just responds less and less frequently. Daily → every 2 days → weekly → " +
                "silence. No formal ending. The decent human impulse to not hurt anyone combined with the " +
                "cowardly impulse to not say the thing. Leaves everyone in ambiguous purgatory.";

            _behaviors["The Ghost"] =
                "Matches. Maybe chats a bit. Then disappears without explanation. Can ghost at any stage: the " +
                "match itself, after three messages, after a week of good conversation, after planning a date. " +
                "High self-awareness means they know when something isn't working. Low honesty means they " +
                "can't say it. The most common archetype at every level.";

            _behaviors["The Player"] =
                "Keeps multiple conversations running simultaneously. Deliberately vague about intentions. Not " +
                "necessarily malicious — just maximising options in a system designed for maximising options. " +
                "The denial about what they're doing means they can't be honest about it, which crosses " +
                "into deception.";

            _behaviors["The Sniper"] =
                "Doesn't waste swipes. Selective. Considered. When they message, it's deliberate and specific. " +
                "Every message is a risk they've calculated, which means they can freeze on the edge of a send. " +
                "The counterpart to the Copy-Paste Machine in every way.\n\n" +
                "*Sample lines:* \"I've been on here for three months and I've matched with seventeen people. " +
                "I swiped on you specifically because of [reason].\"";

            _behaviors["The Bio Responder"] =
                "The rarest and most appreciated archetype. Actually reads the bio. References something " +
                "specific. Asks a thoughtful question. The ideal player type — every mechanic in the game is " +
                "designed to reward this behaviour. Women in every study and thread say this is all they want. " +
                "Why is it so rare? Because it requires showing up for real.";
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the archetype definition for the given name, or null if not found.
        /// Name comparison is case-insensitive.
        /// </summary>
        public static ArchetypeDefinition? GetByName(string name)
        {
            _byName.TryGetValue(name, out var def);
            return def;
        }

        /// <summary>
        /// Returns all known archetype definitions.
        /// </summary>
        public static IReadOnlyCollection<ArchetypeDefinition> All => _byName.Values;

        /// <summary>
        /// Returns the set of tiers (1–4) that a character qualifies for at the given level.
        /// Tiers overlap by design:
        ///   Tier 1: levels 1–3
        ///   Tier 2: levels 2–6
        ///   Tier 3: levels 3–9
        ///   Tier 4: levels 5+
        /// </summary>
        public static IReadOnlyList<int> GetCharacterTiers(int characterLevel)
        {
            var tiers = new List<int>(4);
            if (characterLevel >= Tier1Min && characterLevel <= Tier1Max) tiers.Add(1);
            if (characterLevel >= Tier2Min && characterLevel <= Tier2Max) tiers.Add(2);
            if (characterLevel >= Tier3Min && characterLevel <= Tier3Max) tiers.Add(3);
            if (characterLevel >= Tier4Min)                               tiers.Add(4);
            return tiers.AsReadOnly();
        }

        /// <summary>
        /// Returns true if the given archetype name is eligible for a character
        /// at <paramref name="characterLevel"/>, using tier-based filtering.
        ///
        /// An archetype is eligible when its tier is in the character's eligible
        /// tier set (see <see cref="GetCharacterTiers"/>).
        ///
        /// Unknown archetypes (not in catalog) are always considered eligible.
        /// When characterLevel is 0, no filtering is applied (backward-compatible).
        /// </summary>
        public static bool IsEligibleAtLevel(string archetypeName, int characterLevel)
        {
            if (characterLevel <= 0) return true; // no filtering when level unset

            var def = GetByName(archetypeName);
            if (def == null) return true; // unknown archetypes are not filtered

            var characterTiers = GetCharacterTiers(characterLevel);
            foreach (int t in characterTiers)
                if (t == def.Tier) return true;

            return false;
        }

        /// <summary>
        /// Returns the behavioral instruction for the given archetype. Returns a
        /// placeholder if no behavior text is registered.
        /// </summary>
        public static string GetBehavior(string archetypeName)
        {
            if (_behaviors.TryGetValue(archetypeName, out var behavior))
                return behavior;
            return $"Follow {archetypeName} behavioral pattern.";
        }

        /// <summary>
        /// Register behavior text for an archetype (e.g. loaded from YAML at runtime).
        /// Overwrites any existing registration for the same name.
        /// </summary>
        public static void RegisterBehavior(string archetypeName, string behavior)
        {
            if (!string.IsNullOrEmpty(archetypeName) && !string.IsNullOrEmpty(behavior))
                _behaviors[archetypeName] = behavior;
        }

        private static readonly Dictionary<string, string> _behaviors
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
