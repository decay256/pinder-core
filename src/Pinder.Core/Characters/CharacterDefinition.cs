using System;
using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Strongly-typed representation of an on-disk v1 character file
    /// (<c>data/characters/*.json</c>).
    ///
    /// This is the wire-shape of the file system: parsed from JSON, used by
    /// <c>CharacterDefinitionLoader</c> to drive <see cref="CharacterAssembler"/>,
    /// and round-tripped back to JSON by the writer (see issue #815).
    ///
    /// Immutability: every property is a constructor-initialised read-only
    /// reference. The POCO is sealed and exposes no mutators. The codebase is
    /// pinned to <c>LangVersion=8.0</c>, so the issue's "init-only" wording is
    /// implemented as an equivalent immutable-by-construction shape.
    ///
    /// Identity: <see cref="CharacterId"/> is the GUID identity. Filenames are
    /// presentation-only slugs and are not part of the identity contract.
    ///
    /// Allocation vs. bonuses split: this POCO carries the player-authored
    /// allocation (<see cref="Allocation"/>). Bonuses from items / anatomy are
    /// computed every load by <see cref="CharacterAssembler"/> and are NOT
    /// stored on disk.
    /// </summary>
    public sealed class CharacterDefinition
    {
        /// <summary>The integer schema version. v1 files MUST have this set to 1.</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>v1. Reader rejects missing or unknown values.</summary>
        public int SchemaVersion { get; }

        /// <summary>UUIDv4 identity. Stable across renames; filename slug is presentation only.</summary>
        public Guid CharacterId { get; }

        /// <summary>Display name (e.g. "Reuben_404").</summary>
        public string Name { get; }

        /// <summary>Free-form gender identity string (e.g. "he/him", "they/them").</summary>
        public string GenderIdentity { get; }

        /// <summary>Short bio shown to opposing player.</summary>
        public string Bio { get; }

        /// <summary>Character level, 1..11.</summary>
        public int Level { get; }

        /// <summary>Optional base response timing profile id from <c>data/timing/response-profiles.json</c>.</summary>
        public string? TimingProfileId { get; }

        /// <summary>Equipped item ids (resolved against <c>IItemRepository</c>).</summary>
        public IReadOnlyList<string> Items { get; }

        /// <summary>
        /// Anatomy selections. Map of parameter id (e.g. "length") to tier id
        /// (e.g. "short"). Resolved against <c>IAnatomyRepository</c>.
        /// </summary>
        public IReadOnlyDictionary<string, float> Anatomy { get; }

        /// <summary>Player-authored build-point allocation block.</summary>
        public AllocationBlock Allocation { get; }

        /// <summary>
        /// Issue #779: permanent psychological stake generated at character-creation
        /// time. Markdown bullet list of core emotional motivations and
        /// vulnerabilities. Null when absent (legacy files not yet regenerated).
        /// </summary>
        public string? PsychologicalStake { get; }

        public IReadOnlyDictionary<string, BackstoryFact>? Backstory { get; }

        public string? BackgroundStory => Backstory != null 
            ? string.Join(" ", System.Linq.Enumerable.Select(Backstory.Values, v => v.TragicReality)) 
            : null;

        public IReadOnlyList<string>? StakeLines { get; }
        public IReadOnlyDictionary<string, string>? PsychiatricDiagnosis { get; }
        public string? ConsolidatedPersonality { get; }
        public string? ConsolidatedBackstory { get; }

        public CharacterDefinition(
            int schemaVersion,
            Guid characterId,
            string name,
            string genderIdentity,
            string bio,
            int level,
            IReadOnlyList<string> items,
            IReadOnlyDictionary<string, float> anatomy,
            AllocationBlock allocation,
            string? psychologicalStake = null,
            IReadOnlyDictionary<string, BackstoryFact>? backstory = null,
            IReadOnlyList<string>? stakeLines = null,
            IReadOnlyDictionary<string, string>? psychiatricDiagnosis = null,
            string? consolidatedPersonality = null,
            string? consolidatedBackstory = null,
            string? timingProfileId = null)
        {
            SchemaVersion = schemaVersion;
            CharacterId = characterId;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            GenderIdentity = genderIdentity ?? throw new ArgumentNullException(nameof(genderIdentity));
            Bio = bio ?? throw new ArgumentNullException(nameof(bio));
            Level = level;
            TimingProfileId = string.IsNullOrWhiteSpace(timingProfileId) ? null : timingProfileId;
            Items = items ?? throw new ArgumentNullException(nameof(items));
            Anatomy = anatomy ?? throw new ArgumentNullException(nameof(anatomy));
            Allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
            PsychologicalStake = psychologicalStake;
            Backstory = backstory;
            StakeLines = stakeLines;
            PsychiatricDiagnosis = psychiatricDiagnosis;
            ConsolidatedPersonality = consolidatedPersonality;
            ConsolidatedBackstory = consolidatedBackstory;
        }
    }

    /// <summary>
    /// Player-authored build-point allocation. Mutable state in the gameplay
    /// sense (the player can redistribute), immutable in the value-object
    /// sense (assign a new <see cref="AllocationBlock"/> when redistributing).
    /// </summary>
    public sealed class AllocationBlock
    {
        /// <summary>Build points spent on each positive stat.</summary>
        public IReadOnlyDictionary<StatType, int> Spent { get; }

        /// <summary>Unspent build-point pool. v1 starter files all set this to 0.</summary>
        public int UnspentPool { get; }

        /// <summary>Allocated shadow values.</summary>
        public IReadOnlyDictionary<ShadowStatType, int> Shadows { get; }

        public AllocationBlock(
            IReadOnlyDictionary<StatType, int> spent,
            int unspentPool,
            IReadOnlyDictionary<ShadowStatType, int> shadows)
        {
            Spent = spent ?? throw new ArgumentNullException(nameof(spent));
            UnspentPool = unspentPool;
            Shadows = shadows ?? throw new ArgumentNullException(nameof(shadows));
        }
    }
}
