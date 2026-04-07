# Contract: Issue #644 — Shadow Growth Warnings

## Component: `Pinder.SessionRunner`

### 1. `OptionScore`
- Add `public string? ShadowGrowthWarning { get; }` to constructor and properties.

### 2. `ScoringPlayerAgent`
- Evaluate the shadow growth penalty for each option.
- If shadow growth occurs (e.g., Fixation +1 from repeated stat), set `ShadowGrowthWarning = "⚠️ Fixation +1"`.
- If shadow reduction occurs (e.g., Honesty at high interest), set `ShadowGrowthWarning = "✨ Denial -1"`.

### 3. `Program.cs`
- In the options printing loop, append the warning to the badge string.
  `Console.WriteLine($"**{letters[i]})** ... {opt.ShadowGrowthWarning}");`
