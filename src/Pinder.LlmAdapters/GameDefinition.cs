using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Configurable rules for how successful deliveries are written at each margin tier.
    /// </summary>
    public sealed class DeliveryRules
    {
        public string Clean { get; }
        public string Strong { get; }
        public string Critical { get; }
        public string Exceptional { get; }
        public string Test { get; }
        public string RegisterInstruction { get; }
        public string MediumRule { get; }

        public DeliveryRules(string clean, string strong, string critical, string exceptional,
            string test, string registerInstruction, string mediumRule)
        {
            Clean = clean ?? "";
            Strong = strong ?? "";
            Critical = critical ?? "";
            Exceptional = exceptional ?? "";
            Test = test ?? "";
            RegisterInstruction = registerInstruction ?? "";
            MediumRule = mediumRule ?? "";
        }
    }

    /// <summary>
    /// Configurable dramatic craft rules governing emotional investment, tension, and payoff.
    /// </summary>
    public sealed class DramaticCraft
    {
        public string Goal { get; }
        public string OpponentWant { get; }
        public string RevelationBudget { get; }
        public string DirectnessDial { get; }
        public string FailureCost { get; }
        public string EarningTheClose { get; }

        public DramaticCraft(string goal, string opponentWant, string revelationBudget,
            string directnessDial, string failureCost, string earningTheClose)
        {
            Goal = goal ?? "";
            OpponentWant = opponentWant ?? "";
            RevelationBudget = revelationBudget ?? "";
            DirectnessDial = directnessDial ?? "";
            FailureCost = failureCost ?? "";
            EarningTheClose = earningTheClose ?? "";
        }

        public string BuildSection()
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(Goal)) { sb.AppendLine("DRAMATIC GOAL"); sb.AppendLine(Goal.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(OpponentWant)) { sb.AppendLine("OPPONENT'S WANT"); sb.AppendLine(OpponentWant.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(RevelationBudget)) { sb.AppendLine("REVELATION BUDGET"); sb.AppendLine(RevelationBudget.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(DirectnessDial)) { sb.AppendLine("DIRECTNESS CALIBRATION"); sb.AppendLine(DirectnessDial.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(FailureCost)) { sb.AppendLine("FAILURE COST"); sb.AppendLine(FailureCost.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(EarningTheClose)) { sb.AppendLine("EARNING THE CLOSE"); sb.AppendLine(EarningTheClose.TrimEnd()); }
            return sb.ToString().TrimEnd();
        }
    }

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

        /// <summary>Additional world rules about texting psychology.</summary>
        public string TextingPsychology { get; }

        /// <summary>Show-don't-tell writing principle for character revelation.</summary>
        public string RevelationOverStatement { get; }

        /// <summary>Opponent friction / resistance framing.</summary>
        public string OpponentFriction { get; }

        /// <summary>Opponent curiosity / reciprocal questions direction.</summary>
        public string OpponentCuriosity { get; }

        /// <summary>Conversation arc / topic progression guidance.</summary>
        public string ConversationArcProgression { get; }

        /// <summary>Player options probing directive — biographical follow-up instruction.</summary>
        public string PlayerProbing { get; }

        /// <summary>Configurable delivery prompt rules, or null for hardcoded defaults.</summary>
        public DeliveryRules DeliveryRules { get; }

        /// <summary>Configurable dramatic craft rules, or null for hardcoded defaults.</summary>
        public DramaticCraft DramaticCraft { get; }

        public GameDefinition(
            string name,
            string vision,
            string worldDescription,
            string playerRoleDescription,
            string opponentRoleDescription,
            string metaContract,
            string writingRules,
            DeliveryRules deliveryRules = null,
            DramaticCraft dramaticCraft = null,
            string textingPsychology = null,
            string revelationOverStatement = null,
            string opponentFriction = null,
            string opponentCuriosity = null,
            string conversationArcProgression = null,
            string playerProbing = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Vision = vision ?? throw new ArgumentNullException(nameof(vision));
            WorldDescription = worldDescription ?? throw new ArgumentNullException(nameof(worldDescription));
            PlayerRoleDescription = playerRoleDescription ?? throw new ArgumentNullException(nameof(playerRoleDescription));
            OpponentRoleDescription = opponentRoleDescription ?? throw new ArgumentNullException(nameof(opponentRoleDescription));
            MetaContract = metaContract ?? throw new ArgumentNullException(nameof(metaContract));
            WritingRules = writingRules ?? throw new ArgumentNullException(nameof(writingRules));
            TextingPsychology = textingPsychology ?? "";
            RevelationOverStatement = revelationOverStatement ?? "";
            OpponentFriction = opponentFriction ?? "";
            OpponentCuriosity = opponentCuriosity ?? "";
            ConversationArcProgression = conversationArcProgression ?? "";
            PlayerProbing = playerProbing ?? "";
            DeliveryRules = deliveryRules;
            DramaticCraft = dramaticCraft;
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

            DeliveryRules deliveryRules = null;
            if (parsed.TryGetValue("delivery_rules", out var drObj) && drObj is Dictionary<object, object> drDict)
            {
                string DrGet(string key)
                {
                    if (drDict.TryGetValue(key, out var v) && v != null)
                        return v.ToString();
                    return "";
                }
                deliveryRules = new DeliveryRules(
                    clean: DrGet("clean"),
                    strong: DrGet("strong"),
                    critical: DrGet("critical"),
                    exceptional: DrGet("exceptional"),
                    test: DrGet("test"),
                    registerInstruction: DrGet("register_instruction"),
                    mediumRule: DrGet("medium_rule"));
            }

            DramaticCraft dramaticCraft = null;
            if (parsed.TryGetValue("dramatic_craft", out var dcObj) && dcObj is Dictionary<object, object> dcDict)
            {
                string DcGet(string key)
                {
                    if (dcDict.TryGetValue(key, out var v) && v != null)
                        return v.ToString();
                    return "";
                }
                dramaticCraft = new DramaticCraft(
                    goal: DcGet("goal"),
                    opponentWant: DcGet("opponent_want"),
                    revelationBudget: DcGet("revelation_budget"),
                    directnessDial: DcGet("directness_dial"),
                    failureCost: DcGet("failure_cost"),
                    earningTheClose: DcGet("earning_the_close"));
            }

            // Parse optional prose fields
            string GetOptional(string key)
            {
                if (parsed.TryGetValue(key, out var v) && v != null)
                    return v.ToString();
                return null;
            }
            // conversation_arc is a dict with a "progression" key
            string conversationArcProgression = null;
            if (parsed.TryGetValue("conversation_arc", out var caObj) && caObj is Dictionary<object, object> caDict)
            {
                if (caDict.TryGetValue("progression", out var caV) && caV != null)
                    conversationArcProgression = caV.ToString();
            }

            return new GameDefinition(
                name: GetRequired("name"),
                vision: GetRequired("vision"),
                worldDescription: GetRequired("world_description"),
                playerRoleDescription: GetRequired("player_role_description"),
                opponentRoleDescription: GetRequired("opponent_role_description"),
                metaContract: GetRequired("meta_contract"),
                writingRules: GetRequired("writing_rules"),
                deliveryRules: deliveryRules,
                dramaticCraft: dramaticCraft,
                textingPsychology: GetOptional("texting_psychology"),
                revelationOverStatement: GetOptional("revelation_over_statement"),
                opponentFriction: GetOptional("opponent_friction"),
                opponentCuriosity: GetOptional("opponent_curiosity"),
                conversationArcProgression: conversationArcProgression,
                playerProbing: GetOptional("player_probing")
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
            deliveryRules: new DeliveryRules(
                clean: "Deliver essentially as written. Small word choice improvements only.",
                strong: "Improve the phrasing, timing, or rhythm of what's already there.\n" +
                    "You may: rearrange for better flow, sharpen word choice, add ONE word or phrase that makes the existing sentiment more precise.\n" +
                    "You must not: add new sentences that introduce ideas not in the intended message, change the emotional register, or make the message say something the player didn't intend.",
                critical: "Deliver at peak. The message arrives perfectly. Something resonates.",
                exceptional: "This is the best version of this message that could exist. It arrives at exactly the right moment with exactly the right weight. The opponent feels it.",
                test: "The test: every idea in the delivered version should have a counterpart in the intended version. New additions should sharpen, not expand.",
                registerInstruction: "Stay in character. Match the texting register from the character profile above. Do not change the character's capitalization style.",
                mediumRule: "This is a text message on a phone screen, not a monologue. No internal stage directions, no narration of emotional state, no self-commentary mid-message."),
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
app at 1 AM.",
            dramaticCraft: new DramaticCraft(
                goal: @"Every conversation should produce emotional investment, tension, and payoff.
Not just a pleasant exchange — a story the player felt. The player should be
leaning in when they pick turn 7's option, not clicking on autopilot.

The three experiences we are building toward:
- INVESTMENT: The player cares what the opponent thinks. Not just about winning —
  about this specific person's response to this specific thing they said.
- TENSION: Something feels at stake. The player is not sure how the next message
  will land. They have been burned before. They are not comfortable.
- PAYOFF: The win or loss lands with weight. DateSecured should feel earned.
  Unmatched should sting. Neither should feel like a probability calculation resolving.",
                opponentWant: @"The opponent is not passive. They have something they want from this conversation.

At Interest 10-14: They want to find out if there is anything real here, or if
this is another performance. They are gathering evidence. Their questions are tests.
Their humor is a filter. Not hostile — efficient.

At Interest 15-20: They have found something interesting. Now they want to understand
what is underneath it. They are probing for the gap between who the player presents
themselves as and who they actually are. That gap is what they are attracted to —
not the performance, the moment the performance slips.

At Interest 21-24: They are close to deciding yes. Now they are testing whether
the thing they found is real or whether it will disappear under pressure. They
give more — and watch what the player does with it.

This want should drive responses. Not just reaction, but active pursuit.",
                revelationBudget: @"Each conversation has a budget of 2-3 moments where something real surfaces.
Not exposition — moments when behavioral evidence accumulates until the underlying
thing becomes visible.

Spend this budget at structurally meaningful points:
- One moment early-mid (turn 3-5): the first real thing surfaces, usually accidentally
- One moment at or after a failure: pressure creates revelation
- One moment near the close: what makes the win feel earned or the loss feel true

Between these moments, operate at the surface — subtext, deflection, humor, testing.
The real things should feel withheld until the moment they cannot be.",
                directnessDial: @"Characters operate at 2-4 on a 0-10 scale of directness.

0 = pure subtext (everything implied, nothing stated)
5 = occasional direct statement of feeling or intention
10 = characters explain their emotional states

Target: 2-3 for normal exchanges. 4-5 only at maximum pressure points. Never above 6.
A character who says 'I am nervous' is at 7. A character who sends a message that is
slightly too eager is at 2. The eagerness IS the nervousness.

For the opponent: at low interest, operate at 1-2 (near pure subtext). At high
interest, move toward 3-4 — say slightly more real things, but never lose the
withholding quality that makes them worth pursuing.",
                failureCost: @"When the player's message fails (Misfire, Trope Trap, Nat 1), the opponent does not
reset to neutral. They noticed. Whatever leaked through in the corrupted message
informs their emotional stance for the rest of the conversation.

A Misfire revealing backstory is not erased by the next successful message. The
opponent saw it. They are now watching for whether it comes back.

The opponent's response to a failure: reaction to what they saw, dilemma about
what it means, decision about how to proceed. This is how failures create dramatic
arcs rather than temporary interest dips.",
                earningTheClose: @"DateSecured should feel like something dissolved, not a threshold crossed.

A win that came too easily was not a win — it was a transaction. If the path had
friction, reversal, near-loss, and genuine earned moments — the close lands.

The opponent's final concession is not 'Interest hit 25.' It is the opponent
deciding this person earned it. The final message should reflect that something
real happened, not just that the meter filled.")
        );
    }
}
