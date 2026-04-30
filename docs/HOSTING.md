# Hosting Pinder.Core (Unity / non-web)

This document is for engine hosts — Unity, a custom desktop client, a CLI, anything that wants to drive Pinder gameplay without going through `pinder-web` (FastAPI + React).

If you are working on the existing web stack, you do not need this file — `pinder-web` already wraps Pinder.Core for you.

---

## 1. What Pinder.Core gives you

Pinder.Core is a **headless, deterministic-when-seeded RPG engine** that resolves dating-app dialogue turns. It owns:

- The roll engine (d20, success/fail tiers, risk tiers)
- Stat + shadow stat tracking
- Trap activation / persistence / disarm
- Horniness check + per-tier overlay
- Combo / callback / Tell mechanics
- Interest meter + win/loss state
- The full per-turn pipeline (intended message → success/fail rewrite → trap overlay → horniness overlay → shadow corruption → opponent reply)

It does **not** own:

- Rendering (chat bubbles, character sheets, animations) — that's the host's job.
- Asset binding (3D models, textures, sounds) — see §5.
- Persistence (saving/loading sessions) — the host owns the storage; engine exposes snapshot/restore.
- LLM connectivity — the engine calls an `ILlmAdapter` you provide.

All four assemblies target **netstandard2.0**. They run inside Unity's Mono / IL2CPP runtime without modification.

---

## 2. Adding Pinder.Core to a Unity project

You have three options. Pick by how much you want to track upstream changes vs. ship a stable build.

### Option A — Pre-built DLLs (lowest friction)

1. From the `pinder-core` repo, build for netstandard2.0:
   ```bash
   dotnet build src/Pinder.Core/Pinder.Core.csproj -c Release
   dotnet build src/Pinder.Rules/Pinder.Rules.csproj -c Release
   dotnet build src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj -c Release
   dotnet build src/Pinder.SessionSetup/Pinder.SessionSetup.csproj -c Release
   ```
2. Copy the resulting DLLs (and `YamlDotNet.dll`, `Newtonsoft.Json.dll` from the build output) into `Assets/Plugins/PinderCore/` in your Unity project.
3. In Unity, select each DLL in the inspector and confirm: *Any Platform* → *Editor* and *Standalone* enabled. *Auto Reference* = on.
4. Drop the contents of `pinder-core/data/` into `Assets/StreamingAssets/PinderData/` so they ship with the build and are readable at runtime via `Application.streamingAssetsPath`.

### Option B — Git submodule + source import (best for active development)

1. Add `pinder-core` as a git submodule under `Assets/PinderCore/`:
   ```
   git submodule add https://github.com/decay256/pinder-core Assets/PinderCore
   ```
2. Create `.asmdef` files inside Unity for each Pinder assembly so the C# compiler builds them as separate assemblies (mirrors the `.csproj` boundaries — keeps the netstandard layering).
3. Pull `data/` separately into `StreamingAssets/PinderData/` (Unity will not pick up data files automatically from the submodule).
4. Track upstream by `git submodule update --remote` when you want a new version.

This is what `pinder-web` does — it mounts pinder-core as a git submodule.

### Option C — UPM package

If you want clean versioning and you control the package registry, wrap the four DLLs + the data folder + a `package.json` into a Unity package. Out of scope here, but viable.

### Verifying it compiled

Add this to a temporary `MonoBehaviour` in any scene:

```csharp
using Pinder.Core.Stats;
using UnityEngine;

public class PinderSmokeTest : MonoBehaviour
{
    void Start()
    {
        var stats = new StatBlock(
            new System.Collections.Generic.Dictionary<StatType, int>{ {StatType.Charm, 3} },
            new System.Collections.Generic.Dictionary<ShadowStatType, int>());
        Debug.Log($"Pinder OK. Charm = {stats.GetEffective(StatType.Charm)}");
    }
}
```

If that prints `Pinder OK. Charm = 3`, the DLLs are loaded.

---

## 3. Wiring a session — minimal example

The engine's main entry point is `Pinder.Core.Conversation.GameSession`. Here is the smallest viable host wiring, modeled on `session-runner/Program.cs` but stripped of CLI scaffolding.

