namespace Pinder.Core.Rolls
{
    /// <summary>
    /// How badly a roll missed. Determined by (DC - roll) when roll &lt; DC.
    /// </summary>
    public enum FailureTier
    {
        None,           // Not a failure
        Fumble,         // Missed by 1–2:  minor awkwardness
        Misfire,        // Missed by 3–5:  message goes sideways
        TropeTrap,      // Missed by 6–9:  activates a Trap on the stat used
        Catastrophe,    // Missed by 10+:  spectacular disaster
        Legendary       // Nat 1:          regardless of DC, maximum humiliation
    }
}
