using System;
using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Typed material-surface state carried by character files and API DTOs.
    /// Numeric ranges mirror the authored Unity controls; pattern ids are
    /// carried as host-resolved strings and are not scalar anatomy bands.
    /// </summary>
    public sealed class CharacterSurfaceMaterial
    {
        public const float SmoothnessMin = 0f;
        public const float SmoothnessMax = 100f;
        public const float SmoothnessDefault = 51f;
        public const string DefaultFrecklesPatternId = "Freckles_Dots";
        public const int SurfaceLayerCount = 2;

        public float Smoothness { get; }
        public string FrecklesPatternId { get; }
        public IReadOnlyList<CharacterSurfaceLayer> SurfaceLayers { get; }

        public CharacterSurfaceMaterial(
            float smoothness,
            string frecklesPatternId,
            IReadOnlyList<CharacterSurfaceLayer> surfaceLayers)
        {
            ValidateRange("surface_material.smoothness", smoothness, SmoothnessMin, SmoothnessMax);
            if (string.IsNullOrWhiteSpace(frecklesPatternId))
                throw new FormatException("Character definition field surface_material.freckles_pattern_id must be a non-empty string.");
            if (surfaceLayers == null)
                throw new ArgumentNullException(nameof(surfaceLayers));
            if (surfaceLayers.Count != SurfaceLayerCount)
                throw new FormatException($"Character definition field surface_material.surface_layers must contain exactly {SurfaceLayerCount} entries.");

            Smoothness = smoothness;
            FrecklesPatternId = frecklesPatternId;
            SurfaceLayers = new List<CharacterSurfaceLayer>(surfaceLayers).AsReadOnly();
        }

        public static CharacterSurfaceMaterial Default => new CharacterSurfaceMaterial(
            SmoothnessDefault,
            DefaultFrecklesPatternId,
            new[]
            {
                CharacterSurfaceLayer.Default("Wrinkles_Soft"),
                CharacterSurfaceLayer.Default("Wrinkles_Fine"),
            });

        internal static void ValidateRange(string fieldPath, float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new FormatException($"Character definition field {fieldPath} must be within [{min}, {max}], got {value}.");
        }
    }

    public sealed class CharacterSurfaceLayer
    {
        public const float StrengthMin = 0f;
        public const float StrengthMax = 2f;
        public const float TilingMin = 1f;
        public const float TilingMax = 50f;
        public const float RotationMin = 0f;
        public const float RotationMax = 10f;

        public float Strength { get; }
        public float Tiling { get; }
        public float Rotation { get; }
        public string PatternId { get; }

        public CharacterSurfaceLayer(float strength, float tiling, float rotation, string patternId)
        {
            CharacterSurfaceMaterial.ValidateRange("surface_material.surface_layers[].strength", strength, StrengthMin, StrengthMax);
            CharacterSurfaceMaterial.ValidateRange("surface_material.surface_layers[].tiling", tiling, TilingMin, TilingMax);
            CharacterSurfaceMaterial.ValidateRange("surface_material.surface_layers[].rotation", rotation, RotationMin, RotationMax);
            if (string.IsNullOrWhiteSpace(patternId))
                throw new FormatException("Character definition field surface_material.surface_layers[].pattern_id must be a non-empty string.");

            Strength = strength;
            Tiling = tiling;
            Rotation = rotation;
            PatternId = patternId;
        }

        public static CharacterSurfaceLayer Default(string patternId)
            => new CharacterSurfaceLayer(0f, 4f, 0f, patternId);
    }
}