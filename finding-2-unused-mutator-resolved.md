# Finding 2 Resolution: Unused and Deprecated `AddExternalBonus` Mutator Method

## Overview
An audit of the `pinder-core` codebase identified that the `AddExternalBonus(int bonus)` method on `RollResult` was deprecated in Sprint 8 (under ADR #146) to enforce that external bonuses flow directly through `RollEngine.Resolve(externalBonus)` at constructor-time. Leaving this public mutable method active in the assembly posed a high architectural risk: it allowed post-hoc mutation of `ExternalBonus` without re-evaluating the roll's final success state (`IsSuccess`) or failure tier (`Tier`), which leads to subtle "dual-path bugs."

Static search confirmed there were zero active references calling this method across both the `pinder-core` and `pinder-web` repositories.

## Modifications Made
To eliminate the dual-path bug surface area and enforce architectural consistency, we completely removed the deprecated and unused mutator block from both repositories:

1. **`pinder-core` repository modification:**
   - File Modified: `/root/projects/pinder-core/src/Pinder.Core/Rolls/RollResult.cs`
   - Removed:
     ```csharp
     /// <summary>Apply an external bonus (callback, tell, combo, momentum). Additive.
     /// DEPRECATED: Use the externalBonus parameter on RollEngine.Resolve() or ResolveFixedDC() instead.</summary>
     [System.Obsolete("Use the externalBonus parameter on RollEngine.Resolve() or ResolveFixedDC() instead.")]
     public void AddExternalBonus(int bonus) { ExternalBonus += bonus; }
     ```

2. **`pinder-web` repository modification (submodule copy):**
   - File Modified: `/root/projects/pinder-web/pinder-core/src/Pinder.Core/Rolls/RollResult.cs`
   - Removed the same blocks to ensure perfect alignment.

## Verification
- We verified the fix by performing a full compilation on the `pinder-core` project via `dotnet build`. The compilation succeeded with zero errors.
- We executed the unit test suite and confirmed that no tests depend on or utilize `AddExternalBonus`.
