using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Data carrier for game-level creative direction.
    /// Parsed from YAML or provided via hardcoded defaults.
    /// </summary>
    public sealed class GameDefinition
    {
        /// <summary>Game name (e.g. "Pinder").</summary>
        public string Name { get; }

        /// <summary>Creative brief: what the game is, tone, goal.</summary>
        public string Vision { get; }

        /// <summary>World setting: texting psychology, medium rules.</summary>
        public string WorldDescription { get; }

        /// <summary>Player character role description.</summary>
        public string PlayerRoleDescription { get; }

        /// <summary>Opponent character role description.</summary>
        public string OpponentRoleDescription { get; }

        /// <summary>Immersion rules: never break character, [ENGINE] blocks.</summary>
        public string MetaContract { get; }

        /// <summary>Writing style rules: texting register, brevity, etc.</summary>
        public string WritingRules { get; }

        public GameDefinition(
            string name,
            string vision,
            string worldDescription,
            string playerRoleDescription,
            string opponentRoleDescription,
            string metaContract,
            string writingRules)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Vision = vision ?? throw new ArgumentNullException(nameof(vision));
            WorldDescription = worldDescription ?? throw new ArgumentNullException(nameof(worldDescription));
            PlayerRoleDescription = playerRoleDescription ?? throw new ArgumentNullException(nameof(playerRoleDescription));
            OpponentRoleDescription = opponentRoleDescription ?? throw new ArgumentNullException(nameof(opponentRoleDescription));
            MetaContract = metaContract ?? throw new ArgumentNullException(nameof(metaContract));
            WritingRules = writingRules ?? throw new ArgumentNullException(nameof(writingRules));
        }

        /// <summary>
        /// Parse a YAML string into a GameDefinition.
        /// Throws FormatException if YAML is invalid or missing required keys.
        /// Throws ArgumentNullException if yamlContent is null.
        /// </summary>
        public static GameDefinition LoadFrom(string yamlContent)
        {
            if (yamlContent == null)
                throw new ArgumentNullException(nameof(yamlContent));

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            Dictionary<string, object?>? parsed;
            try
            {
                parsed = deserializer.Deserialize<Dictionary<string, object?>>(yamlContent);
            }
            catch (Exception ex)
            {
                throw new FormatException("Failed to parse YAML content: " + ex.Message, ex);
            }

            if (parsed == null)
                throw new FormatException("YAML content did not parse to a dictionary.");

            string GetRequired(string key)
            {
                if (!parsed.TryGetValue(key, out var value))
                    throw new FormatException($"Missing required key: \"{key}\"");
                if (value == null)
                    throw new FormatException($"Key \"{key}\" has a null value.");
                return value.ToString()!;
            }

            return new GameDefinition(
                name: GetRequired("name"),
                vision: GetRequired("vision"),
                worldDescription: GetRequired("world_description"),
                playerRoleDescription: GetRequired("player_role_description"),
                opponentRoleDescription: GetRequired("opponent_role_description"),
                metaContract: GetRequired("meta_contract"),
                writingRules: GetRequired("writing_rules")
            );
        }

        /// <summary>
        /// Hardcoded Pinder defaults used when YAML file is unavailable.
        /// </summary>
        public static GameDefinition PinderDefaults { get; } = new GameDefinition(
            name: "Pinder",
            vision: @"Pinder is a comedy dating RPG where every character is a sentient penis
on a Tinder-like dating app. You dress up, build stats, and try to charm
other players' characters into going on a date — using dice rolls,
real-time stat checks, and an LLM that generates the actual conversation.

The tone is absurdist comedy with genuine emotional stakes underneath.
The comedy comes from taking the absurd premise completely seriously.
Characters never wink at the audience. They are real people (who happen
to be penises) in a real situation (trying to get a date) with real
feelings (that their shadow stats are slowly corrupting).

The mechanical identity is a d20 RPG: six positive stats paired with
six shadow stats that grow on their own and penalize their paired stat.
Shadows represent the psychological cost of prolonged app use — Madness,
Horniness, Denial, Fixation, Dread, Overthinking. You level up to fight
the darkness, but the darkness levels up too.",
            worldDescription: @"Characters are sentient penises who exist on a dating server. Each one
has been dressed up, given a personality through equipped items and
anatomy choices, and uploaded by their player.

Every character has 6 positive stats and 6 shadow stats that form
paired opposites: Charm/Madness, Rizz/Horniness, Honesty/Denial,
Chaos/Fixation, Wit/Dread, Self-Awareness/Overthinking.

Shadows start at 0 and grow from in-conversation events. Every 3 points
of shadow penalizes the paired positive stat by -1. At threshold 6 the
shadow taints dialogue. At 12 it imposes mechanical disadvantage. At 18+
it can override the character's will entirely.

Conversations are the core gameplay loop. Each turn, the player picks
from 4 dialogue options (each tied to a stat). A d20 is rolled against
the opponent's defence DC. Success raises the Interest meter; failure
drops it and risks activating traps.

The Interest meter runs from 0 to 25. At 25 (Date Secured) you win.
Characters think before sending — this is a texting medium that makes
people self-conscious.",
            playerRoleDescription: @"The player character is genuinely trying to get a date. Their goal is to
raise the Interest meter to 25 (Date Secured). Each turn, generate 4
dialogue options tied to the character's stats. Options must reflect the
player character's personality as assembled from their items, anatomy
fragments, and texting style.

Horniness mechanics can force Rizz options onto the menu. When combo or
callback opportunities exist, options should weave them in organically.",
            opponentRoleDescription: @"The opponent is another player's character being puppeted by the LLM.
Their personality prompt is the character bible — everything they say
must be consistent with their assembled identity.

Below Interest 25, the opponent is NOT won over. They are evaluating.
Resistance is proportional to their current interest state. The opponent
reacts to mechanical events they can perceive through the conversation:
failed messages land awkwardly, shadow taint shifts tone, and traps
create conversational disruptions the opponent responds to naturally.

The opponent has their own texting style that must remain distinct from
the player's at all times.",
            metaContract: @"Characters believe they are real people in a real situation. They cannot
see dice, DCs, stat modifiers, interest meters, shadow thresholds,
failure tiers, combo trackers, or any game mechanic. All of these exist
beneath the conversation, not inside it.

All game events manifest through HOW characters speak, not through
commentary. Never break character. The LLM is always in-world. There is
no narrator, no aside, no wink to the audience.

Never add ideas the player didn't choose. Success delivery improves
phrasing — it does not introduce new topics, jokes, or emotional content.
Never resolve the date before Interest reaches 25 mechanically.
Maintain two distinct character voices throughout the entire conversation.",
            writingRules: @"All dialogue is texting register. Short, informal, platform-appropriate.
Message length: typically 1-3 sentences for player options, 1-4 sentences
for opponent responses. Brevity is a feature.

Emoji: use only when the character's texting style fragment calls for it.
No asterisk actions (*walks over*). This is a text-based dating app.

Comedy comes from character voice, not narration. Strong rolls sharpen
phrasing — they do NOT add new ideas. Failed deliveries corrupt the
message proportional to the failure tier.

Subtext over text. Reveal through choices, not statements. Every message
should sound like something a real person would actually send on a dating
app at 1 AM."
        );
    }
}
