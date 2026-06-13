using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Pinder.SessionRunner;
using Pinder.SessionRunner.Snapshot;

partial class Program
{
    internal static InitialSessionSnapshot BuildInitialSnapshot(
        CharacterProfile player,
        CharacterProfile datee,
        int playerLevelBonus,
        int dateeLevelBonus,
        GameSession session,
        int startingInterest,
        int maxTurns,
        string modelSpec,
        int globalDcBias,
        int maxDialogueOptions)
    {
        return new InitialSessionSnapshot
        {
            Player = BuildCharacterSnapshot(player, playerLevelBonus),
            Datee = BuildCharacterSnapshot(datee, dateeLevelBonus),
            SessionHorniness = session.SessionHorniness,
            HorninessRoll = session.HorninessRoll,
            HorninessTimeModifier = session.HorninessTimeModifier,
            StartingInterest = startingInterest,
            MaxTurns = maxTurns,
            ModelSpec = modelSpec,
            SessionStartedAt = DateTime.UtcNow.ToString("o"),
            PlayerPsychologicalStake = player.PsychologicalStake ?? string.Empty,
            DateePsychologicalStake = datee.PsychologicalStake ?? string.Empty,
            GlobalDcBias = globalDcBias,
            MaxDialogueOptions = maxDialogueOptions,
        };
    }

    internal static CharacterSnapshot BuildCharacterSnapshot(CharacterProfile profile, int levelBonus)
    {
        var stats = new Dictionary<string, int>();
        foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            stats[stat.ToString()] = profile.Stats.GetBase(stat);

        return new CharacterSnapshot
        {
            DisplayName = profile.DisplayName,
            Level = profile.Level,
            LevelBonus = levelBonus,
            Stats = stats,
            Bio = profile.Bio,
            AssembledSystemPrompt = profile.AssembledSystemPrompt,
            EquippedItems = profile.EquippedItemDisplayNames.ToArray(),
        };
    }

