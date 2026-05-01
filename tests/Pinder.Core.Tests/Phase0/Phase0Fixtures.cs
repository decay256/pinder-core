using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// Shared fixture helpers for Phase 0 regression tests (#787).
    ///
    /// Built on top of the existing test helpers in <c>GameSessionTests.cs</c>
    /// (<see cref="TestHelpers"/>, <see cref="FixedDice"/>, <see cref="NullTrapRegistry"/>),
    /// so we don't duplicate basic profile/clock construction.
    /// </summary>
    internal static class Phase0Fixtures
    {
        /// <summary>
        /// Constructs a low-noise CharacterProfile with the given name.
        /// stats default to 2 across the board (matches <see cref="TestHelpers.MakeStatBlock"/>).
        /// </summary>
        public static CharacterProfile MakeProfile(string name, int allStats = 2, int level = 1)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: level);
        }

        /// <summary>
        /// Builds a vanilla <see cref="GameSessionConfig"/> with seeded steering RNG
        /// AND seeded stat-draw RNG so every random source the engine consults during
        /// a turn is deterministic. Steering covers <c>SteeringEngine</c> /
        /// <c>HorninessEngine</c> rolls; stat-draw covers
        /// <c>OptionFilterEngine.DrawRandomStats</c> shuffling (per #130).
        /// </summary>
        public static GameSessionConfig MakeConfig(int steeringSeed = 42, int statDrawSeed = 4242)
        {
            return new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: new Random(steeringSeed),
                statDrawRng: new Random(statDrawSeed));
        }

        /// <summary>
        /// Standard parseable dialogue-options canned LLM response. Produces 3 options
        /// (Charm / Wit / Honesty) that the existing
        /// <c>DialogueOptionParsers.ParseDialogueOptionsText</c> consumes cleanly.
        /// </summary>
        public const string CannedDialogueOptions =
            "OPTION_1\n[STAT: Charm]\n\"Hey, you come here often?\"\n\n" +
            "OPTION_2\n[STAT: Wit]\n\"Did you know penguins propose with pebbles?\"\n\n" +
            "OPTION_3\n[STAT: Honesty]\n\"I have to be real with you.\"\n";

        /// <summary>Canned delivery: echoes a flat string. The engine uses it verbatim.</summary>
        public const string CannedDelivery = "Hey, you come here often?";

        /// <summary>
        /// Canned opponent response. Bare message with no signals — the parser
        /// returns this as the opponent message text.
        /// </summary>
        public const string CannedOpponent = "Maybe.";

        /// <summary>
        /// Build a <see cref="PinderLlmAdapter"/> wired to the given transport, with the
        /// smallest viable options. Accepts any <see cref="ILlmTransport"/> so failure-mode
        /// transports (throwing, slow, etc.) can be injected too.
        /// </summary>
        public static PinderLlmAdapter MakeAdapter(ILlmTransport transport)
        {
            return new PinderLlmAdapter(transport, new PinderLlmAdapterOptions
            {
                MaxTokens = 256,
                Temperature = 0.9,
            });
        }
    }
}
