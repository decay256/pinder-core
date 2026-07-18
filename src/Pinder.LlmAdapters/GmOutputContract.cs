using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// #1124: THE single canonical text output-format contract the Game Master
    /// must emit, reused by BOTH sessions (player-avatar delivery and datee
    /// response). pinder-core parses this format deterministically.
    ///
    /// Canonical shape:
    ///
    ///   &lt;message text — one or more lines&gt;
    ///   [SIGNALS]
    ///   TELL: &lt;Stat&gt; (&lt;description&gt;)
    ///   WEAKNESS: &lt;Stat&gt; -&lt;n&gt; (&lt;description&gt;)
    ///
    /// Rules:
    ///   • The message text is everything before the optional <c>[SIGNALS]</c>
    ///     marker. It is always present (may be empty on malformed input).
    ///   • The <c>[SIGNALS]</c> block is OPTIONAL. When present it may carry a
    ///     <c>TELL:</c> line and/or a <c>WEAKNESS:</c> line, in any order.
    ///   • A <c>TELL:</c> names the revealed stat and a parenthetical description.
    ///   • A <c>WEAKNESS:</c> names the defending stat, a <c>-n</c> DC reduction,
    ///     and a parenthetical description.
    ///
    /// <see cref="Emit"/> renders a <see cref="GmTurnOutput"/> to this format and
    /// <see cref="Parse"/> reads it back. The pair round-trips:
    /// <c>Parse(Emit(x)).Equals(x)</c> for any well-formed value. Parsing
    /// degrades gracefully — malformed or partial input never throws; unknown
    /// stats / missing fields collapse to a message-only result.
    /// </summary>
    public static class GmOutputContract
    {
        /// <summary>Marker that opens the optional structured signals block.</summary>
        public const string SignalsMarker = "[SIGNALS]";

        private const string ResponseMarker = "[RESPONSE]";
        private const string TellSignalPrefix = "TELL:";
        private const string WeaknessSignalPrefix = "WEAKNESS:";
        private const string ParserName = "GmOutputContract";
        private const string ParseFailureReason = "signals_parse_failure";
        private const string ParseFailureEventName = "ValidatedSignalsParseFailed";
        private const string GmOutputPhase = "gm_output";

        private static readonly Regex TellSignalRegex = new Regex(
            @"TELL:\s*(\w+)\s*\(([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex WeaknessSignalRegex = new Regex(
            @"WEAKNESS:\s*(\w+)\s*-(\d+)\s*\(([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Renders a GM turn output to the canonical text format. The result is
        /// parseable back into an equal value by <see cref="Parse"/>.
        /// </summary>
        public static string Emit(GmTurnOutput output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));

            var sb = new StringBuilder();
            sb.Append(output.Message ?? string.Empty);

            var tell = output.Tell;
            var weakness = output.Weakness;
            if (tell != null || weakness != null)
            {
                sb.Append('\n').Append(SignalsMarker);
                if (tell != null)
                {
                    sb.Append('\n')
                      .Append("TELL: ")
                      .Append(StatName(tell.Stat))
                      .Append(" (")
                      .Append(tell.Description)
                      .Append(')');
                }
                if (weakness != null)
                {
                    sb.Append('\n')
                      .Append("WEAKNESS: ")
                      .Append(StatName(weakness.DefendingStat))
                      .Append(" -")
                      .Append(weakness.DcReduction.ToString())
                      .Append(" (")
                      .Append(output.WeaknessDescription ?? string.Empty)
                      .Append(')');
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Parses canonical GM output text into a <see cref="GmTurnOutput"/>.
        /// Never throws — malformed input degrades to a message-only result with
        /// null signals. The message is everything before <c>[SIGNALS]</c>.
        /// <para>
        /// NOTE: This parser is lenient and best-effort for backwards compatibility.
        /// Production gameplay uses strict validation (ValidateSignalsStrict).
        /// </para>
        /// </summary>
        public static GmTurnOutput Parse(string? raw)
        {
            return ParseCore(
                raw,
                validatedSignalsBlock: false,
                onDiagnostic: null,
                tellSignalMatcher: MatchTellSignal,
                weaknessSignalMatcher: MatchWeaknessSignal);
        }

        /// <summary>
        /// Parses GM output after strict validation has accepted its [SIGNALS] block.
        /// If the validated signal block cannot be parsed, throws a contract exception
        /// instead of silently dropping gameplay-relevant signals.
        /// </summary>
        internal static GmTurnOutput ParseValidatedSignals(
            string? raw,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null)
        {
            return ParseValidatedSignals(
                raw,
                onDiagnostic,
                MatchTellSignal,
                MatchWeaknessSignal);
        }

        internal static GmTurnOutput ParseValidatedSignals(
            string? raw,
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            Func<string, Match> tellSignalMatcher,
            Func<string, Match> weaknessSignalMatcher)
        {
            if (tellSignalMatcher == null) throw new ArgumentNullException(nameof(tellSignalMatcher));
            if (weaknessSignalMatcher == null) throw new ArgumentNullException(nameof(weaknessSignalMatcher));

            return ParseCore(
                raw,
                validatedSignalsBlock: true,
                onDiagnostic: onDiagnostic,
                tellSignalMatcher: tellSignalMatcher,
                weaknessSignalMatcher: weaknessSignalMatcher);
        }

        private static GmTurnOutput ParseCore(
            string? raw,
            bool validatedSignalsBlock,
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            Func<string, Match> tellSignalMatcher,
            Func<string, Match> weaknessSignalMatcher)
        {
            if (string.IsNullOrEmpty(raw))
                return new GmTurnOutput(string.Empty);

            string message;
            Tell? tell = null;
            WeaknessWindow? weakness = null;
            string? weaknessDescription = null;

            try
            {
                int signalsIdx = raw!.IndexOf(SignalsMarker, StringComparison.OrdinalIgnoreCase);
                message = signalsIdx >= 0 ? raw.Substring(0, signalsIdx) : raw;
                message = message.TrimEnd('\n', '\r', ' ', '\t');

                if (signalsIdx >= 0)
                {
                    string block = raw.Substring(signalsIdx);

                    var tellMatch = tellSignalMatcher(block);
                    if (tellMatch.Success)
                    {
                        if (TryParseStat(tellMatch.Groups[1].Value, out var stat))
                        {
                            tell = new Tell(stat, tellMatch.Groups[2].Value.Trim());
                        }
                    }

                    var weakMatch = weaknessSignalMatcher(block);
                    if (weakMatch.Success)
                    {
                        if (TryParseStat(weakMatch.Groups[1].Value, out var stat) &&
                            int.TryParse(weakMatch.Groups[2].Value.Trim(), out int reduction) &&
                            reduction > 0)
                        {
                            weakness = new WeaknessWindow(stat, reduction);
                            weaknessDescription = weakMatch.Groups[3].Value.Trim();
                        }
                    }
                }
            }
            catch (Exception ex) when (validatedSignalsBlock)
            {
                throw CreateValidatedSignalsParseException(raw!, ex, onDiagnostic);
            }
            catch (RegexMatchTimeoutException)
            {
                return new GmTurnOutput(raw!.Trim());
            }
            catch (ArgumentException)
            {
                return new GmTurnOutput(raw!.Trim());
            }
            catch (OverflowException)
            {
                return new GmTurnOutput(raw!.Trim());
            }

            return new GmTurnOutput(message, tell, weakness, weaknessDescription);
        }

        private static Match MatchTellSignal(string block)
        {
            return TellSignalRegex.Match(block);
        }

        private static Match MatchWeaknessSignal(string block)
        {
            return WeaknessSignalRegex.Match(block);
        }

        private static LlmContractException CreateValidatedSignalsParseException(
            string raw,
            Exception exception,
            Action<OperationalDiagnosticEvent>? onDiagnostic)
        {
            bool hasSignalsMarker = HasSignalsMarker(raw);
            int signalCount = CountSignalIndicators(raw);
            var hints = new Dictionary<string, string>
            {
                ["has_signals_marker"] = hasSignalsMarker.ToString(),
                ["exception_type"] = exception.GetType().Name,
                ["signal_count"] = signalCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };

            OperationalDiagnostics.Emit(
                onDiagnostic,
                new OperationalDiagnosticEvent(
                    ParserName,
                    ParseFailureEventName,
                    OperationalDiagnosticSeverity.Error,
                    "Validated GM output signals block failed to parse.",
                    exception,
                    OperationalDiagnosticOperationKind.DateeResponse,
                    OperationalDiagnosticPhaseCode.Parse,
                    OperationalDiagnosticLifecycle.Terminal,
                    OperationalDiagnosticOutcome.Failed,
                    OperationalDiagnostics.ClassifyException(exception),
                    correlationHints: hints));

            return new LlmContractException(
                phase: GmOutputPhase,
                reason: ParseFailureReason,
                message: "Validated GM output signals block failed to parse; refusing to drop gameplay signals silently.",
                provider: null,
                model: null,
                parserName: ParserName,
                signalCount: signalCount);
        }

        private static bool HasSignalsMarker(string raw)
        {
            return raw.IndexOf(SignalsMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CountSignalIndicators(string raw)
        {
            int count = 0;
            if (raw.IndexOf(TellSignalPrefix, StringComparison.OrdinalIgnoreCase) >= 0) count++;
            if (raw.IndexOf(WeaknessSignalPrefix, StringComparison.OrdinalIgnoreCase) >= 0) count++;
            return count;
        }

        private static bool TryParseStat(string raw, out StatType stat)
        {
            var normalized = StatNameNormalizer.NormalizeStatName(raw.Trim());
            try
            {
                stat = (StatType)Enum.Parse(typeof(StatType), normalized, true);
                return true;
            }
            catch (ArgumentException)
            {
                stat = default;
                return false;
            }
            catch (OverflowException)
            {
                stat = default;
                return false;
            }
        }

        private static string StatName(StatType stat)
        {
            // Canonical wire spelling: enum name, with SelfAwareness expanded to
            // the SELF_AWARENESS token the model emits and the normalizer accepts.
            return stat == StatType.SelfAwareness ? "SELF_AWARENESS" : stat.ToString();
        }

        /// <summary>
        /// Strictly validates the optional [SIGNALS] block of datee output if present.
        /// </summary>
        public static DateeSignalsValidationResult ValidateSignalsStrict(string? raw, out string? errorDetail)
        {
            errorDetail = null;
            if (string.IsNullOrEmpty(raw))
            {
                return DateeSignalsValidationResult.NoSignalsBlock;
            }

            int signalsIdx = raw!.IndexOf(SignalsMarker, StringComparison.OrdinalIgnoreCase);
            if (signalsIdx < 0)
            {
                return DateeSignalsValidationResult.NoSignalsBlock;
            }

            if (!HasResponseTextBeforeSignals(raw, signalsIdx))
            {
                errorDetail = "missing_response_text";
                return DateeSignalsValidationResult.MalformedSignals;
            }

            string block = raw.Substring(signalsIdx + SignalsMarker.Length).Trim();

            // Check if block contains TELL: or WEAKNESS: at all
            bool hasTellIndicator = block.IndexOf(TellSignalPrefix, StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasWeaknessIndicator = block.IndexOf(WeaknessSignalPrefix, StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasTellIndicator && !hasWeaknessIndicator)
            {
                errorDetail = "malformed_signals";
                return DateeSignalsValidationResult.MalformedSignals;
            }

            var lines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;

                if (trimmedLine.IndexOf(TellSignalPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var match = TellSignalRegex.Match(trimmedLine);
                    if (!match.Success)
                    {
                        errorDetail = "malformed_signals";
                        return DateeSignalsValidationResult.MalformedSignals;
                    }

                    if (!TryParseStat(match.Groups[1].Value, out _))
                    {
                        errorDetail = "tell_invalid_stat";
                        return DateeSignalsValidationResult.MalformedSignals;
                    }
                }
                else if (trimmedLine.IndexOf(WeaknessSignalPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var match = WeaknessSignalRegex.Match(trimmedLine);
                    if (!match.Success)
                    {
                        errorDetail = "malformed_signals";
                        return DateeSignalsValidationResult.MalformedSignals;
                    }

                    if (!TryParseStat(match.Groups[1].Value, out _))
                    {
                        errorDetail = "weakness_invalid_stat";
                        return DateeSignalsValidationResult.MalformedSignals;
                    }

                    if (!int.TryParse(match.Groups[2].Value.Trim(), out int reduction) || reduction <= 0)
                    {
                        errorDetail = "weakness_missing_dc";
                        return DateeSignalsValidationResult.MalformedSignals;
                    }
                }
            }

            return DateeSignalsValidationResult.ValidSignals;
        }

        private static bool HasResponseTextBeforeSignals(string raw, int signalsIdx)
        {
            string message = raw.Substring(0, signalsIdx).Trim();
            if (message.StartsWith(ResponseMarker, StringComparison.OrdinalIgnoreCase))
            {
                message = message.Substring(ResponseMarker.Length).Trim();
            }

            return !string.IsNullOrWhiteSpace(message);
        }
    }

    /// <summary>
    /// Result of strict signals block validation.
    /// </summary>
    public enum DateeSignalsValidationResult
    {
        NoSignalsBlock,
        ValidSignals,
        MalformedSignals
    }
}
