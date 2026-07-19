namespace Pinder.Core.Characters
{
    /// <summary>
    /// Data-driven host-facing metadata for rendering an anatomy control.
    /// Gameplay still resolves through <see cref="AnatomyParameterDefinition.Bands"/>.
    /// </summary>
    public sealed class AnatomyParameterMetadata
    {
        public string Group { get; }
        public string Section { get; }
        public string LabelKey { get; }
        public string ControlType { get; }
        public float NormalizedMin { get; }
        public float NormalizedMax { get; }
        public float NormalizedDefault { get; }
        public float NormalizedStep { get; }
        public int DisplayOrder { get; }

        public AnatomyParameterMetadata(
            string group,
            string section,
            string labelKey,
            string controlType,
            float normalizedMin,
            float normalizedMax,
            float normalizedDefault,
            float normalizedStep,
            int displayOrder)
        {
            Group = group;
            Section = section;
            LabelKey = labelKey;
            ControlType = controlType;
            NormalizedMin = normalizedMin;
            NormalizedMax = normalizedMax;
            NormalizedDefault = normalizedDefault;
            NormalizedStep = normalizedStep;
            DisplayOrder = displayOrder;
        }
    }
}
