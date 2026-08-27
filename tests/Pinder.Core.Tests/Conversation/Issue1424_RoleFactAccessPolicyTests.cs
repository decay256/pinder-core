using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Xunit;

namespace Pinder.Core.Tests.Conversation;

public sealed class Issue1424_RoleFactAccessPolicyTests
{
    private static readonly Guid PlayerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DateeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string SecretSentinel = "Gerald keeps a GBP 70 Soho silk sleeping bag hidden in plain sight.";

    [Fact]
    public void PlayerPrivateSentinelIsDeniedToDateeAndAdmittedToPlayerAvatar()
    {
        var fact = NewFact(
            PlayerId,
            ConversationParticipantRole.PlayerAvatar,
            PromptFactVisibility.PrivateToSubject,
            SecretSentinel);

        RoleFactAccessDecision denied = RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(
            DateeId,
            ConversationParticipantRole.Datee,
            fact));

        RoleFactAccessDecision admitted = RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(
            PlayerId,
            ConversationParticipantRole.PlayerAvatar,
            fact));

        Assert.False(denied.Admitted);
        Assert.Equal("denied.private_to_subject", denied.Code);
        Assert.True(admitted.Admitted);
        Assert.Equal("admitted.subject", admitted.Code);
    }

    [Fact]
    public void AccessMatrixDeniesEveryUnlistedRoleVisibilityCombination()
    {
        var visibilities = new[]
        {
            PromptFactVisibility.PrivateToSubject,
            PromptFactVisibility.Public,
            PromptFactVisibility.RevealedToPlayerAvatar,
            PromptFactVisibility.RevealedToDatee,
        };
        var subjectRoles = new[]
        {
            ConversationParticipantRole.PlayerAvatar,
            ConversationParticipantRole.Datee,
        };
        var recipientRoles = subjectRoles;

        foreach (ConversationParticipantRole subjectRole in subjectRoles)
        {
            Guid subjectId = IdFor(subjectRole);
            foreach (PromptFactVisibility visibility in visibilities)
            {
                foreach (ConversationParticipantRole recipientRole in recipientRoles)
                {
                    foreach (bool sameId in new[] { true, false })
                    {
                        Guid recipientId = sameId ? subjectId : IdFor(Other(recipientRole));
                        var fact = NewFact(subjectId, subjectRole, visibility, "matrix fact", revealedBy: EvidenceFor(visibility));
                        RoleFactAccessDecision decision = RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(recipientId, recipientRole, fact));

                        bool expected = ExpectedAdmission(subjectId, subjectRole, visibility, recipientId, recipientRole);
                        Assert.Equal(expected, decision.Admitted);
                        Assert.Equal(fact.SourceId, decision.FactSourceId);
                        Assert.Equal(subjectId, decision.SubjectCharacterId);
                        Assert.Equal(subjectRole, decision.SubjectRole);
                        Assert.Equal(recipientId, decision.RecipientCharacterId);
                        Assert.Equal(recipientRole, decision.RecipientRole);
                        Assert.Equal(visibility, decision.Visibility);
                    }
                }
            }
        }
    }

    [Fact]
    public void PublicFactsAreAdmittedToEitherParticipantWithoutRevealedEvidence()
    {
        var fact = NewFact(DateeId, ConversationParticipantRole.Datee, PromptFactVisibility.Public, "public profile fact");

        Assert.True(RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(PlayerId, ConversationParticipantRole.PlayerAvatar, fact)).Admitted);
        Assert.True(RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(DateeId, ConversationParticipantRole.Datee, fact)).Admitted);
    }

    [Fact]
    public void RevealedFactsRequireEvidenceAndAdmitOnlySubjectOrNamedOpposingRole()
    {
        Assert.Throws<RoleFactContractException>(() => NewFact(
            DateeId,
            ConversationParticipantRole.Datee,
            PromptFactVisibility.RevealedToPlayerAvatar,
            "revealed without evidence"));

        var dateeFactRevealedToPlayer = NewFact(
            DateeId,
            ConversationParticipantRole.Datee,
            PromptFactVisibility.RevealedToPlayerAvatar,
            "revealed with evidence",
            revealedBy: ConversationMessageReference.Create(3, ConversationParticipantRole.Datee));

        Assert.True(RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(DateeId, ConversationParticipantRole.Datee, dateeFactRevealedToPlayer)).Admitted);
        Assert.True(RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(PlayerId, ConversationParticipantRole.PlayerAvatar, dateeFactRevealedToPlayer)).Admitted);
        Assert.False(RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(PlayerId, ConversationParticipantRole.Datee, dateeFactRevealedToPlayer)).Admitted);
    }

    [Theory]
    [InlineData(0, PromptFactVisibility.PrivateToSubject, PromptFactSourceKind.Backstory, "text")]
    [InlineData(1, (PromptFactVisibility)0, PromptFactSourceKind.Backstory, "text")]
    [InlineData(1, PromptFactVisibility.PrivateToSubject, (PromptFactSourceKind)0, "text")]
    [InlineData(1, PromptFactVisibility.PrivateToSubject, PromptFactSourceKind.Backstory, " ")]
    [InlineData(1, PromptFactVisibility.PrivateToSubject, PromptFactSourceKind.Backstory, "{stem_text}")]
    [InlineData(1, PromptFactVisibility.PrivateToSubject, PromptFactSourceKind.Backstory, "prefix {another_token} suffix")]
    [InlineData(1, PromptFactVisibility.PrivateToSubject, PromptFactSourceKind.Backstory, "resolved STEM text")]
    public void MalformedFactsFailClosed(int schemaVersion, PromptFactVisibility visibility, PromptFactSourceKind sourceKind, string text)
    {
        Assert.Throws<RoleFactContractException>(() => new OwnedPromptFactV1(
            PlayerId,
            ConversationParticipantRole.PlayerAvatar,
            visibility,
            sourceKind,
            PromptFactSourceIds.Backstory(PlayerId, "bio", "lie"),
            text,
            schemaVersion: schemaVersion));
    }

    [Fact]
    public void SourceKindMustMatchParsedSourceId()
    {
        Assert.Throws<RoleFactContractException>(() => new OwnedPromptFactV1(
            PlayerId,
            ConversationParticipantRole.PlayerAvatar,
            PromptFactVisibility.PrivateToSubject,
            PromptFactSourceKind.Diagnosis,
            PromptFactSourceIds.Backstory(PlayerId, "bio", "lie"),
            "text"));
    }

    [Fact]
    public void MalformedIdsRolesAndRequestsFailClosed()
    {
        var fact = NewFact(PlayerId, ConversationParticipantRole.PlayerAvatar, PromptFactVisibility.PrivateToSubject, "valid");

        Assert.Throws<RoleFactContractException>(() => NewFact(Guid.Empty, ConversationParticipantRole.PlayerAvatar, PromptFactVisibility.PrivateToSubject, "valid"));
        Assert.Throws<RoleFactContractException>(() => NewFact(PlayerId, (ConversationParticipantRole)0, PromptFactVisibility.PrivateToSubject, "valid"));
        Assert.Throws<RoleFactContractException>(() => new RoleFactAccessRequest(Guid.Empty, ConversationParticipantRole.PlayerAvatar, fact));
        Assert.Throws<RoleFactContractException>(() => new RoleFactAccessRequest(PlayerId, (ConversationParticipantRole)0, fact));
        Assert.Throws<ArgumentNullException>(() => new RoleFactAccessRequest(PlayerId, ConversationParticipantRole.PlayerAvatar, null!));
    }

    [Fact]
    public void DecisionSerializationContainsProvenanceButNotPrivateText()
    {
        var fact = NewFact(PlayerId, ConversationParticipantRole.PlayerAvatar, PromptFactVisibility.PrivateToSubject, SecretSentinel);
        RoleFactAccessDecision decision = RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(DateeId, ConversationParticipantRole.Datee, fact));

        string json = JsonSerializer.Serialize(decision);

        Assert.Contains("denied.private_to_subject", json);
        Assert.Contains(fact.SourceId, json);
        Assert.DoesNotContain(SecretSentinel, json);
        Assert.DoesNotContain("sleeping bag", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceIdsRejectPrivateProseBeforeItCanReachDiagnostics()
    {
        const string privateSource = "Gerald keeps a GBP 70 Soho silk sleeping bag hidden in plain sight.";

        Assert.Throws<RoleFactContractException>(() => PromptFactSourceIds.Backstory(
            PlayerId,
            privateSource,
            "bio_lie"));

        Assert.Throws<RoleFactContractException>(() => PromptFactSourceId.Parse(privateSource));
    }

    [Theory]
    [InlineData("Gerald revealed the sleeping bag during turn three.")]
    [InlineData("conversation:turn:3:Datee")]
    [InlineData("conversation:turn:03:DATEE")]
    [InlineData(" conversation:turn:3:DATEE")]
    [InlineData("conversation:turn:-1:DATEE")]
    [InlineData("conversation:turn:3:PLAYER")]
    public void RevealedByRejectsArbitraryOrNonCanonicalEvidence(string revealedBy)
    {
        Assert.Throws<RoleFactContractException>(() => ConversationMessageReference.Parse(revealedBy));
    }

    [Fact]
    public void RoleExplicitTargetFactoriesRejectSwappedOwnership()
    {
        var avatarFact = new OwnedPromptFactV1(
            PlayerId,
            ConversationParticipantRole.PlayerAvatar,
            PromptFactVisibility.PrivateToSubject,
            PromptFactSourceKind.Backstory,
            PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie"),
            "avatar target");
        var dateeFact = new OwnedPromptFactV1(
            DateeId,
            ConversationParticipantRole.Datee,
            PromptFactVisibility.PrivateToSubject,
            PromptFactSourceKind.Backstory,
            PromptFactSourceIds.Backstory(DateeId, "age_and_demographics", "bio_lie"),
            "datee target");

        Assert.Equal(avatarFact, AvatarRevelationTarget.Create(PlayerId, avatarFact).Fact);
        Assert.Equal(dateeFact, DateeReactionTarget.Create(DateeId, dateeFact).Fact);

        RoleFactContractException avatarRoleError = Assert.Throws<RoleFactContractException>(() => AvatarRevelationTarget.Create(PlayerId, dateeFact));
        RoleFactContractException dateeRoleError = Assert.Throws<RoleFactContractException>(() => DateeReactionTarget.Create(DateeId, avatarFact));
        RoleFactContractException avatarIdError = Assert.Throws<RoleFactContractException>(() => AvatarRevelationTarget.Create(DateeId, avatarFact));
        RoleFactContractException dateeIdError = Assert.Throws<RoleFactContractException>(() => DateeReactionTarget.Create(PlayerId, dateeFact));

        Assert.Equal("target.subject_role_mismatch", avatarRoleError.Code);
        Assert.Equal("target.subject_role_mismatch", dateeRoleError.Code);
        Assert.Equal("target.subject_character_mismatch", avatarIdError.Code);
        Assert.Equal("target.subject_character_mismatch", dateeIdError.Code);
    }

    [Fact]
    public void LegacyResolvedTargetConversionAdmitsCorrectlyOwnedAvatarTarget()
    {
        var legacy = new ResolvedRevelationTarget
        {
            Registry = EmotionStemSelectionRules.BackstoryRegistry,
            Index = 0,
            Field = "BIO_LIE",
            Manner = "CURATED_BUFFER",
            StemText = SecretSentinel,
            TransitionStyle = "soft"
        };

        AvatarRevelationTarget target = AvatarRevelationTarget.FromLegacyResolvedTarget(
            legacy,
            PlayerId,
            PlayerId,
            ConversationParticipantRole.PlayerAvatar,
            PromptFactVisibility.PrivateToSubject,
            PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie"));

        Assert.Equal(PlayerId, target.SubjectCharacterId);
        Assert.Equal(ConversationParticipantRole.PlayerAvatar, target.Fact.SubjectRole);
        Assert.Equal(PromptFactVisibility.PrivateToSubject, target.Fact.Visibility);
        Assert.Equal(PromptFactSourceKind.Backstory, target.Fact.SourceKind);
        Assert.Equal($"character:{PlayerId}:backstory:age_and_demographics:bio_lie", target.SourceId);
        Assert.Equal(SecretSentinel, target.Text);
        Assert.Null(target.Fact.RevealedBy);
    }

    [Fact]
    public void LegacyResolvedTargetConversionAdmitsCorrectlyOwnedDateeTarget()
    {
        var legacy = new ResolvedRevelationTarget
        {
            Registry = EmotionStemSelectionRules.StakeRegistry,
            Index = 2,
            Field = "STAKE_LINE",
            Manner = "INTIMATE_BREAKTHROUGH",
            StemText = "Datee protects tenderness by staying theatrical.",
            TransitionStyle = "direct"
        };
        ConversationMessageReference revealedBy = ConversationMessageReference.Create(
            7,
            ConversationParticipantRole.Datee);

        DateeReactionTarget target = DateeReactionTarget.FromLegacyResolvedTarget(
            legacy,
            DateeId,
            PlayerId,
            ConversationParticipantRole.PlayerAvatar,
            PromptFactVisibility.RevealedToPlayerAvatar,
            PromptFactSourceIds.PsychologicalStake(DateeId, 2),
            revealedBy);

        Assert.Equal(DateeId, target.SubjectCharacterId);
        Assert.Equal(ConversationParticipantRole.Datee, target.Fact.SubjectRole);
        Assert.Equal(PromptFactVisibility.RevealedToPlayerAvatar, target.Fact.Visibility);
        Assert.Equal(PromptFactSourceKind.PsychologicalStake, target.Fact.SourceKind);
        Assert.Equal($"character:{DateeId}:stake:2", target.SourceId);
        Assert.Equal("conversation:turn:7:DATEE", target.Fact.RevealedBy);
        Assert.Equal("Datee protects tenderness by staying theatrical.", target.Text);
    }

    [Fact]
    public void LegacyResolvedTargetConversionFailsClosedForWrongRecipientRoleBeforePromptTargetEscapes()
    {
        var legacy = new ResolvedRevelationTarget
        {
            Registry = EmotionStemSelectionRules.BackstoryRegistry,
            Index = 0,
            Field = "BIO_LIE",
            Manner = "CURATED_BUFFER",
            StemText = SecretSentinel,
            TransitionStyle = "soft"
        };

        RoleFactContractException error = Assert.Throws<RoleFactContractException>(() =>
            AvatarRevelationTarget.FromLegacyResolvedTarget(
                legacy,
                PlayerId,
                DateeId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie")));

        Assert.Equal("target.access_denied", error.Code);
        Assert.DoesNotContain(SecretSentinel, error.Message);
    }

    [Fact]
    public void LegacyResolvedTargetConversionFailsClosedForStakeIndexMismatch()
    {
        var legacy = new ResolvedRevelationTarget
        {
            Registry = EmotionStemSelectionRules.StakeRegistry,
            Index = 2,
            Field = "STAKE_LINE",
            Manner = "INTIMATE_BREAKTHROUGH",
            StemText = SecretSentinel,
            TransitionStyle = "direct"
        };

        RoleFactContractException error = Assert.Throws<RoleFactContractException>(() =>
            DateeReactionTarget.FromLegacyResolvedTarget(
                legacy,
                DateeId,
                PlayerId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.Public,
                PromptFactSourceIds.PsychologicalStake(DateeId, 3)));

        Assert.Equal("target.source_id.index_mismatch", error.Code);
        Assert.DoesNotContain(SecretSentinel, error.Message);
    }

    [Fact]
    public void LegacyResolvedTargetConversionFailsClosedForBackstoryCategoryOrFieldMismatch()
    {
        var legacy = new ResolvedRevelationTarget
        {
            Registry = EmotionStemSelectionRules.BackstoryRegistry,
            Index = 0,
            Field = "BIO_LIE",
            Manner = "CURATED_BUFFER",
            StemText = SecretSentinel,
            TransitionStyle = "soft"
        };

        RoleFactContractException categoryError = Assert.Throws<RoleFactContractException>(() =>
            AvatarRevelationTarget.FromLegacyResolvedTarget(
                legacy,
                PlayerId,
                PlayerId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceIds.Backstory(PlayerId, "birthplace_and_origin", "bio_lie")));
        RoleFactContractException fieldError = Assert.Throws<RoleFactContractException>(() =>
            AvatarRevelationTarget.FromLegacyResolvedTarget(
                legacy,
                PlayerId,
                PlayerId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "tragic_reality")));

        Assert.Equal("target.source_id.category_mismatch", categoryError.Code);
        Assert.Equal("target.source_id.field_mismatch", fieldError.Code);
        Assert.DoesNotContain(SecretSentinel, categoryError.Message);
        Assert.DoesNotContain(SecretSentinel, fieldError.Message);
    }

    [Fact]
    public void LegacyResolvedTargetConversionFailsClosedForMalformedOwnerVisibilityOrSourceMetadata()
    {
        var legacy = new ResolvedRevelationTarget
        {
            Registry = EmotionStemSelectionRules.BackstoryRegistry,
            Index = 0,
            Field = "BIO_LIE",
            Manner = "CURATED_BUFFER",
            StemText = SecretSentinel,
            TransitionStyle = "soft"
        };

        Assert.Throws<RoleFactContractException>(() => AvatarRevelationTarget.FromLegacyResolvedTarget(
            legacy, Guid.Empty, PlayerId, ConversationParticipantRole.PlayerAvatar, PromptFactVisibility.PrivateToSubject, PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie")));
        Assert.Throws<RoleFactContractException>(() => AvatarRevelationTarget.FromLegacyResolvedTarget(
            legacy, PlayerId, Guid.Empty, ConversationParticipantRole.PlayerAvatar, PromptFactVisibility.PrivateToSubject, PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie")));
        Assert.Throws<RoleFactContractException>(() => AvatarRevelationTarget.FromLegacyResolvedTarget(
            legacy, PlayerId, PlayerId, ConversationParticipantRole.PlayerAvatar, (PromptFactVisibility)0, PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie")));
        Assert.Throws<RoleFactContractException>(() => AvatarRevelationTarget.FromLegacyResolvedTarget(
            legacy, PlayerId, PlayerId, ConversationParticipantRole.PlayerAvatar, PromptFactVisibility.PrivateToSubject, PromptFactSourceIds.PsychologicalStake(PlayerId, 0)));
        Assert.Throws<RoleFactContractException>(() => AvatarRevelationTarget.FromLegacyResolvedTarget(
            legacy, PlayerId, PlayerId, ConversationParticipantRole.PlayerAvatar, PromptFactVisibility.PrivateToSubject, PromptFactSourceIds.Backstory(DateeId, "age_and_demographics", "bio_lie")));
        Assert.Throws<RoleFactContractException>(() => AvatarRevelationTarget.FromLegacyResolvedTarget(
            legacy, PlayerId, PlayerId, ConversationParticipantRole.PlayerAvatar, PromptFactVisibility.RevealedToDatee, PromptFactSourceIds.Backstory(PlayerId, "age_and_demographics", "bio_lie")));
    }

    [Fact]
    public void ContractDocumentationForbidsParallelPromptAdapterFiltersAndProviderDependencies()
    {
        string root = RepoRoot();
        string docs = File.ReadAllText(Path.Combine(root, "docs", "role-fact-access-policy.md"));
        string[] contractFiles =
        {
            "PromptFactContracts.cs",
            "PromptFactSourceIds.cs",
            "PromptFactReferences.cs",
            "RoleExplicitPromptTargets.cs",
            "RoleFactAccessDecision.cs",
            "RoleFactAccessPolicy.cs",
            "RoleFactAccessRequest.cs",
            "RoleFactContractException.cs",
        };

        Assert.Contains("Prompt adapters must construct `OwnedPromptFactV1`", docs);
        Assert.Contains("must not create parallel ad hoc visibility filters", docs);

        foreach (string file in contractFiles)
        {
            string source = File.ReadAllText(Path.Combine(root, "src", "Pinder.Core", "Conversation", file));
            Assert.DoesNotContain("Pinder.LlmAdapters", source);
            Assert.DoesNotContain("OpenRouter", source);
            Assert.DoesNotContain("Anthropic", source);
            Assert.DoesNotContain("Gemini", source);
            Assert.DoesNotContain("HttpClient", source);
        }
    }

    [Fact]
    public void SourceIdBuildersReturnStableContentFreeProvenanceKeys()
    {
        Assert.Equal($"character:{PlayerId}:backstory:truth:tragic_reality", PromptFactSourceIds.Backstory(PlayerId, "truth", "tragic_reality").Value);
        Assert.Equal($"character:{PlayerId}:stake:2", PromptFactSourceIds.PsychologicalStake(PlayerId, 2).Value);
        Assert.Equal($"character:{DateeId}:diagnosis:repair_requirement", PromptFactSourceIds.Diagnosis(DateeId, "repair_requirement").Value);
        Assert.Equal($"character:{DateeId}:cognitive-subtext:4", PromptFactSourceIds.CognitiveSubtext(DateeId, 4).Value);
        Assert.Equal("conversation:turn:5:PLAYER_AVATAR", PromptFactSourceIds.VisibleMessage(5, ConversationParticipantRole.PlayerAvatar).Value);
        Assert.Equal("engine:authored-target:6:option_c", PromptFactSourceIds.AuthoredTransitionTarget(6, "option_c").Value);
    }

    [Fact]
    public void SourceIdBuildersRejectMalformedSegments()
    {
        Assert.Throws<RoleFactContractException>(() => PromptFactSourceIds.Backstory(Guid.Empty, "bio", "lie"));
        Assert.Throws<RoleFactContractException>(() => PromptFactSourceIds.PsychologicalStake(PlayerId, -1));
        Assert.Throws<RoleFactContractException>(() => PromptFactSourceIds.Backstory(PlayerId, "bio:{stem_text}", "lie"));
        Assert.Throws<RoleFactContractException>(() => PromptFactSourceIds.Diagnosis(PlayerId, "repair:requirement"));
        Assert.Throws<RoleFactContractException>(() => PromptFactSourceId.Parse("character:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa:backstory:sleeping bag:lie"));
        Assert.Throws<RoleFactContractException>(() => PromptFactSourceIds.VisibleMessage(1, (ConversationParticipantRole)0));
    }


    [Fact]
    public void DialogueAndDateeContextsThrowTypedTextFreeDenialAndEmitJournalDiagnostic()
    {
        var avatarFact = NewFact(PlayerId, ConversationParticipantRole.PlayerAvatar, PromptFactVisibility.PrivateToSubject, SecretSentinel);
        var dateeFact = NewFact(DateeId, ConversationParticipantRole.Datee, PromptFactVisibility.PrivateToSubject, "datee private fact sentinel");
        var diagnostics = new List<OperationalDiagnosticEvent>();

        RoleFactAccessDeniedException avatarDenial = Assert.Throws<RoleFactAccessDeniedException>(() => new DialogueContext(
            playerAvatarPrompt: "avatar prompt",
            dateePrompt: "datee prompt",
            conversationHistory: Array.Empty<(string Sender, string Text)>(),
            dateeLastMessage: "",
            activeTraps: Array.Empty<string>(),
            currentInterest: 0,
            cognitiveSubtextFact: dateeFact,
            recipientCharacterId: PlayerId,
            onDiagnostic: diagnostics.Add));
        RoleFactAccessDeniedException dateeDenial = Assert.Throws<RoleFactAccessDeniedException>(() => new DateeContext(
            dateePrompt: "datee prompt",
            conversationHistory: Array.Empty<(string Sender, string Text)>(),
            dateeLastMessage: "",
            activeTraps: Array.Empty<string>(),
            currentInterest: 0,
            playerDeliveredMessage: "hello",
            interestBefore: 0,
            interestAfter: 0,
            responseDelayMinutes: 0,
            cognitiveSubtextFact: avatarFact,
            recipientCharacterId: DateeId,
            onDiagnostic: diagnostics.Add));

        Assert.Equal("prompt_fact.access_denied", avatarDenial.Code);
        Assert.Equal("denied.private_to_subject", avatarDenial.Decision.Code);
        Assert.Equal(PromptFactSourceKind.Backstory, avatarDenial.Decision.FactSourceKind);
        Assert.Equal(PromptFactSourceKind.Backstory, dateeDenial.Decision.FactSourceKind);
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(AgentJournalOperationalDiagnostics.RoleFactAccessRejectedEventName, diagnostic.EventName);
            Assert.Equal(OperationalDiagnosticOutcome.Failed, diagnostic.Outcome);
            Assert.Equal(AgentJournalOperationalDiagnostics.RoleFactAccessPhaseCode, diagnostic.PhaseCode);
        });
        string serialized = System.Text.Json.JsonSerializer.Serialize(new
        {
            AvatarDecision = avatarDenial.Decision,
            DateeDecision = dateeDenial.Decision,
            Diagnostics = diagnostics.Select(diagnostic => new { diagnostic.Message, diagnostic.CorrelationHints }),
        });
        Assert.DoesNotContain(SecretSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("datee private fact sentinel", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretSentinel, avatarDenial.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptContextsRejectRawFallbacksAndTypedFactsWithoutRecipientIdentity()
    {
        var diagnostics = new List<OperationalDiagnosticEvent>();
        OwnedPromptFactV1 typedFact = NewFact(
            DateeId,
            ConversationParticipantRole.Datee,
            PromptFactVisibility.PrivateToSubject,
            SecretSentinel);
        RoleFactContractException raw = Assert.Throws<RoleFactContractException>(() => new DialogueContext(
            playerAvatarPrompt: "avatar",
            dateePrompt: "datee",
            conversationHistory: Array.Empty<(string Sender, string Text)>(),
            dateeLastMessage: "",
            activeTraps: Array.Empty<string>(),
            currentInterest: 0,
            cognitiveSubtext: SecretSentinel,
            onDiagnostic: diagnostics.Add));
        RoleFactContractException missingIdentity = Assert.Throws<RoleFactContractException>(() => new DateeContext(
            dateePrompt: "datee",
            conversationHistory: Array.Empty<(string Sender, string Text)>(),
            dateeLastMessage: "",
            activeTraps: Array.Empty<string>(),
            currentInterest: 0,
            playerDeliveredMessage: "hello",
            interestBefore: 0,
            interestAfter: 0,
            responseDelayMinutes: 0,
            cognitiveSubtextFact: typedFact,
            onDiagnostic: diagnostics.Add));

        Assert.Equal("prompt_fact.raw_fallback_forbidden", raw.Code);
        Assert.Equal("prompt_fact.recipient_character_id.required", missingIdentity.Code);
        Assert.DoesNotContain(SecretSentinel, raw.Message);
        Assert.DoesNotContain(SecretSentinel, missingIdentity.Message);
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(AgentJournalOperationalDiagnostics.RoleFactContractRejectedEventName, diagnostic.EventName);
            Assert.Equal(OperationalDiagnosticOutcome.Failed, diagnostic.Outcome);
            Assert.Equal(AgentJournalOperationalDiagnostics.RoleFactAccessPhaseCode, diagnostic.PhaseCode);
        });
        OperationalDiagnosticEvent missingRecipientDiagnostic = diagnostics[1];
        Assert.Equal(typedFact.SourceId, missingRecipientDiagnostic.CorrelationHints["fact_source_id"]);
        Assert.Equal(typedFact.SourceKind.ToString(), missingRecipientDiagnostic.CorrelationHints["fact_source_kind"]);
        Assert.Equal(typedFact.SubjectCharacterId.ToString("D"), missingRecipientDiagnostic.CorrelationHints["owner_character_id"]);
        Assert.Equal(typedFact.SubjectRole.ToString(), missingRecipientDiagnostic.CorrelationHints["owner_role"]);
        string diagnosticJson = System.Text.Json.JsonSerializer.Serialize(diagnostics.Select(
            diagnostic => new { diagnostic.Message, diagnostic.CorrelationHints }));
        Assert.DoesNotContain(SecretSentinel, diagnosticJson, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Pinder.Core.sln")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Pinder.Core.sln from test output directory.");
    }

    private static OwnedPromptFactV1 NewFact(
        Guid subjectId,
        ConversationParticipantRole subjectRole,
        PromptFactVisibility visibility,
        string text,
        ConversationMessageReference? revealedBy = null)
        => new OwnedPromptFactV1(
            subjectId,
            subjectRole,
            visibility,
            PromptFactSourceKind.Backstory,
            PromptFactSourceIds.Backstory(subjectId == Guid.Empty ? PlayerId : subjectId, "bio", "lie"),
            text,
            revealedBy);

    private static bool ExpectedAdmission(
        Guid subjectId,
        ConversationParticipantRole subjectRole,
        PromptFactVisibility visibility,
        Guid recipientId,
        ConversationParticipantRole recipientRole)
    {
        if (recipientId == subjectId)
        {
            return recipientRole == subjectRole;
        }

        if (recipientRole == subjectRole)
        {
            return false;
        }

        if (visibility == PromptFactVisibility.Public)
        {
            return true;
        }

        if (visibility == PromptFactVisibility.RevealedToPlayerAvatar)
        {
            return recipientRole == ConversationParticipantRole.PlayerAvatar;
        }

        if (visibility == PromptFactVisibility.RevealedToDatee)
        {
            return recipientRole == ConversationParticipantRole.Datee;
        }

        return false;
    }

    private static ConversationMessageReference? EvidenceFor(PromptFactVisibility visibility)
        => visibility == PromptFactVisibility.RevealedToPlayerAvatar || visibility == PromptFactVisibility.RevealedToDatee
            ? ConversationMessageReference.Create(2, ConversationParticipantRole.PlayerAvatar)
            : null;

    private static Guid IdFor(ConversationParticipantRole role)
        => role == ConversationParticipantRole.PlayerAvatar ? PlayerId : DateeId;

    private static ConversationParticipantRole Other(ConversationParticipantRole role)
        => role == ConversationParticipantRole.PlayerAvatar
            ? ConversationParticipantRole.Datee
            : ConversationParticipantRole.PlayerAvatar;
}
