using System;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// A named modifier entry in a roll's modifier bag.
    /// Key is a stable machine-readable token (e.g. "charm", "level", "tell", "callback").
    /// Value may be zero; zero-value modifiers are kept so the renderer can show +0 if needed.
    /// </summary>
    public readonly struct NamedModifier : IEquatable<NamedModifier>
    {
        public string Key { get; }
        public int Value { get; }

        public NamedModifier(string key, int value)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Value = value;
        }

        public bool Equals(NamedModifier other) => Key == other.Key && Value == other.Value;
        public override bool Equals(object? obj) => obj is NamedModifier other && Equals(other);
        public override int GetHashCode() => (Key?.GetHashCode() ?? 0) ^ Value;
        public override string ToString() => $"{Key}:{Value:+0;-0;+0}";

        public static bool operator ==(NamedModifier left, NamedModifier right) => left.Equals(right);
        public static bool operator !=(NamedModifier left, NamedModifier right) => !left.Equals(right);
    }
}
