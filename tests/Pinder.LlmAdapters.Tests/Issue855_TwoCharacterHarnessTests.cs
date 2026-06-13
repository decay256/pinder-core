using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Pinder.Tools.NarrativeHarness;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #855: the Narrative harness can drive BOTH sides as real Pinder
    /// characters that stay in character for the whole conversation. These
    /// regression tests pin the pursuer-side wiring:
    ///   • two-character mode → the pursuer is a real character driven through
    ///     the SAME production prompt path as the datee (BuildDatee);
    ///   • single-character fallback → the generic lightweight LLM persona
    ///     (NOT a real character) is used, exactly as before;
    ///   • scripted mode (--player-script) → fixed lines, no transport calls.
    ///
    /// Deterministic: the LLM is stubbed by <see cref="RecordingTransport"/>
    /// (same pattern as #340/#831) — no real network calls.
    /// </summary>
    public class Issue855_TwoCharacterHarnessTests
    {
        // A small but recognizable "assembled system prompt" for the pursuer
        // character — its presence in the system prompt proves the production
        // BuildDatee path was used.
        private const string PursuerAssembledPrompt =
            "You are Velvet_99, a velvet-voiced romantic who never breaks character.";

        // ── 1. Two-character mode: pursuer uses the BuildDatee path ────────

        [Fact]
        public void Factory_with_pursuer_character_returns_character_actor()
        {
            var transport = new RecordingTransport("reply");
            IPursuerActor actor = PursuerActorFactory.Create(
                transport, scriptedLines: null,
                pursuerAssembledPrompt: PursuerAssembledPrompt,
                pursuerDisplayName: "Velvet_99", pursuerSlug: "velvet",
                baseDef: GameDefinition.PinderDefaults);

            Assert.IsType<CharacterPursuerActor>(actor);
            Assert.Contains("velvet", actor.HeaderLabel);
            Assert.Contains("BuildDatee", actor.HeaderLabel);
        }

        [Fact]
        public async Task Character_pursuer_drives_BuildDatee_system_prompt_with_its_own_profile()
        {
            var transport = new RecordingTransport("hey, I noticed you too.");
            IPursuerActor actor = PursuerActorFactory.Create(
                transport, scriptedLines: null,
                pursuerAssembledPrompt: PursuerAssembledPrompt,
                pursuerDisplayName: "Velvet_99", pursuerSlug: "velvet",
                baseDef: GameDefinition.PinderDefaults);

            var transcript = new List<(string, string)>
            {
                ("Pursuer", "opening"),
                ("Datee", "a reply from the datee"),
            };
            string? line = await actor.NextLineAsync(transcript, turn: 1);

            Assert.Equal("hey, I noticed you too.", line);
            // The system prompt is the REAL production datee prompt for the
            // pursuer character — proven by the shared GM character-spec block
            // and the pursuer's own assembled profile text appearing in it.
            Assert.NotNull(transport.LastSystem);
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, transport.LastSystem!);
            Assert.Contains(PursuerAssembledPrompt, transport.LastSystem!);
            // It is NOT the generic-persona prompt.
            Assert.DoesNotContain("witty, curious person texting", transport.LastSystem!);
        }

        [Fact]
        public async Task Character_pursuer_is_reactive_no_arc_injection()
        {
            // ARC DESIGN DECISION (#855): the pursuer is REACTIVE — built from the
            // BASE game definition with NO arc text, so its system prompt carries
            // no "== CONVERSATION ARC ==" section. Only the datee carries the
            // experiment's arc. This pins that reversible default.
            var transport = new RecordingTransport("reactive line");
            IPursuerActor actor = PursuerActorFactory.Create(
                transport, scriptedLines: null,
                pursuerAssembledPrompt: PursuerAssembledPrompt,
                pursuerDisplayName: "Velvet_99", pursuerSlug: "velvet",
                baseDef: GameDefinition.PinderDefaults);

            await actor.NextLineAsync(new List<(string, string)> { ("Pursuer", "hi") }, turn: 1);

            Assert.NotNull(transport.LastSystem);
            Assert.DoesNotContain("== CONVERSATION ARC ==", transport.LastSystem!);
        }

        [Fact]
        public async Task Character_pursuer_relabels_its_own_lines_to_its_name_in_its_view()
        {
            // Each side maintains its OWN conversation view: the pursuer sees its
            // own "Pursuer"-keyed lines under its display name, the datee's
            // lines as the inbound messages.
            var transport = new RecordingTransport("ok");
            IPursuerActor actor = PursuerActorFactory.Create(
                transport, scriptedLines: null,
                pursuerAssembledPrompt: PursuerAssembledPrompt,
                pursuerDisplayName: "Velvet_99", pursuerSlug: "velvet",
                baseDef: GameDefinition.PinderDefaults);

            var transcript = new List<(string, string)>
            {
                ("Pursuer", "my earlier line"),
                ("Brick_77", "the datee answered"),
            };
            await actor.NextLineAsync(transcript, turn: 2);

            Assert.NotNull(transport.LastUser);
            Assert.Contains("Velvet_99: my earlier line", transport.LastUser!);
            Assert.Contains("Brick_77: the datee answered", transport.LastUser!);
            Assert.DoesNotContain("Pursuer: my earlier line", transport.LastUser!);
        }

        // ── 2. Single-character fallback: generic LLM persona ────────────────

        [Fact]
        public void Factory_without_pursuer_or_script_returns_generic_actor()
        {
            var transport = new RecordingTransport("reply");
            IPursuerActor actor = PursuerActorFactory.Create(
                transport, scriptedLines: null,
                pursuerAssembledPrompt: null, pursuerDisplayName: null,
                pursuerSlug: null, baseDef: GameDefinition.PinderDefaults);

            Assert.IsType<GenericLlmPursuerActor>(actor);
            Assert.Contains("NOT a real character", actor.HeaderLabel);
        }

        [Fact]
        public async Task Generic_pursuer_uses_lightweight_standalone_persona()
        {
            var transport = new RecordingTransport("tell me more");
            IPursuerActor actor = PursuerActorFactory.Create(
                transport, scriptedLines: null,
                pursuerAssembledPrompt: null, pursuerDisplayName: null,
                pursuerSlug: null, baseDef: GameDefinition.PinderDefaults);

            string? line = await actor.NextLineAsync(
                new List<(string, string)> { ("Pursuer", "hi"), ("Datee", "hey") }, turn: 1);

            Assert.Equal("tell me more", line);
            // The generic persona — NOT a production character prompt.
            Assert.NotNull(transport.LastSystem);
            Assert.Contains("witty, curious person texting", transport.LastSystem!);
            Assert.DoesNotContain("== DATEE CHARACTER ==", transport.LastSystem!);
        }

        // ── 3. Scripted mode still works ─────────────────────────────────────

        [Fact]
        public void Factory_with_script_and_no_pursuer_character_returns_scripted_actor()
        {
            var transport = new RecordingTransport("unused");
            IPursuerActor actor = PursuerActorFactory.Create(
                transport, scriptedLines: new List<string> { "a", "b" },
                pursuerAssembledPrompt: null, pursuerDisplayName: null,
                pursuerSlug: null, baseDef: GameDefinition.PinderDefaults);

            Assert.IsType<ScriptedPursuerActor>(actor);
            Assert.Contains("scripted", actor.HeaderLabel);
        }

        [Fact]
        public async Task Scripted_pursuer_returns_lines_in_order_then_ends_with_no_transport_calls()
        {
            var transport = new RecordingTransport("SHOULD NOT BE CALLED");
            IPursuerActor actor = PursuerActorFactory.Create(
                transport, scriptedLines: new List<string> { "line one", "line two" },
                pursuerAssembledPrompt: null, pursuerDisplayName: null,
                pursuerSlug: null, baseDef: GameDefinition.PinderDefaults);

            var transcript = new List<(string, string)>();
            Assert.Equal("line one", await actor.OpeningLineAsync());
            Assert.Equal("line two", await actor.NextLineAsync(transcript, turn: 1));
            // Ran out of lines → null ends the conversation.
            Assert.Null(await actor.NextLineAsync(transcript, turn: 2));
            Assert.Equal(0, transport.CallCount);
        }

        // ── 4. Precedence: pursuer character beats a script if both given ────

        [Fact]
        public void Factory_pursuer_character_takes_precedence_over_script()
        {
            var transport = new RecordingTransport("reply");
            IPursuerActor actor = PursuerActorFactory.Create(
                transport, scriptedLines: new List<string> { "scripted" },
                pursuerAssembledPrompt: PursuerAssembledPrompt,
                pursuerDisplayName: "Velvet_99", pursuerSlug: "velvet",
                baseDef: GameDefinition.PinderDefaults);

            Assert.IsType<CharacterPursuerActor>(actor);
        }

        // ── Recording transport (mirror of #340/#831 pattern) ────────────────

        private sealed class RecordingTransport : ILlmTransport
        {
            private readonly string _response;

            public string? LastSystem { get; private set; }
            public string? LastUser { get; private set; }
            public int CallCount { get; private set; }

            public RecordingTransport(string response) => _response = response;

            public Task<string> SendAsync(string systemPrompt, string userMessage,
                double temperature = 0.9, int maxTokens = 1024, string? phase = null,
                CancellationToken ct = default)
            {
                CallCount++;
                LastSystem = systemPrompt;
                LastUser = userMessage;
                return Task.FromResult(_response);
            }
        }
    }
}
