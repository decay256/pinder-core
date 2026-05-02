# Unity Integration Guide

How to drop `pinder-core` into a Unity project as the game engine, and
how to adapt it when your Unity content (anatomy params, items, stat
ranges, archetypes) doesn't map cleanly onto the defaults shipped in
`data/`.

> **Audience:** Unity developers integrating Pinder.Core for the first
> time. Assumes Unity 2022.3 LTS or newer (Mono / IL2CPP both
> supported). Assumes you've read [`README.md`](../README.md) and
> [`docs/ARCHITECTURE.md`](ARCHITECTURE.md).

> **Status:** Prototype. The integration patterns in this guide work
> against the current `main`; the public API surfaces (`ILlmAdapter`,
> `IAnatomyRepository`, `IItemRepository`, `GameSession`) are stable
> enough to consume but not yet versioned.

---

## 0. What you're integrating

`pinder-core` is a **pure C# class library**. There is no Unity
dependency, no `MonoBehaviour`, no `UnityEngine` reference. You are
adding up to four .NET assemblies to your project (skip
`Pinder.SessionSetup` if you wire characters yourself from
ScriptableObjects rather than JSON):

| Assembly | Target | Required NuGet | Purpose |
|---|---|---|---|
| `Pinder.Core.dll` | netstandard2.0 | `Microsoft.Bcl.AsyncInterfaces` | Game logic kernel + JSON repositories (`Pinder.Core.Data`) |
| `Pinder.Rules.dll` | netstandard2.0 | `YamlDotNet` | Data-driven mechanics |
| `Pinder.LlmAdapters.dll` | netstandard2.0 | `Newtonsoft.Json`, `YamlDotNet` | Prompt assembly + (optional) HTTP transports |
| `Pinder.SessionSetup.dll` | netstandard2.0 | `System.Text.Json` | High-level character JSON loader (`CharacterDefinitionLoader`) |

`pinder-web` is **not** part of the Unity integration — that's the
React/FastAPI presentation tier for the browser game. Skip it.

You write three things on the Unity side:

1. **An `ILlmAdapter` implementation** that calls your chosen LLM
   provider (or a stub for offline play / fixtures).
2. **Repositories or asset loaders** for character / anatomy / item
   data. Use the JSON files shipped in `data/` as a starting point, or
   substitute Unity-native sources (`ScriptableObject`, Addressables,
   Resources, etc.).
3. **A driver** — typically a `MonoBehaviour` or coroutine — that
   constructs a `GameSession`, calls `ResolveTurnAsync`, and feeds the
   results into your UI.

---

## 1. Step-by-step: get a turn running in Unity

### 1.1 Add the engine to your project

Pick one of the following.

**Option A — Source import (recommended during integration).**
Drop the `src/Pinder.Core/`, `src/Pinder.Rules/`, and
`src/Pinder.LlmAdapters/` folders into your Unity project under e.g.
`Assets/Plugins/PinderCore/`. Add an Asmdef per assembly so Unity
compiles them as separate libraries:

```
Assets/Plugins/PinderCore/
  Pinder.Core/
    Pinder.Core.asmdef         { "name": "Pinder.Core", "rootNamespace": "Pinder.Core",
                                 "allowUnsafeCode": false, "overrideReferences": true,
                                 "precompiledReferences": ["Microsoft.Bcl.AsyncInterfaces.dll"] }
    [all .cs files from src/Pinder.Core]
  Pinder.Rules/
    Pinder.Rules.asmdef        { "name": "Pinder.Rules", "references": ["Pinder.Core"],
                                 "precompiledReferences": ["YamlDotNet.dll"] }
    [all .cs files from src/Pinder.Rules]
  Pinder.LlmAdapters/
    Pinder.LlmAdapters.asmdef  { "name": "Pinder.LlmAdapters",
                                 "references": ["Pinder.Core", "Pinder.Rules"],
                                 "precompiledReferences": ["Newtonsoft.Json.dll", "YamlDotNet.dll",
                                                           "Microsoft.Bcl.AsyncInterfaces.dll"] }
    [all .cs files from src/Pinder.LlmAdapters]
  Pinder.SessionSetup/        # optional — only if you load characters from JSON
    Pinder.SessionSetup.asmdef { "name": "Pinder.SessionSetup",
                                 "references": ["Pinder.Core", "Pinder.LlmAdapters"],
                                 "precompiledReferences": ["System.Text.Json.dll"] }
    [all .cs files from src/Pinder.SessionSetup]
```

