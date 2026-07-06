# Finding 2 Resolution: Scattered and Hardcoded Default Parameter Defaults (Parameter Drift)

## Overview
An audit of `PinderLlmAdapter` identified a parameter drift vulnerability. Although `PinderLlmAdapter` defines a central constant `DefaultDeliveryTemperature = 0.7` at the class level, three critical overlay methods (`ApplyHorninessOverlayAsync`, `ApplyTrapOverlayAsync`, and `ApplyShadowCorruptionAsync`) hardcoded the literal value `0.7` inside their bodies instead of using the constant. This meant any central tuning of `DefaultDeliveryTemperature` to adjust the game's comedic voice would be silently ignored by these three methods.

## Modifications Made

1. **Unified Parameter Fallback (Refactoring)**
   - Located the relevant code in `/root/projects/pinder-core/src/Pinder.LlmAdapters/PinderLlmAdapter.cs`.
   - Updated the temperature selection line in all three overlay methods to use the class-level constant `DefaultDeliveryTemperature` instead of the literal `0.7`:
     - **Before:** `double temperature = _options.DeliveryTemperature ?? 0.7;`
     - **After:** `double temperature = _options.DeliveryTemperature ?? DefaultDeliveryTemperature;`
   - This ensures all delivery-based and overlay operations draw their fallback temperature from a single source of truth (`DefaultDeliveryTemperature`), eliminating parameter drift.

2. **Added Unit Tests for Parameter Fallback**
   - Created a new test class `/root/projects/pinder-core/tests/Pinder.LlmAdapters.Tests/ParameterDriftFixTests.cs`.
   - Added automated tests verifying that:
     - `ApplyHorninessOverlayAsync` uses `DefaultDeliveryTemperature` (0.7) when `_options.DeliveryTemperature` is null.
     - `ApplyHorninessOverlayAsync` respects `_options.DeliveryTemperature` when explicitly set.
     - `ApplyTrapOverlayAsync` uses `DefaultDeliveryTemperature` (0.7) when `_options.DeliveryTemperature` is null.
     - `ApplyTrapOverlayAsync` respects `_options.DeliveryTemperature` when explicitly set.
     - `ApplyShadowCorruptionAsync` uses `DefaultDeliveryTemperature` (0.7) when `_options.DeliveryTemperature` is null.
     - `ApplyShadowCorruptionAsync` respects `_options.DeliveryTemperature` when explicitly set.

## Verification and Results

1. **Successful Compilation**
   - Ran `dotnet build /root/projects/pinder-core/tests/Pinder.LlmAdapters.Tests` which compiled with zero errors and warnings.

2. **Test Execution**
   - Executed `dotnet test /root/projects/pinder-core/tests/Pinder.LlmAdapters.Tests --filter FullyQualifiedName~ParameterDriftFixTests`.
   - **Result:** All 6 newly added tests passed successfully.
