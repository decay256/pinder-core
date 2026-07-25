using System;
using System.Text.Json;

namespace Pinder.LlmAdapters
{
    public enum GeneratedJsonObjectExtractionFailureCode
    {
        None = 0,
        EmptyInput,
        NoObject,
        UnterminatedObject,
        InputTooLarge,
        ObjectTooLarge,
        NoValidObject
    }

    public sealed class GeneratedJsonObjectExtractionOptions
    {
        public const int DefaultMaxInputChars = 64 * 1024;
        public const int DefaultMaxObjectChars = 64 * 1024;

        internal static readonly GeneratedJsonObjectExtractionOptions Default =
            new GeneratedJsonObjectExtractionOptions(DefaultMaxInputChars, DefaultMaxObjectChars);

        public GeneratedJsonObjectExtractionOptions(
            int maxInputChars = DefaultMaxInputChars,
            int maxObjectChars = DefaultMaxObjectChars)
        {
            if (maxInputChars <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxInputChars), "maxInputChars must be greater than zero.");
            if (maxObjectChars <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxObjectChars), "maxObjectChars must be greater than zero.");

            MaxInputChars = maxInputChars;
            MaxObjectChars = maxObjectChars;
        }

        public int MaxInputChars { get; }

        public int MaxObjectChars { get; }
    }

    public sealed class GeneratedJsonObjectExtractionResult
    {
        private GeneratedJsonObjectExtractionResult(
            bool success,
            string? json,
            GeneratedJsonObjectExtractionFailureCode failureCode,
            int? candidateStartIndex,
            int? candidateEndIndexExclusive)
        {
            Success = success;
            Json = json;
            FailureCode = failureCode;
            CandidateStartIndex = candidateStartIndex;
            CandidateEndIndexExclusive = candidateEndIndexExclusive;
        }

        public bool Success { get; }

        public string? Json { get; }

        public GeneratedJsonObjectExtractionFailureCode FailureCode { get; }

        public int? CandidateStartIndex { get; }

        public int? CandidateEndIndexExclusive { get; }

        internal static GeneratedJsonObjectExtractionResult Accepted(
            string json,
            int candidateStartIndex,
            int candidateEndIndexExclusive)
        {
            return new GeneratedJsonObjectExtractionResult(
                true,
                json,
                GeneratedJsonObjectExtractionFailureCode.None,
                candidateStartIndex,
                candidateEndIndexExclusive);
        }

        internal static GeneratedJsonObjectExtractionResult Failed(
            GeneratedJsonObjectExtractionFailureCode failureCode,
            int? candidateStartIndex = null,
            int? candidateEndIndexExclusive = null)
        {
            return new GeneratedJsonObjectExtractionResult(
                false,
                null,
                failureCode,
                candidateStartIndex,
                candidateEndIndexExclusive);
        }
    }

    /// <summary>
    /// Extracts the first complete, syntactically valid JSON object from generated text.
    /// Validation, retry policy, and domain exception mapping remain caller-owned.
    /// </summary>
    public static class GeneratedJsonObjectExtractor
    {
        public static GeneratedJsonObjectExtractionResult TryExtractFirstValidObject(
            string? text,
            GeneratedJsonObjectExtractionOptions? options = null)
        {
            var effectiveOptions = options ?? GeneratedJsonObjectExtractionOptions.Default;

            if (string.IsNullOrWhiteSpace(text))
                return GeneratedJsonObjectExtractionResult.Failed(
                    GeneratedJsonObjectExtractionFailureCode.EmptyInput);

            if (text!.Length > effectiveOptions.MaxInputChars)
                return GeneratedJsonObjectExtractionResult.Failed(
                    GeneratedJsonObjectExtractionFailureCode.InputTooLarge);

            bool sawCandidateStart = false;
            for (int start = FindNextJsonValueStart(text, 0);
                 start >= 0 && start < text.Length;
                 start = FindNextJsonValueStart(text, start + 1))
            {
                sawCandidateStart = true;

                var scan = text[start] == '['
                    ? ScanArrayCandidate(text, start, effectiveOptions.MaxObjectChars)
                    : ScanObjectCandidate(text, start, effectiveOptions.MaxObjectChars);
                if (scan.ObjectTooLarge)
                {
                    return GeneratedJsonObjectExtractionResult.Failed(
                        GeneratedJsonObjectExtractionFailureCode.ObjectTooLarge,
                        start);
                }

                if (!scan.IsBalanced)
                {
                    return GeneratedJsonObjectExtractionResult.Failed(
                        GeneratedJsonObjectExtractionFailureCode.UnterminatedObject,
                        start);
                }

                var candidate = text.Substring(start, scan.EndIndexExclusive - start);
                try
                {
                    using var document = JsonDocument.Parse(candidate);
                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return GeneratedJsonObjectExtractionResult.Accepted(
                            candidate,
                            start,
                            scan.EndIndexExclusive);
                    }

                    if (document.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        return GeneratedJsonObjectExtractionResult.Failed(
                            GeneratedJsonObjectExtractionFailureCode.NoValidObject,
                            start,
                            scan.EndIndexExclusive);
                    }
                }
                catch (JsonException)
                {
                    // Keep scanning: generated text often contains an invalid
                    // first object-shaped fragment before a usable JSON object.
                }
            }

            if (!sawCandidateStart)
                return GeneratedJsonObjectExtractionResult.Failed(
                    GeneratedJsonObjectExtractionFailureCode.NoObject);

            return GeneratedJsonObjectExtractionResult.Failed(
                GeneratedJsonObjectExtractionFailureCode.NoValidObject);
        }

        public static string ExtractFirstValidObjectOrThrow(
            string? text,
            GeneratedJsonObjectExtractionOptions? options = null)
        {
            var result = TryExtractFirstValidObject(text, options);
            if (result.Success)
                return result.Json!;

            throw new JsonException(
                $"Generated text did not contain a valid JSON object. FailureCode={result.FailureCode}.");
        }

        private static int FindNextJsonValueStart(string text, int startIndex)
        {
            for (int i = startIndex; i < text.Length; i++)
            {
                if (text[i] == '{' || text[i] == '[')
                    return i;
            }

            return -1;
        }

        private static CandidateScan ScanObjectCandidate(string text, int start, int maxObjectChars)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < text.Length; i++)
            {
                if (i - start + 1 > maxObjectChars)
                    return CandidateScan.TooLarge();

                char c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return CandidateScan.Balanced(i + 1);
                }
            }

            return CandidateScan.Unterminated();
        }

        private static CandidateScan ScanArrayCandidate(string text, int start, int maxObjectChars)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < text.Length; i++)
            {
                if (i - start + 1 > maxObjectChars)
                    return CandidateScan.TooLarge();

                char c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return CandidateScan.Balanced(i + 1);
                }
            }

            return CandidateScan.Unterminated();
        }

        private struct CandidateScan
        {
            private CandidateScan(bool isBalanced, bool objectTooLarge, int endIndexExclusive)
            {
                IsBalanced = isBalanced;
                ObjectTooLarge = objectTooLarge;
                EndIndexExclusive = endIndexExclusive;
            }

            public bool IsBalanced { get; }

            public bool ObjectTooLarge { get; }

            public int EndIndexExclusive { get; }

            public static CandidateScan Balanced(int endIndexExclusive)
            {
                return new CandidateScan(true, false, endIndexExclusive);
            }

            public static CandidateScan TooLarge()
            {
                return new CandidateScan(false, true, -1);
            }

            public static CandidateScan Unterminated()
            {
                return new CandidateScan(false, false, -1);
            }
        }
    }
}