Drop the matching DLLs into `Assets/Plugins/Managed/`
(Microsoft.Bcl.AsyncInterfaces, Newtonsoft.Json, YamlDotNet, and
System.Text.Json if you take SessionSetup). Use `nuget.exe install`
against `Pinder.Core.csproj` to fetch the exact versions, then copy
the DLLs out of the resolved `packages/` folder. Versions to match
are:

```
Microsoft.Bcl.AsyncInterfaces  8.0.0
YamlDotNet                     16.3.0
Newtonsoft.Json                13.0.3
System.Text.Json               8.0.5    (only if you import Pinder.SessionSetup)
```

> Unity ships a built-in `Newtonsoft.Json` (`com.unity.nuget.newtonsoft-json`).
> If you already have it installed via Package Manager, **do not also
> drop a copy under `Plugins/`** — duplicate JsonConvert symbols will
> cause IL2CPP build errors. Set
> `precompiledReferences` to skip Newtonsoft.Json and let the package
> provide it.

**Option B — DLL import (recommended for a stable release pin).**
Build `dotnet build -c Release` against the engine's solution and
copy `Pinder.Core.dll`, `Pinder.Rules.dll`, `Pinder.LlmAdapters.dll`
into `Assets/Plugins/Managed/` along with their NuGet dependencies.
Faster Unity compile, no source visibility. Use this once your
adapter and repositories are stable.

### 1.2 Ship the data files

Copy the engine's `data/` directory into Unity's
**`Assets/StreamingAssets/PinderData/`**:

```
Assets/StreamingAssets/PinderData/
  characters/      ← *.json
  anatomy/         ← anatomy-parameters.json
  items/           ← starter-items.json
  traps/           ← traps.json + trap-schema.json
  i18n/en/         ← *.yaml
  delivery-instructions.yaml
  game-definition.yaml
  timing/          ← timing rules
```

Read them at runtime with `Application.streamingAssetsPath`. On
Android the streamingAssets path is inside the APK and reads must go
through `UnityWebRequest`; on standalone / iOS / Editor it's a file
path. Wrap the read site so you don't sprinkle platform `#if`s
everywhere. See §1.4 below for the canonical loader.

### 1.3 Implement `ILlmAdapter`

`pinder-core` does not call any LLM directly. It depends on the
abstraction `Pinder.Core.Interfaces.ILlmAdapter` (full surface in
[`src/Pinder.Core/Interfaces/ILlmAdapter.cs`](../src/Pinder.Core/Interfaces/ILlmAdapter.cs)):

```csharp
public interface ILlmAdapter
{
    Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext c, CancellationToken ct = default);
    Task<string>           DeliverMessageAsync   (DeliveryContext  c, CancellationToken ct = default);
    Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext c, CancellationToken ct = default);
    Task<string?>          GetInterestChangeBeatAsync(InterestChangeContext c, CancellationToken ct = default);
    Task<string>           ApplyHorninessOverlayAsync(string msg, string instruction, string? oc, string? ad, CancellationToken ct = default);
    Task<string>           ApplyShadowCorruptionAsync(string msg, string instruction, ShadowStatType s, string? ad, CancellationToken ct = default);
    Task<string>           ApplyTrapOverlayAsync     (string msg, string trapInstruction, string trapName, string? oc, string? ad, CancellationToken ct = default);
}
```

You have two choices:

**(a) Reuse `Pinder.LlmAdapters.PinderLlmAdapter`** — the production
`ILlmAdapter` implementation used by `pinder-web` and the
`session-runner` CLI. It owns prompt assembly, response parsing, and
overlay handling. You provide an `ILlmTransport` (the thin
provider-specific HTTP layer) plus a `PinderLlmAdapterOptions`. Two
transports ship in the box:

- `Pinder.LlmAdapters.OpenAi.OpenAiTransport` — OpenAI Chat
  Completions API. Constructor takes an `HttpClient`, an API key,
  and a model name.