```csharp
using System.IO;
using Pinder.Core.Conversation;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;
using Pinder.Rules;
using Pinder.SessionSetup;

string dataRoot = Path.Combine(Application.streamingAssetsPath, "PinderData");

// 1. Load static config (do this once at boot, share across sessions)
IItemRepository    items    = new JsonItemRepository(File.ReadAllText(Path.Combine(dataRoot, "items/starter-items.json")));
IAnatomyRepository anatomy  = new JsonAnatomyRepository(File.ReadAllText(Path.Combine(dataRoot, "anatomy/anatomy-parameters.json")));
// JsonTrapRepository implements ITrapRegistry. (session-runner has a small
// helper called TrapRegistryLoader for CLI fallbacks; replicate or skip it.)
ITrapRegistry      traps    = new JsonTrapRepository(File.ReadAllText(Path.Combine(dataRoot, "traps/traps.json")));
GameDefinition     gameDef  = GameDefinition.LoadFrom(File.ReadAllText(Path.Combine(dataRoot, "game-definition.yaml")));
StatDeliveryInstructions deliveryInstr =
    StatDeliveryInstructions.LoadFrom(File.ReadAllText(Path.Combine(dataRoot, "delivery-instructions.yaml")));
// Note: the rules YAML lives under `pinder-core/rules/extracted/` in the repo,
// not under `data/`. Ship it alongside `data/` in your StreamingAssets layout
// and adjust this path to wherever you put it.
IRuleResolver rules = RuleBookResolver.FromYaml(
    File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "PinderRules/rules-v3-enriched.yaml")));

// 2. Build characters from JSON (player + opponent go through the same path).
//    CharacterDefinitionLoader.Load takes a *path*; .Parse takes a JSON string.
CharacterProfile player   = CharacterDefinitionLoader.Parse(
    File.ReadAllText(Path.Combine(dataRoot, "characters/marisol-tides.json")),
    items, anatomy);
CharacterProfile opponent = CharacterDefinitionLoader.Parse(
    File.ReadAllText(Path.Combine(dataRoot, "characters/velvet.json")),
    items, anatomy);

// 3. Choose an LLM adapter — Anthropic, OpenAI-compatible, or your own implementing ILlmAdapter.
//    Adapter constructors vary; check the adapter's source for the current signature.
ILlmAdapter llm = new Pinder.LlmAdapters.Anthropic.AnthropicLlmAdapter(
    System.Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
    "claude-sonnet-4-20250514",
    gameDef);

// 4. Build the session
var clock = new GameClock(
    startTime: System.DateTimeOffset.UtcNow,
    modifiers: new HorninessModifiers(morning: 3, afternoon: 0, evening: 2, overnight: 5),
    dailyEnergy: 10);
var config = new GameSessionConfig(
    clock: clock,
    rules: rules,
    statDeliveryInstructions: deliveryInstr);

var session = new GameSession(
    player, opponent, llm,
    new SystemRandomDiceRoller(seed: 42),  // or your own IDiceRoller for determinism
    traps, config);

// 5. Drive the loop.
//    Each turn: call StartTurnAsync to get the 3 dialogue options,
//    show them to the user, then call ResolveTurnAsync(index) on click.
TurnStart  start  = await session.StartTurnAsync();    // start.Options is the 3-option set
// ... render start.Options, await user input, get chosenIndex ...
TurnResult result = await session.ResolveTurnAsync(chosenIndex);
// result has: delivered message, opponent reply, roll detail, interest delta, traps, etc.
// Repeat until result.IsGameOver.
```

The host's job: call `StartTurnAsync`, render options, take user input, call `ResolveTurnAsync(index)`, render the `TurnResult`, repeat.

For the streaming variant (per-stage progress events as the LLM pipeline runs), use the `ResolveTurnAsync(int, IProgress<TurnProgressEvent>)` overload.

> **API caveat.** Constructor signatures and helper names drift over sprints. The example above was current at the time of writing; if a name no longer compiles, grep `session-runner/Program.cs` — it is the canonical, up-to-date host wiring.

---

## 4. The host contract — what your Unity layer must implement

Anything not provided by the engine is the host's responsibility. The minimum:

- **UI**
  - Render character sheets from `CharacterProfile` (stats, items, anatomy, fragments).
  - Render the conversation log (chat bubbles).
  - Present 3 dialogue options each turn from `StartTurnAsync().Options`.
  - Render the per-turn `TurnResult` — interest delta, roll detail, traps, shadow growth, opponent reply.
- **Input**
  - Map a click on an option button to the option's index, then call `ResolveTurnAsync(index)`.
