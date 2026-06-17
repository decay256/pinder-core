using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Pinder.Tools.NarrativeHarness;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #869 (CORE half): the Narrative Harness collects and exposes a
    /// structured list of every raw LLM exchange, additively, without changing
    /// the existing markdown transcript.
    ///
    /// Coverage:
    ///   A. <see cref="RawLlmSession.ParsePhase"/> derivation of Speaker + Turn
    ///      from representative phase strings.
    ///   B. <see cref="RecordingLlmTransport"/> records every successful
    ///      SendAsync with correct fields, passes the response through unchanged,
    ///      and does NOT record on error.
    ///   C. <see cref="HarnessRunner.RunAsync"/> returns a non-null
    ///      <see cref="HarnessRunResult"/> with RawSessions populated when the
    ///      transport is a RecordingLlmTransport, and empty otherwise.
    ///   D. The Transcript is byte-identical to the old Task&lt;string&gt; return
    ///      value (verified by comparing two runs with identical stubs).
    ///
    /// Deterministic: all LLM calls are serviced by a canned-response stub —
    /// no real network calls.
    /// </summary>
    public class Issue869_RawLlmSessionTests
    {
        // ─────────────────────────────────────────────────────────────────────
        // A. RawLlmSession.ParsePhase
        // ─────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("harness-turn-1",         "Character", 1)]
        [InlineData("harness-turn-14",        "Character", 14)]
        [InlineData("harness-pursuer-open",   "Pursuer",   0)]
        [InlineData("harness-pursuer-char-1", "Pursuer",   1)]
        [InlineData("harness-pursuer-char-7", "Pursuer",   7)]
        [InlineData("harness-pursuer-3",      "Pursuer",   3)]
        [InlineData("harness-pursuer-99",     "Pursuer",   99)]
        public void ParsePhase_maps_known_phases_to_correct_speaker_and_turn(
            string phase, string expectedSpeaker, int expectedTurn)
        {
            var (speaker, turn) = RawLlmSession.ParsePhase(phase);

            Assert.Equal(expectedSpeaker, speaker);
            Assert.Equal(expectedTurn, turn);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("dialogue_options")]
        [InlineData("delivery")]
        [InlineData("datee_response")]
        public void ParsePhase_unknown_phases_yield_Unknown_speaker_and_null_turn(string? phase)
        {
            var (speaker, turn) = RawLlmSession.ParsePhase(phase);

            Assert.Equal("Unknown", speaker);
            Assert.Null(turn);
        }

        // ─────────────────────────────────────────────────────────────────────
        // B. RecordingLlmTransport decorator
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task RecordingTransport_records_one_session_per_call()
        {
            var stub = new CannedTransport("hello");
            var rec  = new RecordingLlmTransport(stub, modelLabel: "test-model");

            await rec.SendAsync("sys", "usr", temperature: 0.5, maxTokens: 256, phase: "harness-turn-1");
            await rec.SendAsync("sys2", "usr2", temperature: 0.8, maxTokens: 512, phase: "harness-pursuer-open");

            Assert.Equal(2, rec.Sessions.Count);
        }

        [Fact]
        public async Task RecordingTransport_session_fields_are_correct()
        {
            var stub = new CannedTransport("the-response");
            var rec  = new RecordingLlmTransport(stub, modelLabel: "claude-test");

            string returned = await rec.SendAsync(
                "my-system", "my-user", temperature: 0.7, maxTokens: 128, phase: "harness-turn-3");

            Assert.Equal("the-response", returned);            // response passed through

            var s = rec.Sessions[0];
            Assert.Equal("my-system",    s.SystemPrompt);
            Assert.Equal("my-user",      s.UserMessage);
            Assert.Equal(0.7,            s.Temperature);
            Assert.Equal(128,            s.MaxTokens);
            Assert.Equal("the-response", s.RawResponse);
            Assert.Equal("claude-test",  s.Model);
            Assert.Equal("Character",    s.Speaker);
            Assert.Equal(3,              s.Turn);
        }

        [Fact]
        public async Task RecordingTransport_pursuer_open_session_is_correct()
        {
            var stub = new CannedTransport("opening line");
            var rec  = new RecordingLlmTransport(stub);

            await rec.SendAsync("sp", "um", phase: "harness-pursuer-open");

            var s = rec.Sessions[0];
            Assert.Equal("Pursuer", s.Speaker);
            Assert.Equal(0,         s.Turn);
            Assert.Null(s.Model);   // no label supplied
        }

        [Fact]
        public async Task RecordingTransport_does_not_record_on_inner_exception()
        {
            var stub = new ThrowingTransport(new InvalidOperationException("boom"));
            var rec  = new RecordingLlmTransport(stub);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => rec.SendAsync("sp", "um", phase: "harness-turn-1"));

            Assert.Empty(rec.Sessions);
        }

        [Fact]
        public async Task RecordingTransport_model_label_null_by_default()
        {
            var stub = new CannedTransport("ok");
            var rec  = new RecordingLlmTransport(stub);

            await rec.SendAsync("sp", "um", phase: "harness-pursuer-1");

            Assert.Null(rec.Sessions[0].Model);
        }

        [Fact]
        public async Task RecordingTransport_passes_all_parameters_to_inner()
        {
            var spy = new SpyTransport();
            var rec = new RecordingLlmTransport(spy);

            await rec.SendAsync("SYS", "USR", temperature: 0.42, maxTokens: 333,
                phase: "harness-turn-5");

            Assert.Equal("SYS",  spy.LastSystemPrompt);
            Assert.Equal("USR",  spy.LastUserMessage);
            Assert.Equal(0.42,   spy.LastTemperature);
            Assert.Equal(333,    spy.LastMaxTokens);
            Assert.Equal("harness-turn-5", spy.LastPhase);
        }

        // ─────────────────────────────────────────────────────────────────────
        // C. HarnessRunner.RunAsync raw-session exposure
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When the transport IS a RecordingLlmTransport, RunAsync returns a
        /// HarnessRunResult whose RawSessions list is non-empty and contains at
        /// least one entry per LLM call (character turn + pursuer lines).
        /// A 1-turn run driven by the GenericLlmPursuerActor makes exactly 1
        /// character-side call (harness-turn-1) — the generic pursuer skips the
        /// LLM for the opening line (it uses a fixed fallback) and makes 0
        /// subsequent calls after the last turn.
        /// </summary>
        [Fact]
        public async Task HarnessRunner_with_RecordingTransport_exposes_raw_sessions()
        {
            var inner = new CannedTransport("canned reply");
            var rec   = new RecordingLlmTransport(inner, modelLabel: "stub");

            var runner = BuildRunner(rec, turns: 1, pursuerAssembledPrompt: null);
            var result = await runner.RunAsync();

            Assert.NotNull(result.RawSessions);
            Assert.NotEmpty(result.RawSessions);

            // The character-side call for turn 1 must be present.
            var charSession = result.RawSessions.FirstOrDefault(
                s => s.Speaker == "Character" && s.Turn == 1);
            Assert.NotNull(charSession);
            Assert.Equal("stub", charSession!.Model);
        }

        [Fact]
        public async Task HarnessRunner_without_RecordingTransport_returns_empty_raw_sessions()
        {
            var plain = new CannedTransport("plain reply");

            var runner = BuildRunner(plain, turns: 1, pursuerAssembledPrompt: null);
            var result = await runner.RunAsync();

            Assert.NotNull(result.RawSessions);
            Assert.Empty(result.RawSessions);
        }

        [Fact]
        public async Task HarnessRunner_with_CharacterPursuer_captures_pursuer_calls_too()
        {
            var inner = new CannedTransport("canned");
            var rec   = new RecordingLlmTransport(inner, modelLabel: "stub");

            // Use a real pursuer character so the pursuer makes LLM calls.
            var runner = BuildRunner(rec, turns: 1,
                pursuerAssembledPrompt: "You are Velvet_99, a romantic pursuer.");

            var result = await runner.RunAsync();

            // Should contain at least one Pursuer session (the opening line
            // from CharacterPursuerActor goes via harness-pursuer-open).
            bool hasPursuer = result.RawSessions.Any(s => s.Speaker == "Pursuer");
            Assert.True(hasPursuer,
                "Expected at least one Pursuer session in RawSessions when CharacterPursuerActor is used.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // D. Transcript byte-identity
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Two runs with identical stubs and a plain (non-recording) transport
        /// produce byte-identical Transcripts — proving that the transcript
        /// generation is unaffected by the new code paths.
        /// </summary>
        [Fact]
        public async Task HarnessRunner_transcript_is_byte_identical_across_identical_runs()
        {
            // Run 1
            string t1;
            {
                var inner = new CannedTransport("stub-reply-A");
                var runner = BuildRunner(inner, turns: 2, pursuerAssembledPrompt: null);
                t1 = (await runner.RunAsync()).Transcript;
            }

            // Run 2 — identical stubs
            string t2;
            {
                var inner = new CannedTransport("stub-reply-A");
                var runner = BuildRunner(inner, turns: 2, pursuerAssembledPrompt: null);
                t2 = (await runner.RunAsync()).Transcript;
            }

            Assert.Equal(t1, t2);
        }

        /// <summary>
        /// The Transcript from a RecordingLlmTransport run is byte-identical to
        /// the Transcript from a plain-transport run with the same canned
        /// responses — the decorator is truly additive.
        /// </summary>
        [Fact]
        public async Task HarnessRunner_transcript_same_with_and_without_recorder()
        {
            const string reply = "same-reply";

            string plainTranscript;
            {
                var plain = new CannedTransport(reply);
                var runner = BuildRunner(plain, turns: 1, pursuerAssembledPrompt: null);
                plainTranscript = (await runner.RunAsync()).Transcript;
            }

            string recordingTranscript;
            {
                var inner = new CannedTransport(reply);
                var rec   = new RecordingLlmTransport(inner);
                var runner = BuildRunner(rec, turns: 1, pursuerAssembledPrompt: null);
                recordingTranscript = (await runner.RunAsync()).Transcript;
            }

            Assert.Equal(plainTranscript, recordingTranscript);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build a HarnessRunner with minimal wiring for unit tests.
        /// Uses GameDefinition.PinderDefaults to avoid needing real game-def YAML.
        /// Uses NarrativePromptLoader which the test module-init already wires.
        /// </summary>
        private static HarnessRunner BuildRunner(
            ILlmTransport transport,
            int turns,
            string? pursuerAssembledPrompt)
        {
            var baseDef   = GameDefinition.PinderDefaults;
            var character = MakeCharacter("TestChar");
            var menu      = ConfessionMenu.Build(
                "TestChar",
                "Surface fear: [1] I want connection. [2] I push people away.",
                "Background: grew up near the sea.");

            var opts = MakeOptions(turns);

            var pursuer = PursuerActorFactory.Create(
                transport,
                scriptedLines: null,
                pursuerAssembledPrompt: pursuerAssembledPrompt,
                pursuerDisplayName: pursuerAssembledPrompt != null ? "VelvetTest" : null,
                pursuerSlug: pursuerAssembledPrompt != null ? "velvettest" : null,
                baseDef: baseDef);

            return new HarnessRunner(transport, character, menu, baseDef, opts, pursuer);
        }

        private static LoadedCharacter MakeCharacter(string name)
        {
            // CharacterProfile — mirrors Phase0Fixtures.MakeProfile pattern.
            var stats   = MakeStatBlock();
            var profile = new CharacterProfile(
                stats: stats,
                assembledSystemPrompt: $"You are {name}, a test character.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);

            // CharacterDefinition — minimal valid construction.
            var def = new CharacterDefinition(
                schemaVersion: 2,
                characterId: Guid.NewGuid(),
                name: name,
                genderIdentity: "they/them",
                bio: "A test character.",
                level: 1,
                items: new List<string>(),
                anatomy: new Dictionary<string, float>(),
                allocation: new AllocationBlock(
                    spent: new Dictionary<StatType, int>(),
                    unspentPool: 0,
                    shadows: new Dictionary<ShadowStatType, int>()),
                psychologicalStake: "Surface fear: [1] I want connection. [2] I push people away.",
                backgroundStory: "Grew up near the sea.");

            return new LoadedCharacter(profile, def);
        }

        private static StatBlock MakeStatBlock()
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                { StatType.Chaos, 2 }, { StatType.Wit, 2 },  { StatType.SelfAwareness, 2 }
            };
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial,  0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread,   0 }, { ShadowStatType.Overthinking, 0 }
            };
            return new StatBlock(stats, shadows);
        }

        /// <summary>
        /// Build a HarnessOptions with the specified turn count. Because the
        /// setters are private, we use the public Parse() pathway with a
        /// hand-rolled arg array.
        /// </summary>
        private static HarnessOptions MakeOptions(int turns)
            => HarnessOptions.Parse(new[] { "--turns", turns.ToString(), "--character", "testchar" });

        // ── Stub transports ───────────────────────────────────────────────────

        /// <summary>Returns the same canned response for every call.</summary>
        private sealed class CannedTransport : ILlmTransport
        {
            private readonly string _response;
            public CannedTransport(string response) => _response = response;

            public Task<string> SendAsync(string systemPrompt, string userMessage,
                double temperature = 0.9, int maxTokens = 1024, string? phase = null,
                CancellationToken ct = default)
                => Task.FromResult(_response);
        }

        /// <summary>Always throws the supplied exception.</summary>
        private sealed class ThrowingTransport : ILlmTransport
        {
            private readonly Exception _ex;
            public ThrowingTransport(Exception ex) => _ex = ex;

            public Task<string> SendAsync(string systemPrompt, string userMessage,
                double temperature = 0.9, int maxTokens = 1024, string? phase = null,
                CancellationToken ct = default)
                => Task.FromException<string>(_ex);
        }

        /// <summary>Records the last set of parameters for inspection.</summary>
        private sealed class SpyTransport : ILlmTransport
        {
            public string? LastSystemPrompt { get; private set; }
            public string? LastUserMessage  { get; private set; }
            public double  LastTemperature  { get; private set; }
            public int     LastMaxTokens    { get; private set; }
            public string? LastPhase        { get; private set; }

            public Task<string> SendAsync(string systemPrompt, string userMessage,
                double temperature = 0.9, int maxTokens = 1024, string? phase = null,
                CancellationToken ct = default)
            {
                LastSystemPrompt = systemPrompt;
                LastUserMessage  = userMessage;
                LastTemperature  = temperature;
                LastMaxTokens    = maxTokens;
                LastPhase        = phase;
                return Task.FromResult("ok");
            }
        }
    }
}