- `Pinder.LlmAdapters.Anthropic.AnthropicTransport` — Anthropic
  Messages API. Same shape.

Wire `HttpClient` carefully on Unity — long-lived `HttpClient`
survives domain reloads if you parent it to a non-static container;
otherwise leak.

**(b) Write your own adapter.** Useful if you're routing through your
own backend (e.g. a Cloud Run proxy that holds the API key) or if
you're using a Unity-native LLM SDK. The cheap version: implement
`ILlmTransport` (one method, `SendAsync`) and let `PinderLlmAdapter`
do the prompt assembly. The heavy version: implement `ILlmAdapter`
directly (the seven methods above). The context/response shapes
(`DialogueContext`, `OpponentResponse`, etc.) are all in
`Pinder.Core.Conversation`. Keep it stateless across calls — the
engine owns conversation history, not the adapter.

For **offline play / unit tests** there's already a
`Pinder.Core.Conversation.NullLlmAdapter` that returns canned
responses. Use it as your bring-up adapter.

### 1.4 Wire the data repositories

The engine consumes anatomy and item data through two interfaces:

```csharp
public interface IAnatomyRepository {
    AnatomyParameterDefinition? GetParameter(string parameterId);
    IEnumerable<AnatomyParameterDefinition> GetAll();
}

public interface IItemRepository {
    ItemDefinition? GetItem(string itemId);
    IEnumerable<ItemDefinition> GetAll();
}
```

`Pinder.Core.Data` ships JSON-backed implementations
(`JsonAnatomyRepository`, `JsonItemRepository`, `JsonTrapRepository`)
inside the `Pinder.Core` assembly. All three take the **JSON string**
in their constructor — read the file yourself, then hand off:

```csharp
using Pinder.Core.Data;

var anatomyJson = File.ReadAllText(Path.Combine(
    Application.streamingAssetsPath, "PinderData/anatomy/anatomy-parameters.json"));
IAnatomyRepository anatomy = new JsonAnatomyRepository(anatomyJson);

var itemsJson = File.ReadAllText(Path.Combine(
    Application.streamingAssetsPath, "PinderData/items/starter-items.json"));
IItemRepository items = new JsonItemRepository(itemsJson);

var trapsJson = File.ReadAllText(Path.Combine(
    Application.streamingAssetsPath, "PinderData/traps/traps.json"));
ITrapRegistry traps = new JsonTrapRepository(trapsJson);
```

On **Android** you must read through `UnityWebRequest` first
(StreamingAssets is inside the APK / OBB), then construct the
repository from the in-memory string:

```csharp
public static async Task<IAnatomyRepository> LoadAnatomyAsync()
{
    var json = await ReadStreamingAssetTextAsync(
        "PinderData/anatomy/anatomy-parameters.json");
    return new JsonAnatomyRepository(json);
}
```

> **Item id casing.** `JsonItemRepository` uses `StringComparer.Ordinal`
> (case-sensitive), while `JsonAnatomyRepository` uses
> `StringComparer.OrdinalIgnoreCase`. Stick to lower-case kebab-case
> for ids in both files — it's the only convention that survives
> both stores. If you write your own `IItemRepository` over
> ScriptableObjects, choose case-insensitive lookup to be forgiving
> of content-team typos.

If you'd rather store anatomy / items as **`ScriptableObject` assets**
in Unity's content pipeline (Addressables, Resources, etc.) — write
your own `IAnatomyRepository` that builds
`AnatomyParameterDefinition` instances from the SO data. See §3
below — that's the recommended path once you're past bring-up.

### 1.5 Run a turn

There are two paths into a `GameSession`:

**(a) Use `Pinder.SessionSetup.CharacterDefinitionLoader`** — the
high-level loader that reads a character JSON file, resolves its
items and anatomy through the repositories you supply, runs
`CharacterAssembler` to combine fragments, and returns a fully-built
`CharacterProfile`. This is the path `pinder-web` and the
`session-runner` CLI both use; recommended for Unity too.

**(b) Build the `CharacterProfile` yourself** by calling
`CharacterAssembler.Assemble(...)` and then constructing
`CharacterProfile(stats, systemPrompt, name, timing, level, bio,
textingStyleFragment, activeArchetype, equippedItemDisplayNames,
textingStyleSources)`. Useful when your characters live in
ScriptableObjects, not JSON.