- **Persistence**
  - Call `session.CreateSnapshot()` whenever you want to save. The returned `GameStateSnapshot` is JSON-serializable; persist it anywhere (PlayerPrefs, file, server).
  - On resume, build a fresh `GameSession` and call `session.RestoreState(resimulateData, trapRegistry)` before the first turn. The snapshot → `ResimulateData` mapping currently happens in `pinder-web` (`Pinder.GameApi`); replicate that wiring in your host or copy the helper.
- **Time**
  - Provide an `IGameClock`. The default `GameClock` is fine if you want simulated time. If you want real wall-clock time for slow-burn async play, implement your own `IGameClock` returning `DateTime.UtcNow`.
- **LLM**
  - Provide an `ILlmAdapter`. Use the shipped Anthropic / OpenAI adapters, or implement your own (e.g. wrap your studio's internal LLM gateway).

What you do **not** need to implement: roll math, stat assembly, trap rules, shadow thresholds, interest math, prompt assembly. All of that lives in pinder-core.

---

## 5. Asset binding — 3D / 2D models for items and anatomy

This is where Unity hosts diverge from web hosts. The engine **only knows ids and fragments**. It does not know about meshes, textures, or animations. You bind assets to ids in your host.

### Recommended pattern — `ScriptableObject` registries

Create two `ScriptableObject` registries that your project exposes in the Unity inspector:

```csharp
// Assets/PinderHost/Scripts/ItemAsset.cs
using UnityEngine;

[CreateAssetMenu(fileName = "ItemAsset", menuName = "Pinder/Item Asset")]
public class ItemAsset : ScriptableObject
{
    public string itemId;          // MUST match the item_id in starter-items.json
    public Sprite icon;            // shown in inventory + character sheet
    public GameObject worldPrefab; // shown on the character model
    public AudioClip equipSound;
    // …whatever else your art / audio stack needs
}
```

```csharp
// Assets/PinderHost/Scripts/AnatomyTierAsset.cs
[CreateAssetMenu(fileName = "AnatomyTierAsset", menuName = "Pinder/Anatomy Tier Asset")]
public class AnatomyTierAsset : ScriptableObject
{
    public string parameterId;   // MUST match anatomy parameter id (e.g. "length")
    public string tierId;        // MUST match the tier id (e.g. "short")
    public Mesh   mesh;
    public Texture2D texture;
    public Sprite pickerThumbnail;
}
```

Then a single registry per type:

```csharp
[CreateAssetMenu(fileName = "ItemRegistry", menuName = "Pinder/Item Registry")]
public class ItemRegistryAsset : ScriptableObject
{
    public ItemAsset[] items;

    public ItemAsset Lookup(string itemId) =>
        System.Array.Find(items, x => x.itemId == itemId);
}

[CreateAssetMenu(fileName = "AnatomyRegistry", menuName = "Pinder/Anatomy Registry")]
public class AnatomyRegistryAsset : ScriptableObject
{
    public AnatomyTierAsset[] tiers;

    public AnatomyTierAsset Lookup(string parameterId, string tierId) =>
        System.Array.Find(tiers, x => x.parameterId == parameterId && x.tierId == tierId);
}
```

### Wiring — at character spawn time

```csharp
void RenderCharacter(CharacterProfile profile)
{
    foreach (var itemId in profile.EquippedItemIds)
    {
        var asset = itemRegistry.Lookup(itemId);
        if (asset == null) {
            Debug.LogWarning($"No ItemAsset for {itemId} — engine knows about it but the host has no visual.");
            continue;
        }
        Instantiate(asset.worldPrefab, characterRoot);
    }

    foreach (var (paramId, tierId) in profile.AnatomySelections)
    {
        var asset = anatomyRegistry.Lookup(paramId, tierId);
        if (asset == null) continue;
        ApplyAnatomyMesh(asset.mesh, asset.texture);
    }
}
```

### Validation at editor time

Add an editor menu entry that checks every id in `data/items/starter-items.json` and `data/anatomy/anatomy-parameters.json` against the registries and logs any unbound id:

```csharp
#if UNITY_EDITOR
[UnityEditor.MenuItem("Pinder/Validate Asset Bindings")]
static void Validate()
{
    var items = new JsonItemRepository(File.ReadAllText("Assets/StreamingAssets/PinderData/items/starter-items.json"));
    foreach (var it in items.GetAll())
        if (FindItemRegistry().Lookup(it.ItemId) == null)
            Debug.LogError($"Item '{it.ItemId}' has no ItemAsset binding.");
    // … same for anatomy
}
#endif
```

This catches "I added a new item to the JSON but forgot to author the prefab" before runtime.

### What about new anatomy parameters?

Because anatomy parameters are data-driven (see [data-architecture.md §6](data-architecture.md#6-anatomy-parameter-extensibility)), adding a new parameter to `anatomy-parameters.json` immediately makes it visible to `IAnatomyRepository.GetAll()`. Your Unity-side anatomy picker UI should iterate `repo.GetAll()` rather than hardcode a list of parameters — that way it picks up new parameters automatically.

```csharp
foreach (var param in anatomyRepo.GetAll())
{
    var dropdown = SpawnDropdownFor(param.Name);
    foreach (var tier in param.Tiers)
        dropdown.Add(tier.Name, tier.Id);
}
```

Same principle for items: iterate `IItemRepository.GetAll()` and let the UI render whatever the JSON declares.

---

## 6. Adjusting the number and type of anatomy parameters

This is a content change, not a code change. See the canonical procedure in [data-architecture.md §6](data-architecture.md#6-anatomy-parameter-extensibility). The Unity-specific addenda:

1. **Append the parameter** to `data/anatomy/anatomy-parameters.json` (add to your `StreamingAssets/PinderData/anatomy/` copy too).
2. **Author the assets**: one `AnatomyTierAsset` per tier, with `parameterId` matching the new parameter id and `tierId` matching each tier id.
3. **Add them to the registry**: drag the new `AnatomyTierAsset`s into your `AnatomyRegistryAsset.tiers` array.
4. **Run editor validation**: ensure the registry covers every (parameterId, tierId) pair the JSON declares.
5. **Update characters** that should pick a tier on the new parameter — open `data/characters/<slug>.json`, add the `parameterId: tierId` line under `anatomy`.
6. The picker UI requires no code change if you iterate `anatomyRepo.GetAll()` (per §5).

To **remove** a parameter:

1. Delete it from `anatomy-parameters.json`.
2. Audit `data/characters/*.json` and remove any reference (the engine silently skips unknown ids, but your host's asset registry will warn on stale references — fix them up).
3. Optional: delete the now-orphaned `AnatomyTierAsset`s.

To **add or remove a tier on an existing parameter**:

Edit the parameter's `tiers[]` array. Same registry / character-cleanup steps.

There is no enum, no migration script, no engine recompile. The only hard rule: tier ids must be unique within their parameter.

---

## 7. Determinism and replay

If you want recorded sessions to replay byte-identically:

- Pass an explicit seed to `SystemRandomDiceRoller(seed: …)`.
- Use a deterministic `IGameClock` (e.g. one driven by `session.TurnNumber * fixedTimeStepMinutes`).
- Use a deterministic LLM adapter — production LLMs are non-deterministic, so for true replay you record the LLM responses to disk and use a `RecordedLlmAdapter` on playback. The web tier already uses this pattern for the audit log; see `Pinder.GameApi/Services/TurnAuditWriter.cs` and the planned replay epic in pinder-web for the canonical implementation.

---

## 8. Common pitfalls

- **`netstandard2.0` quirks** — Unity's IL2CPP supports it, but a few BCL APIs are missing. The pinder-core code is already constrained to the supported subset; any third-party library you wrap must also be netstandard2.0-compatible.
- **`StreamingAssets` on Android** — `File.ReadAllText` does **not** work on `Application.streamingAssetsPath` on Android (the path lives inside the APK). Use `UnityWebRequest` to fetch and cache to `Application.persistentDataPath` at startup, then point loaders at the cached path. iOS / desktop / WebGL have their own quirks; check the Unity manual page for "Streaming Assets" before shipping.
- **Asset binding drift** — running the editor validator on every PR (or at least pre-merge) prevents JSON-vs-asset drift accumulating silently.
- **Forgetting to ship `data/`** — the engine throws at startup if any of the singleton config files are missing. Bake `data/` into a build step.
- **Hot-reloading content during play** — pinder-core repositories are immutable after construction. To swap content, recreate the repository and start a new session. Mid-session content swaps are not supported.

---

## See also

- [`docs/ARCHITECTURE.md`](ARCHITECTURE.md) — assemblies, game loop, interfaces
- [`docs/data-architecture.md`](data-architecture.md) — full configuration map and extensibility model
- [`docs/modules/`](modules/) — per-module deep dives
- [`session-runner/Program.cs`](../session-runner/Program.cs) — the canonical CLI host. Read this before writing your own host; it shows the full wiring including snapshot/restore, deterministic seeding, and adapter selection.
