using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Wire-contract DTO mirroring Unity's <c>CharacterData</c> fields
    /// (see <c>Assets/Scripts/Core/CharacterData.cs</c> at tip c0d45c5).
    ///
    /// All field names and ranges match Unity exactly. This DTO is the
    /// entry-point for JSON coming from the Unity client or EigenCore.
    ///
    /// Normalisation (raw → [0..1]) is done by <see cref="CharacterDataNormalizer"/>.
    ///
    /// Note (issue #1175): grooming/look fields (<c>hasHair</c>,
    /// <c>hairLength</c>, <c>hairStyleIndex</c>, <c>hairColor</c>) are
    /// treated as COSMETIC-only and excluded from the anatomy parameter set
    /// per the locked design doc. Flag for Daniel to confirm before including.
    /// Sticker/tattoo fields are ITEMS (#1176), not anatomy — excluded.
    /// </summary>
    public sealed class CharacterDataDto
    {
        // ---- Trunk (0–100) ----
        public float TrunkLengthBase { get; set; } = 50f;
        public float TrunkLengthMid  { get; set; } = 50f;
        public float TrunkLengthTip  { get; set; } = 50f;
        public float TrunkGirth      { get; set; } = 50f;
        /// <summary>Bipolar −100..100; 0 = straight.</summary>
        public float TrunkCurvature  { get; set; } = 0f;

        // ---- Glans (0–100) ----
        public float GlansScale { get; set; } = 50f;
        public float GlansWidth { get; set; } = 50f;

        // ---- Scrotum (0–100) ----
        public float ScrotumScale        { get; set; } = 50f;
        public float LeftTesticleScale   { get; set; } = 50f;
        public float RightTesticleScale  { get; set; } = 50f;
        public float ScrotumDrop         { get; set; } = 50f;

        // ---- Age (0–100, default 0) ----
        public float Prepucius  { get; set; } = 0f;
        public float Arrugatis  { get; set; } = 0f;
        public float Gravitatis { get; set; } = 0f;
        public float Venicus    { get; set; } = 0f;

        // ---- Expression (0–100, default 0) ----
        public float Sad   { get; set; } = 0f;
        public float Happy { get; set; } = 0f;
        public float Serius { get; set; } = 0f;

        // ---- Skin ----
        /// <summary>Unity Color RGB, each channel [0..1].</summary>
        public float SkinColorR { get; set; } = 0.87f;
        public float SkinColorG { get; set; } = 0.72f;
        public float SkinColorB { get; set; } = 0.63f;

        /// <summary>Freckles intensity 0–100.</summary>
        public float Freckles  { get; set; } = 0f;
        /// <summary>Blemishes intensity 0–100.</summary>
        public float Blemishes { get; set; } = 0f;
        /// <summary>Vein visibility 0–100.</summary>
        public float Veins     { get; set; } = 30f;

        /// <summary>Circumcision state.</summary>
        public bool IsCircumcised { get; set; } = false;

        // ---- Grooming (COSMETIC-ONLY, excluded from anatomy params) ----
        // hasHair, hairLength, hairStyleIndex, hairColor — not anatomy scalars.
    }

    /// <summary>
    /// Maps a <see cref="CharacterDataDto"/> (raw Unity values) to a
    /// <c>Dictionary&lt;string,float&gt;</c> of normalised [0..1] anatomy values,
    /// ready for <see cref="CharacterDefinition.Anatomy"/>.
    ///
    /// Normalisation rules:
    /// <list type="bullet">
    ///   <item>0–100 → / 100</item>
    ///   <item>trunkCurvature: −100..100 → (x + 100) / 200</item>
    ///   <item>skinColor RGB → HSV → skinHue/skinSat/skinVal (each [0..1])</item>
    ///   <item>isCircumcised bool → 0.0 / 1.0</item>
    /// </list>
    /// </summary>
    public static class CharacterDataNormalizer
    {
        /// <summary>
        /// Normalises all anatomy fields in <paramref name="dto"/> to [0..1]
        /// and returns a dictionary keyed by the Unity field name.
        /// </summary>
        public static IReadOnlyDictionary<string, float> Normalize(CharacterDataDto dto)
        {
            var map = new Dictionary<string, float>();

            // Trunk
            map["trunkLengthBase"] = Clamp01(dto.TrunkLengthBase / 100f);
            map["trunkLengthMid"]  = Clamp01(dto.TrunkLengthMid  / 100f);
            map["trunkLengthTip"]  = Clamp01(dto.TrunkLengthTip  / 100f);
            map["trunkGirth"]      = Clamp01(dto.TrunkGirth       / 100f);
            // Bipolar: (x + 100) / 200  →  [0..1]
            map["trunkCurvature"]  = Clamp01((dto.TrunkCurvature + 100f) / 200f);

            // Glans
            map["glansScale"] = Clamp01(dto.GlansScale / 100f);
            map["glansWidth"] = Clamp01(dto.GlansWidth / 100f);

            // Scrotum
            map["scrotumScale"]       = Clamp01(dto.ScrotumScale       / 100f);
            map["leftTesticleScale"]  = Clamp01(dto.LeftTesticleScale  / 100f);
            map["rightTesticleScale"] = Clamp01(dto.RightTesticleScale / 100f);
            map["scrotumDrop"]        = Clamp01(dto.ScrotumDrop        / 100f);

            // Age
            map["prepucius"]  = Clamp01(dto.Prepucius  / 100f);
            map["arrugatis"]  = Clamp01(dto.Arrugatis  / 100f);
            map["gravitatis"] = Clamp01(dto.Gravitatis / 100f);
            map["venicus"]    = Clamp01(dto.Venicus     / 100f);

            // Expression
            map["sad"]    = Clamp01(dto.Sad    / 100f);
            map["happy"]  = Clamp01(dto.Happy  / 100f);
            map["serius"] = Clamp01(dto.Serius / 100f);

            // Skin — RGB → HSV
            RgbToHsv(dto.SkinColorR, dto.SkinColorG, dto.SkinColorB,
                     out float h, out float s, out float v);
            map["skinHue"] = Clamp01(h);
            map["skinSat"] = Clamp01(s);
            map["skinVal"] = Clamp01(v);

            map["freckles"]  = Clamp01(dto.Freckles  / 100f);
            map["blemishes"] = Clamp01(dto.Blemishes / 100f);
            map["veins"]     = Clamp01(dto.Veins     / 100f);

            // Bool → 2-band at 0.5
            map["isCircumcised"] = dto.IsCircumcised ? 1.0f : 0.0f;

            return map;
        }

        // -------------------------------------------------------------------

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        /// <summary>
        /// Converts an RGB colour (each channel [0..1]) to HSV.
        /// H is in [0..1] (i.e. 0–360° / 360). S and V are [0..1].
        /// Hue wraps: the same colour at different hue values is valid
        /// per the design doc (same colour, different stats acceptable).
        /// </summary>
        internal static void RgbToHsv(float r, float g, float b,
                                      out float h, out float s, out float v)
        {
            float max = Max3(r, g, b);
            float min = Min3(r, g, b);
            float delta = max - min;

            v = max;

            if (max < 1e-6f)
            {
                s = 0f;
                h = 0f;
                return;
            }

            s = delta / max;

            if (delta < 1e-6f)
            {
                h = 0f;
                return;
            }

            float hRaw;
            if (max == r)
                hRaw = (g - b) / delta + (g < b ? 6f : 0f);
            else if (max == g)
                hRaw = (b - r) / delta + 2f;
            else
                hRaw = (r - g) / delta + 4f;

            h = hRaw / 6f;
        }

        private static float Max3(float a, float b, float c)
            => a > b ? (a > c ? a : c) : (b > c ? b : c);

        private static float Min3(float a, float b, float c)
            => a < b ? (a < c ? a : c) : (b < c ? b : c);
    }
}