Minimal end-to-end with path (a):

```csharp
using System.Net.Http;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.OpenAi;
using Pinder.SessionSetup;

public class PinderRunner : MonoBehaviour
{
    // Long-lived HttpClient — share across calls, dispose on app quit.
    private static readonly HttpClient _http = new HttpClient();
    private GameSession _session;

    public async void StartGame(string playerSlug, string opponentSlug)
    {
        // 1. Load data repositories.
        IAnatomyRepository anatomy = new JsonAnatomyRepository(
            await ReadStreamingAssetTextAsync("PinderData/anatomy/anatomy-parameters.json"));
        IItemRepository    items   = new JsonItemRepository(
            await ReadStreamingAssetTextAsync("PinderData/items/starter-items.json"));
        ITrapRegistry      traps   = new JsonTrapRepository(
            await ReadStreamingAssetTextAsync("PinderData/traps/traps.json"));

        // 2. Wire the LLM adapter via PinderLlmAdapter + a transport.
        var transport = new OpenAiTransport(_http, apiKey: "sk-...", model: "gpt-4o");
        var options   = new PinderLlmAdapterOptions(/* see source for full list */);
        ILlmAdapter llm = new PinderLlmAdapter(transport, options);

        // 3. Load characters via SessionSetup. CharacterDefinitionLoader.Load
        //    is a static helper: it reads the JSON, runs CharacterAssembler,
        //    and returns a fully-built CharacterProfile.
        var playerPath = Path.Combine(
            Application.streamingAssetsPath, $"PinderData/characters/{playerSlug}.json");
        var opponentPath = Path.Combine(
            Application.streamingAssetsPath, $"PinderData/characters/{opponentSlug}.json");
        CharacterProfile player   = CharacterDefinitionLoader.Load(playerPath, items, anatomy);
        CharacterProfile opponent = CharacterDefinitionLoader.Load(opponentPath, items, anatomy);

        // 4. Construct the session. `GameSessionConfig` is required — the
        //    engine refuses silent defaults. The zero-arg call is fine for
        //    bring-up; see GameSessionConfig.cs for every knob (DC bias,
        //    clock, shadow trackers, RNG, etc.).
        var config       = new GameSessionConfig();
        IDiceRoller dice = new SystemRandomDiceRoller(seed: null);  // null = nondeterministic
        _session = new GameSession(player, opponent, llm, dice, traps, config);
    }

    public async Task PickOption(int optionIndex, IProgress<TurnProgressEvent>? progress = null)
    {
        TurnResult result = await _session.ResolveTurnAsync(optionIndex, progress);
        // result.DeliveredMessage ← the player's outgoing message after roll degradation
        // result.OpponentMessage  ← opponent reply
        // result.Roll             ← RollResult: d20, DC, fail tier, etc.
        // result.InterestDelta    ← +/− net interest change this turn
        // result.IsGameOver       ← true when the conversation has ended
        // result.Outcome          ← GameOutcome? when ended
        // result.StateAfter       ← GameStateSnapshot of post-turn state
        UpdateUI(result);
    }
}
```

The `IProgress<TurnProgressEvent>` callback fires during the turn:
options ready → option picked → resolution rolling → opponent
streaming → done. Subscribe to drive a progress bar or to surface
early text. A complete event taxonomy is at the top of
[`src/Pinder.Core/Conversation/TurnProgress.cs`](../src/Pinder.Core/Conversation/TurnProgress.cs).

> **`Pinder.SessionSetup`** is a fourth assembly alongside the three
> in §0's table. Add it to your Unity project the same way — source
> import or DLL — if you take path (a). It depends on `Pinder.Core`
> only. If you take path (b) (ScriptableObject characters), you can
> skip it.

---

## 2. Snapshot / restore / replay

The engine supports save / load via `GameStateSnapshot`. Call
`session.Snapshot()` to serialise; pass the snapshot to
`GameSession.Restore(...)` to resume:

