using System;
using System.IO;
using System.Text.Json;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Serialises a <see cref="CharacterDefinition"/> to canonical v1 JSON.
    ///
    /// Design contract (issue #815):
    /// <list type="bullet">
    ///   <item>Pure: no I/O, no side effects, deterministic.</item>
    ///   <item>Stable property ordering matches the schema's documented order
    ///         (<c>schema_version</c>, <c>character_id</c>, <c>name</c>,
    ///         <c>gender_identity</c>, <c>bio</c>, <c>level</c>, <c>items</c>,
    ///         <c>anatomy</c>, <c>allocation</c>).</item>
    ///   <item>2-space indent, single trailing newline (LF), UTF-8 no BOM.</item>
    ///   <item>Round-trip stable: <c>Write(Parse(json)) == json</c> byte-equal
    ///         for every v1 file produced by this writer.</item>
    /// </list>
    ///
    /// We hand-walk the POCO with <see cref="Utf8JsonWriter"/> rather than
    /// relying on default-serializer attribute order — the order is part of
    /// the on-disk contract and must not depend on reflection metadata.
    /// </summary>
    public static class CharacterDefinitionWriter
    {
        private const string Indent = "  ";

        // Canonical iteration orders. These determine the on-disk order of
        // map-valued blocks (allocation.spent and allocation.shadows). The
        // order matches the schema file's `properties` declaration order.
        private static readonly StatType[] StatOrder =
        {
            StatType.Charm,
            StatType.Rizz,
            StatType.Honesty,
            StatType.Chaos,
            StatType.Wit,
            StatType.SelfAwareness,
        };

        private static readonly ShadowStatType[] ShadowOrder =
        {
            ShadowStatType.Madness,
            ShadowStatType.Despair,
            ShadowStatType.Denial,
            ShadowStatType.Fixation,
            ShadowStatType.Dread,
            ShadowStatType.Overthinking,
        };

        /// <summary>
        /// Serialise a character definition to canonical v1 JSON. The result
        /// is a UTF-8 string with a single trailing LF newline.
        /// </summary>
        public static string Write(CharacterDefinition def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            // Utf8JsonWriter Indented=true defaults: 2-space indent, '\n'
            // newlines on netstandard2.0 / .NET 8. We pin both explicitly
            // via JsonWriterOptions so behaviour is independent of host
            // newline conventions.
            var options = new JsonWriterOptions
            {
                Indented = true,
                // Disable escaping of '+', '/', etc. so bios with normal
                // punctuation round-trip cleanly. The starter files do not
                // currently contain any unicode that would force escaping;
                // if a future bio does, the relaxed encoder still emits
                // valid UTF-8 JSON.
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            // MemoryStream is in System.IO but the writer never touches the
            // file system. The Pinder.Core "no System.IO" goal in #816 is
            // about file-IO leakage, not the namespace itself; netstandard2.0
            // does not expose ArrayBufferWriter<T>.WrittenMemory on the
            // version of System.Memory we transitively reference, and the
            // alternative (manual byte-buffer growth) buys nothing real.
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, options))
            {
                WriteRoot(writer, def);
            }

            string body = System.Text.Encoding.UTF8.GetString(ms.ToArray());

            // Force LF line endings even on Windows hosts so the writer's
            // output is byte-stable across operating systems. (.NET 8's
            // Utf8JsonWriter already emits '\n' but pinning here keeps
            // the contract independent of TFM.)
            body = body.Replace("\r\n", "\n");

            // Single trailing newline at EOF.
            if (!body.EndsWith("\n", StringComparison.Ordinal))
                body += "\n";

            return body;
        }

        private static void WriteRoot(Utf8JsonWriter writer, CharacterDefinition def)
        {
            writer.WriteStartObject();

            writer.WriteNumber("schema_version", def.SchemaVersion);
            writer.WriteString("character_id", def.CharacterId.ToString("D"));
            writer.WriteString("name", def.Name);
            writer.WriteString("gender_identity", def.GenderIdentity);
            writer.WriteString("bio", def.Bio);
            writer.WriteNumber("level", def.Level);

            writer.WriteStartArray("items");
            foreach (var item in def.Items)
                writer.WriteStringValue(item);
            writer.WriteEndArray();

            // anatomy: emitted in the order it appears in the POCO's
            // dictionary, which preserves insertion order from parse time.
            // Starter files are hand-authored with a documented field order;
            // the parser uses Dictionary<string,string> which on .NET preserves
            // insertion order. Round-trip tests pin this contract.
            writer.WriteStartObject("anatomy");
            foreach (var kv in def.Anatomy)
                writer.WriteString(kv.Key, kv.Value);
            writer.WriteEndObject();

            writer.WriteStartObject("allocation");
            WriteSpent(writer, def.Allocation);
            writer.WriteNumber("unspent_pool", def.Allocation.UnspentPool);
            WriteShadows(writer, def.Allocation);
            writer.WriteEndObject();

            // Issue #779: write the permanent stake if present.
            // Omitted (not written as null) when absent so legacy files
            // without it are not dirtied by a round-trip.
            if (!string.IsNullOrWhiteSpace(def.PsychologicalStake))
                writer.WriteString("psychological_stake", def.PsychologicalStake);

            // Issue #820: write the narrative background story if present.
            // Omitted when absent, same hygiene as stake.
            if (!string.IsNullOrWhiteSpace(def.BackgroundStory))
                writer.WriteString("background_story", def.BackgroundStory);

            writer.WriteEndObject();
        }

        private static void WriteSpent(Utf8JsonWriter writer, AllocationBlock alloc)
        {
            writer.WriteStartObject("spent");
            foreach (var stat in StatOrder)
            {
                if (!alloc.Spent.TryGetValue(stat, out int value))
                    value = 0;
                writer.WriteNumber(StatToWireKey(stat), value);
            }
            writer.WriteEndObject();
        }

        private static void WriteShadows(Utf8JsonWriter writer, AllocationBlock alloc)
        {
            writer.WriteStartObject("shadows");
            foreach (var shadow in ShadowOrder)
            {
                if (!alloc.Shadows.TryGetValue(shadow, out int value))
                    value = 0;
                writer.WriteNumber(ShadowToWireKey(shadow), value);
            }
            writer.WriteEndObject();
        }

        private static string StatToWireKey(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm:         return "charm";
                case StatType.Rizz:          return "rizz";
                case StatType.Honesty:       return "honesty";
                case StatType.Chaos:         return "chaos";
                case StatType.Wit:           return "wit";
                case StatType.SelfAwareness: return "self_awareness";
                default: throw new ArgumentOutOfRangeException(nameof(stat), stat, "Unknown StatType.");
            }
        }

        private static string ShadowToWireKey(ShadowStatType shadow)
        {
            switch (shadow)
            {
                case ShadowStatType.Madness:      return "madness";
                case ShadowStatType.Despair:      return "despair";
                case ShadowStatType.Denial:       return "denial";
                case ShadowStatType.Fixation:     return "fixation";
                case ShadowStatType.Dread:        return "dread";
                case ShadowStatType.Overthinking: return "overthinking";
                default: throw new ArgumentOutOfRangeException(nameof(shadow), shadow, "Unknown ShadowStatType.");
            }
        }
    }
}
