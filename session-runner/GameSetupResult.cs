using System;
using System.Text;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;
using Pinder.SessionRunner;

public class GameSetupResult
{
    public bool ShouldExit { get; set; }
    public int ExitCode { get; set; }

    public string? ApiKey { get; set; }
    public string? ResimulateSlug { get; set; }
    public bool IsResimulation { get; set; }
    public int FromTurn { get; set; }
    public int ResimOriginalSessionNum { get; set; }
    public List<string> AssumptionLog { get; set; } = new List<string>();

    public CharacterProfile Sable { get; set; }
    public CharacterProfile Brick { get; set; }

    public StringBuilder Buffer { get; set; } = new StringBuilder();
    public TeeWriter Tee { get; set; }

    public string Player1 { get; set; }
    public string Player2 { get; set; }
    public int P1Level { get; set; }
    public int P2Level { get; set; }
    public int P1LevelBonus { get; set; }
    public int P2LevelBonus { get; set; }
    public StatBlock SableStats { get; set; }
    public StatBlock BrickStats { get; set; }

    public string? PlaytestDir { get; set; }
    public int SessionNumber { get; set; }
    public string SessionSlug { get; set; }

    public GameDefinition? GameDef { get; set; }
    public int MaxTurns { get; set; }
    public string? DebugFile { get; set; }
    public string ModelSpec { get; set; }
    public string SetupModelSpec { get; set; }
    public string PlayerAgentModelSpec { get; set; }
    public string? OverlayModel { get; set; }
    public IStatefulLlmAdapter Llm { get; set; }
    public ITrapRegistry TrapRegistry { get; set; }
    public bool TrapsDisabled { get; set; }
    public I18nCatalog? SnapshotI18nCatalog { get; set; }
    public SessionShadowTracker SableShadows { get; set; }
    public GameClock Clock { get; set; }
    public GameSession Session { get; set; }
    public IPlayerAgent Agent { get; set; }
    public int Interest { get; set; }
    public int Momentum { get; set; }
}