```csharp
GameStateSnapshot snap = _session.Snapshot();
var json = JsonConvert.SerializeObject(snap);
PlayerPrefs.SetString("pinder.session", json);
// ...later...
var restored = GameSession.Restore(JsonConvert.DeserializeObject<GameStateSnapshot>(json),
                                   player, opponent, llm, dice, trapRegistry);
```

The snapshot covers all engine-owned state: interest, traps, momentum,
combo state, shadows, weakness windows, tells, opponent LLM history,
horniness rolls, callback opportunities. **It does not cover client
state** — your UI is responsible for re-rendering from the snapshot's
public fields (`InterestState`, `TurnNumber`, etc.).

For step-through replay (showing previous turns at the player's pace)
the recommended pattern is to log every `TurnResult` returned from
`ResolveTurnAsync` and re-feed those to your UI in order. Don't
re-execute the engine for replay — `pinder-web`'s replay strategy
(locked decision in §8.6 of `407-408-conversation-log-replay-DRY.md`)
is to read stored turn payloads, not re-roll.

---

## 3. Adapting interfaces when your Unity content doesn't match

This is the section to bookmark. The defaults shipped under `data/`
(9 anatomy parameters, 6 stats, ~40 items, 6 character archetypes,
specific trap set) are the **content the LLM was tuned against**.
When your Unity project ships different content, follow the rules
below — in order — to stay inside the engine's contract.

### 3.1 The contract you must preserve

`pinder-core` makes these assumptions and will misbehave if you
violate them:

| Invariant | Why |
|---|---|
| Six positive stats: Charm, Rizz, Honesty, Chaos, Wit, SelfAwareness. | `StatType` enum drives DC computation, success scaling, shadow pairing. Adding/removing requires editing the enum and recompiling. |
| Six paired shadow stats: Madness/Despair/Denial/Fixation/Dread/Overthinking. | Same enum. Shadow growth, hint computation, threshold evaluator all key off these. |
| Anatomy is a flat list of **named parameters**, each with a list of **named tiers**. | The engine indexes by `parameterId` + `tierId` strings. The number / names of parameters is data, not code. |
| Each anatomy tier exposes the six fragment fields (`stat_modifiers`, `personality_fragment`, `backstory_fragment`, `texting_style_fragment`, `archetype_tendencies`, `response_timing_modifier`). | `CharacterAssembler` reads these and combines into the LLM prompt. Missing fields are tolerated (treated as empty); spurious fields are ignored. |
| Items are a flat list keyed by `id`, with the same six fragment fields. | Same reason. |
| Character JSON has `name`, `level`, `items[]`, `anatomy{}`, `build_points{}`, `shadows{}`. | Drives `CharacterAssembler.Assemble`. |

Anything above the line: **safe to vary at the data layer**, no code
change.

Anything below the line (StatType / ShadowStatType): **requires a
code change in pinder-core itself**. Don't fork. Open an issue in the
engine repo first — adding a stat is a breaking change that touches
roll engine, XP, prompts, and the entire `pinder-web` UI layer.

### 3.2 You have different anatomy parameters

E.g. your Unity project has 12 anatomy parameters instead of 9, or
adds a new parameter "Voice Pitch", or drops "Skin Texture".

**Do this:** ship your own `anatomy-parameters.json` (or your own
`IAnatomyRepository`). The engine reads parameter ids and tier ids as
strings; the shipped 9 parameters are not hardcoded anywhere except
in `data/anatomy/anatomy-parameters.json` and the character JSON
files that reference them.

Step-by-step:

1. **Define the parameters** in JSON (or in your ScriptableObject
   pipeline) using the exact shape in
   `data/anatomy/anatomy-parameters.json`. Each parameter has an
   `id`, `name`, and `tiers[]`.
2. **Define the tiers**. Each tier has `id`, `name`, `stat_modifiers`,
   the four fragments, `archetype_tendencies`, and
   `response_timing_modifier`. Cosmetic-only tiers (no fragments,
   only a `visual_description`) are allowed — see "Skin Tone" in the
   shipped data for the pattern.
3. **Update your character JSON** so every character's `anatomy{}`
   block references your new parameter ids and tier ids.
4. **Validate**: run a session against `NullLlmAdapter` and confirm
   `CharacterAssembler.Assemble` succeeds for every character. Any
   missing parameter / tier id will throw; the error message names
   the missing key.

> **Rule:** anatomy parameter ids and tier ids are looked up
> case-insensitively (`StringComparer.OrdinalIgnoreCase`). Item ids
> are looked up case-sensitively (`StringComparer.Ordinal`).
> Lower-case kebab-case is the convention that works for both.

### 3.3 You have different stat ranges

E.g. your Unity game uses 0–5 stats instead of 1–6, or wants no
upper bound, or wants `level` to add +2 not +1 per level.

**Do this:** the roll formula is

```
d20 + statModifier + levelBonus + externalBonus >= DC
DC = 16 + opponent's defending stat modifier
```

`statModifier` is the raw value from `build_points` (no ability-score
table). `levelBonus` is `level - 1`. Both come from data, not code.

| You want | Where to change |
|---|---|
| Different base modifier table (e.g. D&D-style `(score-10)/2`) | Override `Pinder.Core.Stats.StatBlock`'s modifier accessor in your own subclass, or pre-compute and store the modifier directly in `build_points`. |
| Different level scaling | `Pinder.Core.Conversation.GameSession` reads `_player.Level` directly. Change the level bonus by writing your own `GameSession` wrapper, or scale the value at `CharacterAssembler` time. |
| Different DC base (e.g. `15 +` instead of `16 +`) | `_globalDcBias` constructor parameter on `GameSession`. Pass `bias = -1` to lower the base by one. Range bias only — no separate per-stat overrides. |
| Different stat range (0–5, 0–10, etc.) | Stats are integers; the engine doesn't enforce a range. Cap them at character-creation time in your Unity UI. |

Don't rename `StatType` enum values. Their string forms appear in
character JSON, archetype tendencies, shadow pairings, and i18n
catalog keys.

### 3.4 You have items that don't exist in `data/items/`

E.g. you've written 200 new items as Unity ScriptableObjects.

**Do this:** write an `IItemRepository` over your SO catalog. The
engine never enumerates items it doesn't reference; it only looks up
items that appear in a character's `items[]` array. Your SO catalog
needs to produce `ItemDefinition` instances with the same six
fragment fields. See `Pinder.Core.Characters.ItemDefinition`.

Minimal example:

```csharp
public sealed class ScriptableItemRepository : IItemRepository
{
    private readonly Dictionary<string, ItemDefinition> _items;

    public ScriptableItemRepository(IEnumerable<PinderItemSO> sos)
    {
        _items = sos.ToDictionary(
            so => so.Id,
            so => new ItemDefinition(
                id:                    so.Id,
                name:                  so.DisplayName,
                statModifiers:         so.StatModifiers.ToDictionary(),
                personalityFragment:   so.PersonalityFragment,
                backstoryFragment:     so.BackstoryFragment,
                textingStyleFragment:  so.TextingStyleFragment,
                archetypeTendencies:   so.ArchetypeTendencies.ToArray(),
                responseTimingModifier: so.ResponseTimingModifier.ToTimingModifier()),
            StringComparer.OrdinalIgnoreCase);
    }

    public ItemDefinition? GetItem(string itemId)
        => _items.TryGetValue(itemId, out var item) ? item : null;

    public IEnumerable<ItemDefinition> GetAll() => _items.Values;
}
```

If your items have **fewer fragment fields** than the engine expects
— say you don't write a `texting_style_fragment` for some items —
pass `null` or empty string. `CharacterAssembler` skips empty
fragments when assembling the LLM prompt.

If your items have **extra fields** the engine doesn't know about —
e.g. a Unity-specific `iconAddress`, `equippedSlot`, or 3D mesh
reference — store them on your SO and ignore them in the
`ItemDefinition` projection. The engine never sees them. Your Unity
UI can read the SO directly for rendering.

### 3.5 You have characters with mismatched anatomy

E.g. a character has `anatomy.length = "extra-long"` but your shipped
parameter only defines `short / medium / long / legendary`.

**Do one of these:**

1. **Add the missing tier** to your anatomy parameter definition.
   Easiest path; preserves existing characters. New tier needs the
   six fragment fields filled in (or null for cosmetic-only).
2. **Map the unknown value to a known tier** at load time. Write a
   migration step in your character loader:

   ```csharp
   private static readonly Dictionary<(string param, string tier), string> Migrations = new()
   {
       { ("length", "extra-long"), "legendary" },
       { ("eye_style", "piercing"), "soft"     },
   };

   private static CharacterJson Migrate(CharacterJson c)
   {
       foreach (var kv in c.Anatomy.ToList())
       {
           if (Migrations.TryGetValue((kv.Key, kv.Value), out var replacement))
               c.Anatomy[kv.Key] = replacement;
       }
       return c;
   }
   ```
3. **Reject the character** at load time with a clear error. Use this
   for content-team workflow — surfaces missing-tier mistakes during
   character authoring rather than at runtime.

`CharacterAssembler.Assemble` throws if it can't resolve a parameter
or tier id. Catch the exception, log the missing id, decide your
policy.

### 3.6 You have items / anatomy that the engine recognises but you don't want to use

E.g. your Unity game ships a tone-down PEGI-12 build that excludes
the spicier item set.

**Do this:** filter at the `IItemRepository` level. The engine only
cares that an item lookup either succeeds or returns null; it does
not enumerate to discover which items "exist". Build a filtered
repository that omits the items you've curated out, and your
character JSON simply won't reference them. Don't strip them from
the engine's enum or rebuild — the JSON-driven pattern means content
filtering is a data concern.

### 3.7 You're on a non-English locale

`pinder-core` ships a single English i18n catalog under
`data/i18n/en/`. The yaml schema is documented in pinder-web's
[`docs/i18n.md`](https://github.com/decay256/pinder-web/blob/main/docs/i18n.md);
the engine-side loader (`Pinder.LlmAdapters.I18nCatalog`) reads the
same yaml files and the engine-side variant picker
(`Pinder.Core.I18n.VariantPicker`) is byte-for-byte identical to the
frontend's. To add another locale:

1. Copy `data/i18n/en/` to `data/i18n/<your-locale>/` and translate
   every yaml file. Keys must stay identical.
2. Pass your locale to `Pinder.LlmAdapters.I18nCatalog.LoadFor("...")`.
3. Your `ILlmAdapter` should pass the same locale into prompt
   assembly so the LLM is instructed to reply in your language. The
   prompt builder reads `game-definition.yaml` for the language
   directive — translate that file too.
4. The variant-picker (see `Pinder.Core.I18n.VariantPicker`) is
   locale-agnostic — it picks variant N by FNV-1a-32 hashing of
   `(kind, turn)`. Cross-locale parity is preserved.

### 3.8 You want different traps

Edit `data/traps/traps.json`. Each trap has a name, activation
conditions, modification instruction (the LLM rewrite directive), and
optional persistence rules. Schema is in `data/traps/trap-schema.json`.
Adding/removing traps is data-only; the engine reads them through
`ITrapRegistry` (default impl: `Pinder.Core.Data.JsonTrapRepository`).

### 3.9 You want different archetypes

Archetypes live in `Pinder.Core.Characters.ArchetypeCatalog` (engine
code) and are referenced by name from the `archetype_tendencies`
arrays on every anatomy tier and item. There is no separate
archetype data file in `data/` — archetypes are a fixed roster the
engine ships with, and tiers/items vote for which one becomes a
character's active archetype.

If you want a different roster:

1. Edit `ArchetypeCatalog.cs` to add / rename / remove archetypes.
   This is a code change in pinder-core, not a data change.
2. Update every anatomy tier (`data/anatomy/anatomy-parameters.json`)
   and every item (`data/items/*.json`) so its
   `archetype_tendencies` array references only archetypes that
   exist in your modified catalog. Stale references silently
   downweight to zero — no error, but the LLM voice loses signal.
3. Update `data/i18n/<locale>/` strings that name archetypes by id
   (search for `archetype.` keys).

Like `StatType`, this is the kind of change that's better discussed
upstream than forked. Open an issue first.

---

## 4. Common pitfalls on Unity

| Pitfall | Fix |
|---|---|
| `JsonConvert` works in Editor, throws in IL2CPP build. | IL2CPP strips reflection-only types. Use `[Preserve]` on your DTOs or add a `link.xml` keeping `Pinder.Core.*` and your repository projections. |
| Tasks never resolve in Editor play mode. | Unity main thread synchronization context blocks `Task.Run` continuations. Use `await UniTask.SwitchToMainThread()` or marshal back manually with `UnitySynchronizationContext`. The engine itself does no thread-switching. |
| StreamingAssets reads return null on Android. | Use `UnityWebRequest`, not `File.ReadAllText`. See §1.4. |
| Newtonsoft.Json conflict (Unity package vs DLL). | Pick one. If using the Unity Package Manager version, drop the DLL and update the asmdef `precompiledReferences`. |
| `HttpClient` leaks between domain reloads. | Cache it on a `RuntimeInitializeOnLoadMethod`-driven static, dispose in `OnApplicationQuit`. Don't `new HttpClient()` per request. |
| YamlDotNet not stripped by IL2CPP, large binary. | Acceptable cost during integration; consider switching to a precompiled yaml-to-json step at build time once your data is stable. |
| Character data lookup is case-sensitive on disk but the in-memory index is case-insensitive. | Stick to lower-case kebab-case for ids in both files and ScriptableObjects. |
| Async exceptions get swallowed in MonoBehaviour `async void`. | Always `try/catch` inside `async void` UI handlers and surface errors to the user. The engine throws on missing data, broken adapters, and cancellation — do not eat them silently. |

---

## 5. Testing your Unity integration

Three tiers, in the order you should add them:

1. **Bring-up smoke** (run once per content change). Wire
   `NullLlmAdapter` + your repositories + a known character pair, run
   `ResolveTurnAsync` for 5 turns, assert no exceptions and that
   `_session.TurnNumber == 5`. This catches missing items, missing
   tiers, malformed JSON.
2. **Adapter parity** (run during LLM-adapter changes). Replay a
   pinned conversation through your adapter and compare the LLM
   prompts to a fixture file. Any drift means your adapter is
   building prompts differently from `pinder-web`'s adapter — usually
   a missing field on `DialogueContext` or `OpponentContext`.
3. **End-to-end** (run before release). Full session against the live
   LLM, human-reviewed. There's no way to automate this — comedy
   tone is the QA criterion.

Use the engine's `Pinder.Core.Tests` test suite as a reference for
fixture shapes and adapter behaviour. Run
`dotnet test --filter "Category=Core"` from the engine repo to confirm
the engine itself is healthy before chasing Unity-side issues.

---

## 6. Where to look when something breaks

| Symptom | Look here |
|---|---|
| Character won't load (`ArgumentException`) | `CharacterAssembler.Assemble` — names the missing id |
| Roll computes the wrong DC | `RollEngine` + `_globalDcBias` parameter on `GameSession` |
| Opponent never replies | Your `ILlmAdapter.GetOpponentResponseAsync` — engine does not retry adapter errors |
| Snapshot won't restore | `GameStateSnapshot.cs` schema; check that `ResimulateData.OpponentHistory` survived your serialiser |
| Replay shows different text from live | You re-executed the engine on replay. Don't. Read the stored `TurnResult` payload. |
| Items don't affect the prompt | Item is in `IItemRepository.GetItem` returning null. Check the id casing. |
| Mood / horniness overlay never fires | Adapter returned the unmodified message. Confirm the overlay methods on your adapter actually rewrite. |

Engine-side bug reports go to `https://github.com/decay256/pinder-core/issues`.
Integration questions (Unity-specific) belong on your project's
issue tracker first; raise upstream only when you've isolated to an
engine-side problem with a reproducer.

---

## 7. Versioning

Pin your Unity project to a specific pinder-core commit, not `main`.
The engine's `main` is moving — fast-gameplay branches, replay
contracts, prompt-shape revisions, and i18n schema changes all happen
without semver. Treat each pinder-core merge to `main` as a potential
breaking change until the engine ships its first numbered release.

Recommended workflow:

1. Pick a known-good commit on pinder-core `main`.
2. Vendor that commit's source (Option A) or DLLs (Option B) into
   your Unity project. Commit the vendored copy.
3. Track upstream releases in `CHANGELOG.md` (in pinder-web; the
   engine doesn't yet have its own changelog).
4. Plan integration upgrades in dedicated PRs — never roll the engine
   commit forward in a content PR.
