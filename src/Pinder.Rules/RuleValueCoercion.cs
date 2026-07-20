using System;
using System.Globalization;

namespace Pinder.Rules
{
    internal static class RuleValueCoercion
    {
        public static int ToInt(object? value, string prefix, string context)
        {
            if (value == null)
                throw new FormatException($"{prefix} {context} must be numeric, got null.");
            if (value is int i) return i;
            if (value is long l)
            {
                if (l < int.MinValue || l > int.MaxValue)
                    throw new FormatException($"{prefix} {context} must fit in Int32, got '{value}'.");
                return (int)l;
            }
            if (value is double d) return ToStrictInt(d, prefix, context, value);
            if (value is float f) return ToStrictInt(f, prefix, context, value);
            if (value is string s)
            {
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
                {
                    if (parsedLong < int.MinValue || parsedLong > int.MaxValue)
                        throw new FormatException($"{prefix} {context} must fit in Int32, got '{value}'.");
                    return (int)parsedLong;
                }
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
                    return ToStrictInt(parsedDouble, prefix, context, value);
            }
            throw new FormatException($"{prefix} {context} must be numeric, got '{value}'.");
        }

        private static int ToStrictInt(double value, string prefix, string context, object original)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Truncate(value) != value)
                throw new FormatException($"{prefix} {context} must be a finite whole number, got '{original}'.");
            if (value < int.MinValue || value > int.MaxValue)
                throw new FormatException($"{prefix} {context} must fit in Int32, got '{original}'.");
            return (int)value;
        }

        public static double ToDouble(object? value, string prefix, string context)
        {
            if (value == null)
                throw new FormatException($"{prefix} {context} must be numeric, got null.");
            if (value is double d) return ToFiniteDouble(d, prefix, context, value);
            if (value is float f) return ToFiniteDouble(f, prefix, context, value);
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return ToFiniteDouble(parsed, prefix, context, value);
            throw new FormatException($"{prefix} {context} must be numeric, got '{value}'.");
        }

        private static double ToFiniteDouble(double value, string prefix, string context, object original)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException($"{prefix} {context} must be finite, got '{original}'.");
            return value;
        }

        public static bool ToBool(object? value, string prefix, string context)
        {
            if (value == null)
                throw new FormatException($"{prefix} {context} must be boolean, got null.");
            if (value is bool b) return b;
            if (value is string s)
            {
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            throw new FormatException($"{prefix} {context} must be boolean, got '{value}'.");
        }
    }
}