    // internal so Pinder.Core.Tests can verify the conversationHistory ↔ perTurnTextDiffs
    // indexing contract (see Issue767BraceScopeTests).
    internal static TurnSnapshot BuildTurnSnapshot(
        int turnNumber,
        TurnResult result,
        SessionShadowTracker shadows,
        List<StatType> statsUsedHistory,
        List<bool> highestPctHistory,
        int charmUsageCount,
        bool charmMadnessTriggered,
        int saUsageCount,
        bool saOverthinkingTriggered,
        int rizzCumulativeFailureCount,
        List<(string Sender, string Text)> conversationHistory,
        List<(StatType Stat, bool Succeeded)> comboHistory,
        TellSnapshot? activeTell,
        List<List<TextDiffSnapshot>>? perTurnTextDiffs = null,
        IReadOnlyList<Pinder.Core.Conversation.ConversationMessage>? dateeHistory = null,
        string? playerSender = null,
        Pinder.LlmAdapters.I18nCatalog? i18nCatalog = null,
        Pinder.Core.Conversation.DateeDefenseSnapshot? dateeDefenseSnapshot = null,
        int? weaknessDcReduction = null)
    {
        var state = result.StateAfter;

        // Shadow values
        var shadowValues = new Dictionary<string, int>();
        foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
            shadowValues[shadow.ToString()] = shadows.GetEffectiveShadow(shadow);

        // Active traps from state
        var activeTraps = state.ActiveTrapDetails
            .Select(t => new TrapSnapshot { Id = t.Name, Stat = t.Stat, TurnsRemaining = t.TurnsRemaining })
            .ToList();

        // Last 3 turns of combo history
        var comboWindow = comboHistory
            .Skip(Math.Max(0, comboHistory.Count - 3))
            .Select(e => new TurnHistoryEntry { Stat = e.Stat.ToString(), Succeeded = e.Succeeded })
            .ToList();

        // Conversation history (#305: attach per-turn text_diffs[] to the
        // player's entry on each turn so the snapshot can be deserialised
        // straight into a renderer / replay tool.
        //
        // Indexing rule (post #769 / #774): the conversation log is no
        // longer a strict alternating (player, datee, ...) sequence —
        // it may be prefixed with [scene] entries (issue #333), and a
        // skipped player turn (empty DeliveredMessage, post-#767) means
        // a turn slot with no entry on the player axis at all. We use
        // ConversationIndexing.EnumerateConversation to walk the history
        // and classify each entry; perTurnTextDiffs is co-indexed by
        // count of player entries (turn N's diffs live at
        // perTurnTextDiffs[N-1]), which the helper hands us via the
        // 1-based TurnNumber on each player view.
        //
        // When playerSender is supplied (the live engine path always
        // passes it), we identify player entries directly by sender
        // equality and walk a player-entry counter to index into
        // perTurnTextDiffs. This is more robust than the helper's
        // pair-math classification under the #769 "skipped player
        // turn" perturbation: pair-math derives role from the count of
        // non-scene entries seen so far, which silently flips when
        // either side of a (player, datee) pair is missing, while
        // sender-match always identifies the right entry.
        //
        // When playerSender is null (legacy callers / older tests that
        // don't know the display name) we fall back to the helper's
        // IsPlayerEntry classification — correct under the strict
        // pair invariant the engine maintained pre-#769.
        var convEntries = new List<ConversationEntry>(conversationHistory.Count);
        int playerEntriesSeen = 0;
        foreach (var view in Pinder.Core.Conversation.ConversationIndexing.EnumerateConversation(conversationHistory))
        {
            var entry = new ConversationEntry { Sender = view.Sender, Text = view.Text };
            bool isPlayerByIdentity = playerSender != null
                ? (!view.IsScene && view.Sender == playerSender)
                : view.IsPlayerEntry;
            if (isPlayerByIdentity && perTurnTextDiffs != null)
            {
                int turnIdx = playerEntriesSeen;
                if (turnIdx >= 0 && turnIdx < perTurnTextDiffs.Count)
                {
                    entry.TextDiffs = perTurnTextDiffs[turnIdx] ?? new List<TextDiffSnapshot>();
                }
                playerEntriesSeen++;
            }
            convEntries.Add(entry);
        }

        // #788: project the engine-owned datee history into the wire shape.
        var dateeHistoryEntries = (dateeHistory ?? new List<Pinder.Core.Conversation.ConversationMessage>())
            .Select(m => new DateeHistoryEntry { Role = m.Role, Content = m.Content })
            .ToList();

        // Issue #474: detect canonical event kinds fired this turn and
        // resolve their interpretation strings via the (optional) i18n
        // catalog. Empty list when no event-class condition was met,
        // or when the catalog is null (sim runs without data/i18n).
        var eventKinds = TurnEventDetector.DetectEventKinds(result);
        var events = TurnEventInterpreter.Build(eventKinds, turnNumber, i18nCatalog);

        return new TurnSnapshot
        {
            TurnNumber = turnNumber,
            Interest = state.Interest,
            ShadowValues = shadowValues,
            MomentumStreak = state.MomentumStreak,
            ActiveTraps = activeTraps,
            ActiveTell = activeTell,
            ComboHistory = comboWindow,
            PendingTripleBonus = state.TripleBonusActive,
            StatsUsedHistory = statsUsedHistory.Select(s => s.ToString()).ToList(),
            HighestPctHistory = new List<bool>(highestPctHistory),
            CharmUsageCount = charmUsageCount,
            CharmMadnessTriggered = charmMadnessTriggered,
            SaUsageCount = saUsageCount,
            SaOverthinkingTriggered = saOverthinkingTriggered,
            RizzCumulativeFailureCount = rizzCumulativeFailureCount,
            ConversationHistory = convEntries,
            DateeHistory = dateeHistoryEntries,
            Events = events,
            DefendingStat = result.Roll.DefendingStat.ToString(),
            GhostProbabilityPerTurn = state.GhostProbabilityPerTurn,
            DateeDefenseSnapshot = dateeDefenseSnapshot != null
                ? dateeDefenseSnapshot.ByAttackerStat.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => new TurnDefenseEntry
                    {
                        DefendingStat     = kvp.Value.DefendingStat.ToString(),
                        EffectiveModifier = kvp.Value.EffectiveModifier,
                        BaseModifier      = kvp.Value.BaseModifier,
                    })
                : null,
            WeaknessDcReduction = weaknessDcReduction,
        };
    }

    internal static void WritePlaytestLog(string content, string p1, string p2, string? dir, int sessionNumber)
    {
        if (dir == null) { Console.Error.WriteLine("Playtest dir not found — set PINDER_PLAYTESTS_PATH or ensure design/playtests/ exists"); return; }
        string slug = $"session-{sessionNumber:D3}-{p1.ToLower()}-vs-{p2.ToLower()}.md";
        File.WriteAllText(Path.Combine(dir, slug), content);
        Console.WriteLine($"\n📝 Written → {dir}/{slug}");
    }

    internal static int FindLastTurnSnapshot(string playtestDir, string slug)
    {
        int last = 0;
        for (int i = 1; i <= 99; i++)
        {
            string path = Path.Combine(playtestDir, $"{slug}.turn-{i:D2}.snap.json");
            if (File.Exists(path))
                last = i;
            else if (last > 0)
                break; // stop scanning after finding a gap (assumes no gaps)
        }
        return last;
    }

    internal static int ParseSessionNumberFromSlug(string slug)
    {
        // Format: session-NNN-...
        if (slug.StartsWith("session-", StringComparison.OrdinalIgnoreCase))
        {
            string rest = slug.Substring("session-".Length);
            int dash = rest.IndexOf('-');
            string numStr = dash >= 0 ? rest.Substring(0, dash) : rest;
            if (int.TryParse(numStr, out int n))
                return n;
        }
        return 0;
    }

    internal static TurnSnapshot ValidateAndPatchTurnSnapshot(TurnSnapshot snap, List<string> log)
    {
        snap.ShadowValues ??= new Dictionary<string, int>();
        snap.ActiveTraps ??= new List<TrapSnapshot>();
        snap.ComboHistory ??= new List<TurnHistoryEntry>();
        snap.StatsUsedHistory ??= new List<string>();
        snap.HighestPctHistory ??= new List<bool>();
        snap.ConversationHistory ??= new List<ConversationEntry>();
        snap.DateeHistory ??= new List<DateeHistoryEntry>();

        void Assume(string field, string defaultValue)
        {
            string msg = $"[ASSUMPTION] {field} = {defaultValue} (not present in snapshot)";
            Console.Error.WriteLine(msg);
            log.Add(msg);
        }

        // Check required int fields for 0-default (can’t distinguish missing from explicit 0,
        // but log if the whole ShadowValues map is empty on a non-turn-1 snap)
        if (snap.TurnNumber == 0)
            Assume("TurnNumber", "0");
        if (snap.ShadowValues.Count == 0 && snap.TurnNumber > 0)
            Assume("ShadowValues", "(empty — all shadows assumed 0)");

        return snap;
    }

    internal static void WriteAssumptionLog(string playtestDir, string slug, List<string> assumptionLog)
    {
        if (assumptionLog.Count == 0) return;
        var sb = new StringBuilder();
        sb.AppendLine($"# Resimulation Assumptions — {slug}");
        sb.AppendLine();
        sb.AppendLine("The following fields were missing from the snapshot and replaced with defaults:");
        sb.AppendLine();
        foreach (var entry in assumptionLog)
            sb.AppendLine($"- {entry}");
        string path = Path.Combine(playtestDir, $"{slug}-resimulate-assumptions.md");
        File.WriteAllText(path, sb.ToString());
        Console.Error.WriteLine($"[ASSUMPTION LOG] Written → {path}");
    }

    internal static Pinder.Core.Conversation.ResimulateData BuildResimulateData(TurnSnapshot snap)
    {
        return new Pinder.Core.Conversation.ResimulateData
        {
            TargetInterest       = snap.Interest,
            TurnNumber           = snap.TurnNumber,
            MomentumStreak       = snap.MomentumStreak,
            ShadowValues         = snap.ShadowValues ?? new Dictionary<string, int>(),
            ActiveTraps          = (snap.ActiveTraps ?? new List<TrapSnapshot>())
                                     .Select(t => (t.Stat, t.TurnsRemaining))
                                     .ToList(),
            ConversationHistory  = (snap.ConversationHistory ?? new List<ConversationEntry>())
                                     .Select(e => (e.Sender, e.Text))
                                     .ToList(),
            ComboHistory         = (snap.ComboHistory ?? new List<TurnHistoryEntry>())
                                     .Select(e => (e.Stat, e.Succeeded))
                                     .ToList(),
            PendingTripleBonus   = snap.PendingTripleBonus,
            RizzCumulativeFailureCount = snap.RizzCumulativeFailureCount,
            DateeHistory      = (snap.DateeHistory ?? new List<DateeHistoryEntry>())
                                     .Select(e => (e.Role, e.Content))
                                     .ToList(),
        };
    }

    internal static CharacterProfile BuildProfileFromSnapshot(CharacterSnapshot charSnap)
    {
        var baseStats = new Dictionary<Pinder.Core.Stats.StatType, int>();
        foreach (var kvp in charSnap.Stats)
        {
            if (Enum.TryParse<Pinder.Core.Stats.StatType>(kvp.Key, out var statType))
                baseStats[statType] = kvp.Value;
        }
        var shadowStats = new Dictionary<Pinder.Core.Stats.ShadowStatType, int>();

        var statBlock = new Pinder.Core.Stats.StatBlock(baseStats, shadowStats);
        var timing = new Pinder.Core.Conversation.TimingProfile(
            baseDelay: 5, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");

        return new CharacterProfile(
            stats: statBlock,
            assembledSystemPrompt: charSnap.AssembledSystemPrompt,
            displayName: charSnap.DisplayName,
            timing: timing,
            level: charSnap.Level,
            bio: charSnap.Bio,
            textingStyleFragment: "",
            activeArchetype: null,
            equippedItemDisplayNames: charSnap.EquippedItems?.ToList() ?? new List<string>());
    }
}
