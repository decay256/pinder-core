namespace Pinder.Core.Characters
{
    /// <summary>
    /// Numeric scale bounds for a numeric anatomy parameter. The unit is a
    /// free-form display string (e.g. "inches", "cm"); the engine doesn't
    /// reason about it — it's preserved for round-trip and used by the
    /// editor UI as the input suffix.
    /// </summary>
    /// <remarks>
    /// Introduced for the admin-content-editor sprint (#551). Categorical
    /// parameters have no numeric range; their <see cref="AnatomyParameterDefinition.NumericRange"/>
    /// is null. Numeric parameters store one instance of this on the parameter
    /// plus a <see cref="AnatomyTierDefinition.NumericBreakpoint"/> on every
    /// tier indicating where on the scale that tier sits.
    /// </remarks>
    public sealed class NumericRangeSpec
    {
        public int Min { get; }
        public int Max { get; }
        public string Unit { get; }

        public NumericRangeSpec(int min, int max, string unit)
        {
            Min  = min;
            Max  = max;
            Unit = unit ?? string.Empty;
        }
    }
}
