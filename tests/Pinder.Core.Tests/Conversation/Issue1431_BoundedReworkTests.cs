using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Tests.AgentJournals;
using Pinder.Core.Stats;
using Pinder.Core.Conversation;
using Pinder.SessionRunner.Snapshot;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    public sealed class Issue1431_BoundedReworkTests
    {
        private static readonly Guid PlayerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid DateeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid RestorePlayerId = TestHelpers.DeterministicCharacterId("P1");
        private static readonly Guid RestoreDateeId = TestHelpers.DeterministicCharacterId("P2");

        [Fact]
        public void DirectRestoreRejectsStandaloneAndMixedLegacyActiveTargets()
        {
            GameSession session = CreateSession();
            var legacy = new ResolvedRevelationTarget
            {
                Registry = EmotionStemSelectionRules.BackstoryRegistry,
                Index = 0,
                Field = "BIO_LIE",
                Manner = "CURATED_BUFFER",
                StemText = "legacy target sentinel",
                TransitionStyle = "soft",
            };
            AvatarRevelationTarget typed = AvatarTarget();

            InvalidOperationException standalone = Assert.Throws<InvalidOperationException>(() => session.RestoreState(
                new ResimulateData(RestorePlayerId, RestoreDateeId) { TargetInterest = 10, CurrentResolvedTarget = legacy },
                new NullTrapRegistry()));
            InvalidOperationException mixed = Assert.Throws<InvalidOperationException>(() => session.RestoreState(
                new ResimulateData(RestorePlayerId, RestoreDateeId)
                {
                    TargetInterest = 10,
                    CurrentResolvedTarget = legacy,
                    CurrentAvatarRevelationTarget = typed,
                },
                new NullTrapRegistry()));

            Assert.Equal("restore.role_target.legacy_ambiguous_active_target", standalone.Message);
            Assert.Equal(standalone.Message, mixed.Message);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void SessionRunnerRejectsAnyLegacyActiveTarget(bool avatarActive, bool dateeActive)
        {
            var snapshot = new TurnSnapshot
            {
                SchemaVersion = SessionSnapshotSchema.CurrentVersion,
                CurrentResolvedTarget = new RoleTargetSnapshot(),
                AvatarRevelationTarget = avatarActive ? new RoleTargetSnapshot() : null,
                DateeReactionTarget = dateeActive ? new RoleTargetSnapshot() : null,
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                Program.ValidateAndPatchTurnSnapshot(snapshot, new List<string>()));

            Assert.Equal("snapshot.role_target.legacy_active_target_forbidden", error.Message);
        }

        [Fact]
        public void SessionRunnerRoundTripsRoleFactAndTargetMetadata()
        {
            ConversationMessageReference revealedBy = ConversationMessageReference.Create(
                7,
                ConversationParticipantRole.Datee);
            var fact = new OwnedPromptFactV1(
                PlayerId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.RevealedToPlayerAvatar,
                PromptFactSourceKind.Backstory,
                PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie"),
                "typed target text",
                revealedBy);
            var resolved = new ResolvedRevelationTarget
            {
                Registry = EmotionStemSelectionRules.BackstoryRegistry,
                Index = 0,
                Field = "BIO_LIE",
                Manner = "CURATED_BUFFER",
                StemText = "typed target text",
                TransitionStyle = "sideways",
            };
            RoleTargetSnapshot persisted = Program.ToSnapshot(
                AvatarRevelationTarget.Create(PlayerId, fact, resolved))!;

            Assert.Equal(OwnedPromptFactV1.CurrentSchemaVersion, persisted.Fact.SchemaVersion);
            Assert.Equal(PromptFactSourceKind.Backstory.ToString(), persisted.Fact.SourceKind);
            Assert.Equal(revealedBy.Value, persisted.Fact.RevealedBy);
            Assert.Equal(resolved.Registry, persisted.Registry);
            Assert.Equal(resolved.Index, persisted.Index);
            Assert.Equal(resolved.Field, persisted.Field);
            Assert.Equal(resolved.Manner, persisted.Manner);
            Assert.Equal(resolved.TransitionStyle, persisted.TransitionStyle);

            OwnedPromptFactV1 restored = Program.ToCoreFact(persisted.Fact)!;
            Assert.Equal(fact.SchemaVersion, restored.SchemaVersion);
            Assert.Equal(fact.SourceKind, restored.SourceKind);
            Assert.Equal(fact.SourceId, restored.SourceId);
            Assert.Equal(fact.RevealedBy, restored.RevealedBy);

            persisted.Fact.SchemaVersion = 2;
            Assert.Throws<RoleFactContractException>(() => Program.ToCoreFact(persisted.Fact));
            persisted.Fact.SchemaVersion = OwnedPromptFactV1.CurrentSchemaVersion;
            persisted.Fact.RevealedBy = null;
            Assert.Throws<RoleFactContractException>(() => Program.ToCoreFact(persisted.Fact));
        }

        [Theory]
        [InlineData("registry", "target.registry.invalid")]
        [InlineData("index", "target.source_id.category_mismatch")]
        [InlineData("field", "target.source_id.field_mismatch")]
        [InlineData("source", "target.source_id.category_mismatch")]
        [InlineData("stem", "target.stem_text_mismatch")]
        public void TamperedTargetSnapshotsFailCentralCoherenceValidation(string tamper, string expectedCode)
        {
            RoleTargetSnapshot snapshot = Program.ToSnapshot(AvatarTarget())!;
            switch (tamper)
            {
                case "registry": snapshot.Registry = "UNKNOWN"; break;
                case "index": snapshot.Index = 1; break;
                case "field": snapshot.Field = "TRAGIC_REALITY"; break;
                case "source": snapshot.Fact.SourceId = PromptFactSourceIds.Backstory(PlayerId, "relationships_and_family", "bio_lie").Value; break;
                case "stem": snapshot.StemText = "tampered private text"; break;
            }
            var envelope = new TurnSnapshot
            {
                SchemaVersion = SessionSnapshotSchema.CurrentVersion,
                AvatarRevelationTarget = snapshot,
            };

            RoleFactContractException error = Assert.Throws<RoleFactContractException>(() =>
                Program.BuildResimulateData(envelope, PlayerId, DateeId));

            Assert.Equal(expectedCode, error.Code);
        }

        [Fact]
        public void CharacterProfileRejectsMissingCanonicalIdentityWithoutNameFallback()
        {
            RoleFactContractException error = Assert.Throws<RoleFactContractException>(() => new CharacterProfile(
                TestHelpers.MakeStatBlock(),
                "prompt",
                "display-name-must-not-be-identity",
                new TimingProfile(5, 0, 0, "neutral"),
                1));

            Assert.Equal("character_profile.character_id.required", error.Code);
        }

        [Fact]
        public void SnapshotEnvelopeVersionsCoverCurrentLegacyUnsupportedAndMissing()
        {
            CharacterSnapshot player = CharacterSnapshotFor(PlayerId, "Gerald");
            CharacterSnapshot datee = CharacterSnapshotFor(DateeId, "Velvet");
            Assert.NotNull(Program.ValidateInitialSessionSnapshot(new InitialSessionSnapshot
            {
                SchemaVersion = SessionSnapshotSchema.CurrentVersion,
                Player = player,
                Datee = datee,
            }));
            Assert.NotNull(Program.ValidateInitialSessionSnapshot(new InitialSessionSnapshot
            {
                SchemaVersion = SessionSnapshotSchema.IdentityBackedLegacyVersion,
                Player = player,
                Datee = datee,
            }));

            Assert.Equal("snapshot.schema_version.required", Assert.Throws<InvalidOperationException>(() =>
                Program.ValidateInitialSessionSnapshot(new InitialSessionSnapshot { Player = player, Datee = datee })).Message);
            Assert.Equal("snapshot.schema_version.unsupported", Assert.Throws<InvalidOperationException>(() =>
                Program.ValidateInitialSessionSnapshot(new InitialSessionSnapshot { SchemaVersion = 99, Player = player, Datee = datee })).Message);
            Assert.NotNull(Program.ValidateAndPatchTurnSnapshot(new TurnSnapshot
            {
                SchemaVersion = SessionSnapshotSchema.IdentityBackedLegacyVersion,
            }, new List<string>(), authoritativeIdentityAvailable: true));
            Assert.Equal("snapshot.schema_version.legacy_active_target_forbidden", Assert.Throws<InvalidOperationException>(() =>
                Program.ValidateAndPatchTurnSnapshot(new TurnSnapshot
                {
                    SchemaVersion = SessionSnapshotSchema.IdentityBackedLegacyVersion,
                    AvatarRevelationTarget = Program.ToSnapshot(AvatarTarget()),
                }, new List<string>(), authoritativeIdentityAvailable: true)).Message);
            Assert.Equal("snapshot.character_identity.required", Assert.Throws<InvalidOperationException>(() =>
                Program.ValidateAndPatchTurnSnapshot(new TurnSnapshot
                {
                    SchemaVersion = SessionSnapshotSchema.IdentityBackedLegacyVersion,
                }, new List<string>(), authoritativeIdentityAvailable: false)).Message);
        }

        [Fact]
        public void ResimulateDataVersionsCoverCurrentIdentityBackedLegacyUnsupportedAndMissing()
        {
            GameSession session = CreateSession();
            session.RestoreState(new ResimulateData(RestorePlayerId, RestoreDateeId)
            {
                TargetInterest = 10,
            }, new NullTrapRegistry());
            session.RestoreState(new ResimulateData
            {
                SchemaVersion = ResimulateData.IdentityBackedLegacySchemaVersion,
                PlayerCharacterId = RestorePlayerId,
                DateeCharacterId = RestoreDateeId,
                TargetInterest = 10,
            }, new NullTrapRegistry());

            Assert.Equal("restore.schema_version.required", Assert.Throws<InvalidOperationException>(() =>
                session.RestoreState(new ResimulateData
                {
                    PlayerCharacterId = RestorePlayerId,
                    DateeCharacterId = RestoreDateeId,
                }, new NullTrapRegistry())).Message);
            Assert.Equal("restore.schema_version.unsupported", Assert.Throws<InvalidOperationException>(() =>
                session.RestoreState(new ResimulateData
                {
                    SchemaVersion = 99,
                    PlayerCharacterId = RestorePlayerId,
                    DateeCharacterId = RestoreDateeId,
                }, new NullTrapRegistry())).Message);
            Assert.Equal("restore.character_identity.required", Assert.Throws<InvalidOperationException>(() =>
                session.RestoreState(new ResimulateData
                {
                    SchemaVersion = ResimulateData.IdentityBackedLegacySchemaVersion,
                }, new NullTrapRegistry())).Message);
        }

        [Fact]
        public void AgentJournalValidationRejectsUndefinedPromptFactSourceKind()
        {
            var decision = new AgentJournalRoleFactAccessDecision(
                admitted: true,
                code: "admitted.subject",
                factSourceId: PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie").Value,
                factSourceKind: (PromptFactSourceKind)0,
                subjectCharacterId: PlayerId,
                subjectRole: ConversationParticipantRole.PlayerAvatar,
                recipientCharacterId: PlayerId,
                recipientRole: ConversationParticipantRole.PlayerAvatar,
                visibility: PromptFactVisibility.PrivateToSubject);
            var invocation = new LlmInvocationRecord(
                AgentJournalTestRecords.Correlation(),
                "test-model",
                "dialogue_options",
                new[]
                {
                    AgentJournalTestRecords.Document(
                        "doc.system",
                        "system text",
                        AgentJournalTestRecords.Range("doc.system", 0, 11)),
                },
                roleFactAccessDecisions: new[] { decision });

            AgentJournalValidationResult result = AgentJournalValidator.Validate(invocation);

            Assert.Contains(result.Errors, error =>
                error.Code == AgentJournalValidator.InvalidRoleFactDecision
                && error.Path == "$.role_fact_access_decisions[0]");
        }

        [Fact]
        public async Task TransactionalDenialEmitsDiagnosticAndMakesNoProviderCallRetryOrStateMutation()
        {
            const string secret = "GERALD_PRIVATE_DENIAL_SENTINEL_1431";
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var adapter = new CountingAdapter();
            GameSession session = CreateSession(adapter, diagnostics.Add);
            await session.StartTurnAsync();
            int callsBefore = adapter.TotalCalls;
            GameSessionState state = GetState(session);
            state.CurrentDateeCognitiveSubtextFact = new OwnedPromptFactV1(
                RestorePlayerId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.CognitiveSubtext,
                PromptFactSourceIds.CognitiveSubtext(RestorePlayerId, 0),
                secret);
            string stateBefore = JsonSerializer.Serialize(session.CreateResimulateData());

            for (int attempt = 0; attempt < 2; attempt++)
            {
                RoleFactAccessDeniedException error = await Assert.ThrowsAsync<RoleFactAccessDeniedException>(() =>
                    session.ResolveTurnAsync(0));
                Assert.Equal("denied.private_to_subject", error.Decision.Code);
                Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
                Assert.Equal(callsBefore, adapter.TotalCalls);
                Assert.Equal(stateBefore, JsonSerializer.Serialize(session.CreateResimulateData()));
            }

            Assert.Equal(2, diagnostics.Count(diagnostic =>
                diagnostic.EventName == AgentJournalOperationalDiagnostics.RoleFactAccessRejectedEventName));
            Assert.DoesNotContain(secret, JsonSerializer.Serialize(diagnostics.Select(diagnostic => new
            {
                diagnostic.Message,
                diagnostic.CorrelationHints,
            })), StringComparison.Ordinal);
        }

        [Fact]
        public async Task RejectionJournalSinkFailureFailsClosedWithoutProviderDiceOrStateMutation()
        {
            const string secret = "GERALD_PRIVATE_SINK_FAILURE_SENTINEL_1431";
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var adapter = new CountingAdapter();
            var dice = new CountingDice();
            var journalContext = new GameRunAgentJournalContext(
                "game-run-core-1431",
                "agent-session-core-1431",
                requestId: "request-core-1431",
                branchId: "main",
                hostSink: new ThrowingJournalSink());
            GameSession session = CreateSession(adapter, diagnostics.Add, dice, journalContext);
            await session.StartTurnAsync();
            int callsBefore = adapter.TotalCalls;
            int rollsBefore = dice.RollCount;
            GameSessionState state = GetState(session);
            state.CurrentDateeCognitiveSubtextFact = new OwnedPromptFactV1(
                RestorePlayerId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.CognitiveSubtext,
                PromptFactSourceIds.CognitiveSubtext(RestorePlayerId, 0),
                secret);
            string stateBefore = JsonSerializer.Serialize(session.CreateResimulateData());

            AgentJournalSinkPersistenceException error = await Assert.ThrowsAsync<AgentJournalSinkPersistenceException>(() =>
                session.ResolveTurnAsync(0));

            Assert.Equal(AgentJournalSchemaNames.RoleFactPolicyDecisionV1, error.CustomType);
            Assert.Equal(callsBefore, adapter.TotalCalls);
            Assert.Equal(rollsBefore, dice.RollCount);
            Assert.Equal(stateBefore, JsonSerializer.Serialize(session.CreateResimulateData()));
            OperationalDiagnosticEvent diagnostic = Assert.Single(diagnostics.Where(item =>
                item.EventName == AgentJournalOperationalDiagnostics.SinkPersistenceFailedEventName));
            Assert.Equal(OperationalDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(OperationalDiagnosticLifecycle.Terminal, diagnostic.Lifecycle);
            Assert.Equal(OperationalDiagnosticOutcome.Failed, diagnostic.Outcome);
            Assert.Equal("FailClosed", diagnostic.CorrelationHints["failure_mode"]);
            Assert.Equal("request-core-1431", diagnostic.CorrelationHints["request_id"]);
            Assert.DoesNotContain(secret, JsonSerializer.Serialize(diagnostics.Select(item => new
            {
                item.EventName,
                item.Message,
                item.CorrelationId,
                item.CorrelationHints,
            })), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(2, 3, true)]
        [InlineData(3, 4, true)]
        [InlineData(2, 4, false)]
        [InlineData(0, 4, false)]
        public void AvatarTargetSpendingTracksConfiguredFinalOption(int optionIndex, int optionCount, bool expected)
        {
            Assert.Equal(expected, TurnOrchestrator.ShouldSpendAvatarTarget(optionIndex, optionCount));
        }

        private static CharacterSnapshot CharacterSnapshotFor(Guid id, string name)
            => new CharacterSnapshot
            {
                CharacterId = id.ToString("D"),
                DisplayName = name,
                Stats = new Dictionary<string, int>(),
            };

        private static GameSessionState GetState(GameSession session)
            => (GameSessionState)typeof(GameSession)
                .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(session)!;

        private sealed class CountingAdapter : NullLlmAdapter
        {
            public int TotalCalls { get; private set; }

            public override Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
            {
                TotalCalls++;
                return base.GetDialogueOptionsAsync(context, ct);
            }

            public override Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken cancellationToken = default)
            {
                TotalCalls++;
                return base.GetDateeResponseAsync(context, history, cancellationToken);
            }

            public override Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default)
            {
                TotalCalls++;
                return base.GetSuccessImprovementAsync(context, ct);
            }

            public override Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
            {
                TotalCalls++;
                return base.GetInterestChangeBeatAsync(context, ct);
            }
        }

        private sealed class CountingDice : IDiceRoller
        {
            public int RollCount { get; private set; }

            public int Roll(int sides)
            {
                RollCount++;
                return Math.Min(5, sides);
            }
        }

        private sealed class ThrowingJournalSink : IAgentJournalSink
        {
            public Task PersistAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken)
                => Task.FromException(new InvalidOperationException("simulated journal persistence failure"));
        }

        private static AvatarRevelationTarget AvatarTarget()
        {
            var fact = new OwnedPromptFactV1(
                PlayerId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.Backstory,
                PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie"),
                "typed target sentinel");
            var target = new ResolvedRevelationTarget
            {
                Registry = EmotionStemSelectionRules.BackstoryRegistry,
                Index = 0,
                Field = "BIO_LIE",
                Manner = "CURATED_BUFFER",
                StemText = fact.Text,
                TransitionStyle = "soft",
            };
            return AvatarRevelationTarget.Create(PlayerId, fact, target);
        }

        private static GameSession CreateSession(
            NullLlmAdapter? adapter = null,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null,
            IDiceRoller? dice = null,
            GameRunAgentJournalContext? agentJournalContext = null)
        {
            return new GameSession(
                TestHelpers.MakeCharacterProfile(
                    TestHelpers.MakeStatBlock(),
                    "player prompt",
                    "P1",
                    new TimingProfile(5, 0, 0, "neutral"),
                    1),
                TestHelpers.MakeCharacterProfile(
                    TestHelpers.MakeStatBlock(),
                    "datee prompt",
                    "P2",
                    new TimingProfile(5, 0, 0, "neutral"),
                    1),
                adapter ?? new NullLlmAdapter(),
                dice ?? new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(
                    clock: TestHelpers.MakeClock(),
                    onDiagnostic: onDiagnostic,
                    agentJournalContext: agentJournalContext));
        }
    }
}
