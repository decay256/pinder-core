using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Non-sending compilation result for the private emotional director request.
    /// </summary>
    public sealed class CompiledEmotionalDirectorPrompt
    {
        internal CompiledEmotionalDirectorPrompt(
            PromptTraceResult compiledReactionInput,
            PromptTraceResult systemPrompt,
            PromptTraceResult userPrompt,
            double? temperature,
            int? maxTokens,
            IReadOnlyDictionary<string, string> metadata)
        {
            CompiledReactionInput = compiledReactionInput;
            SystemPrompt = systemPrompt;
            UserPrompt = userPrompt;
            Temperature = temperature;
            MaxTokens = maxTokens;
            Metadata = metadata;
        }

        public PromptTraceResult CompiledReactionInput { get; }
        public PromptTraceResult SystemPrompt { get; }
        public PromptTraceResult UserPrompt { get; }
        public double? Temperature { get; }
        public int? MaxTokens { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }
    }

    /// <summary>
    /// Complete non-sending emotional prompt compilation used by admin previews.
    /// </summary>
    public sealed class CompiledEmotionalPrompts
    {
        internal CompiledEmotionalPrompts(
            CompiledEmotionalDirectorPrompt director,
            PromptTraceResult performancePrompt)
        {
            Director = director;
            PerformancePrompt = performancePrompt;
        }

        public PromptTraceResult CompiledReactionInput => Director.CompiledReactionInput;
        public CompiledEmotionalDirectorPrompt Director { get; }
        public PromptTraceResult PerformancePrompt { get; }
    }

    /// <summary>
    /// Compiles emotional director and performance prompts without sending an LLM request.
    /// </summary>
    public sealed class EmotionalPromptCompiler
    {
        public const string DirectorPromptKey = "emotional-reaction-director";
        public const string DirectorContractRepairPromptKey =
            "emotional-reaction-director-repair-contract";
        public const string DirectorDraftedChatReplyRepairPromptKey =
            "emotional-reaction-director-repair-drafted-chat-reply";
        public const string DirectorResponsePostureOmitsPrimaryEmotionRepairPromptKey =
            "emotional-reaction-director-repair-response-posture-omits-primary-emotion";
        public const string DirectorUnsupportedPrimaryEmotionRepairPromptKey =
            "emotional-reaction-director-repair-unsupported-primary-emotion";
        public const string DirectorSystemWrapperPromptKey =
            "emotional-reaction-director-system-wrapper";
        public const string PreviousDirectionEmptyPromptKey =
            "emotional-reaction-previous-direction-empty";
        public const string PreviousDirectionLinePromptKey =
            "emotional-reaction-previous-direction-line";
        public const string DateeResponseRepetitionRepairPromptKey =
            "datee-response-repetition-repair";
        private const string CompiledInputPlaceholder = "{compiled_reaction_input}";

        private readonly PromptCatalog _catalog;

        public EmotionalPromptCompiler(PromptCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public CompiledEmotionalDirectorPrompt CompileDirector(DateeContext context)
            => CompileDirector(
                context,
                includeConversationHistory: true,
                dateeSystemPrompt: null);

        public CompiledEmotionalDirectorPrompt CompileDirector(
            DateeContext context,
            bool includeConversationHistory,
            string? dateeSystemPrompt)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            PromptEntry prompt = _catalog.RequireCompleteEntry(
                DirectorPromptKey,
                "prompt-catalog: missing required runtime prompt key 'emotional-reaction-director'. The yaml file is incomplete or missing.");
            PromptTraceResult compiledInput = new EmotionalReactionEventCompiler(_catalog).Compile(
                context,
                _catalog,
                includeConversationHistory);
            PromptTraceResult directorSystemPrompt = CompileDirectorRulesPrompt(prompt, context);
            PromptTraceResult systemPrompt = string.IsNullOrWhiteSpace(dateeSystemPrompt)
                ? directorSystemPrompt
                : CompileDirectorSystemPrompt(dateeSystemPrompt!, directorSystemPrompt);
            PromptTraceResult userPrompt = CompileDirectorUserPrompt(prompt, compiledInput);

            return new CompiledEmotionalDirectorPrompt(
                compiledInput,
                systemPrompt,
                userPrompt,
                prompt.Temperature,
                prompt.MaxTokens,
                BuildDirectorMetadata(prompt, compiledInput, context));
        }

        public PromptTraceResult CompilePerformance(
            DateeContext context,
            CharacterEmotionalDirection direction,
            bool includeConversationHistory = true)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (direction == null) throw new ArgumentNullException(nameof(direction));
            return SessionDocumentBuilder.BuildDateePerformancePromptEx(
                context,
                direction,
                _catalog,
                includeConversationHistory);
        }

        public CompiledEmotionalPrompts CompileScenario(
            DateeContext context,
            CharacterEmotionalDirection direction)
        {
            CompiledEmotionalDirectorPrompt director = CompileDirector(context);
            PromptTraceResult performance = CompilePerformance(context, direction);
            return new CompiledEmotionalPrompts(director, performance);
        }

        internal PromptTraceResult CompileDirectorRetrySystemPrompt(
            PromptTraceResult baseSystemPrompt,
            string rejectionReason)
        {
            string repairKey = rejectionReason switch
            {
                "response_posture_omits_primary_emotion" => DirectorResponsePostureOmitsPrimaryEmotionRepairPromptKey,
                "unsupported_primary_emotion" => DirectorUnsupportedPrimaryEmotionRepairPromptKey,
                "drafted_chat_reply" => DirectorDraftedChatReplyRepairPromptKey,
                _ => DirectorContractRepairPromptKey,
            };
            PromptEntry repair = _catalog.TryGet(repairKey)
                ?? throw new InvalidOperationException(
                    $"prompt-catalog: missing required runtime prompt key '{repairKey}'. The yaml file is incomplete or missing.");
            string? repairPrompt = repair.SystemPrompt;
            if (string.IsNullOrWhiteSpace(repairPrompt))
            {
                throw new InvalidOperationException(
                    $"prompt-catalog: runtime prompt key '{repairKey}' has no system_prompt. Check the yaml file.");
            }

            var builder = new AnnotatedStringBuilder();
            builder.Append(baseSystemPrompt);
            builder.Append("\n\n");

            const string vocabularyPlaceholder = "{emotion_vocabulary}";
            string trimmedRepairPrompt = repairPrompt!.Trim();
            int vocabIndex = trimmedRepairPrompt.IndexOf(vocabularyPlaceholder, StringComparison.Ordinal);
            if (vocabIndex >= 0)
            {
                PromptEntry vocabulary = _catalog.TryGet(CharacterEmotionCatalog.PromptKey)!;
                builder.Append(
                    trimmedRepairPrompt.Substring(0, vocabIndex),
                    repair.SourceFile,
                    repairKey);
                builder.Append(
                    string.Join(", ", CharacterEmotionCatalog.Load(_catalog)),
                    vocabulary.SourceFile,
                    CharacterEmotionCatalog.PromptKey);
                builder.Append(
                    trimmedRepairPrompt.Substring(vocabIndex + vocabularyPlaceholder.Length),
                    repair.SourceFile,
                    repairKey);
            }
            else
            {
                builder.Append(
                    trimmedRepairPrompt,
                    repair.SourceFile,
                    repairKey);
            }

            return TrimTrace(new PromptTraceResult(builder.ToString(), builder.Spans));
        }

        private PromptTraceResult CompileDirectorRulesPrompt(PromptEntry prompt, DateeContext context)
        {
            return RenderTemplateWithTrace(
                prompt.SystemPrompt!,
                prompt.SourceFile,
                DirectorPromptKey,
                new Dictionary<string, PromptTraceResult>(StringComparer.Ordinal)
                {
                    {
                        "{emotion_vocabulary}",
                        TraceLiteral(
                            string.Join(", ", CharacterEmotionCatalog.Load(_catalog)),
                            _catalog.TryGet(CharacterEmotionCatalog.PromptKey)!.SourceFile,
                            CharacterEmotionCatalog.PromptKey)
                    },
                    {
                        "{previous_accepted_directions}",
                        CompilePreviousAcceptedDirections(context)
                    },
                });
        }

        private static PromptTraceResult CompileDirectorUserPrompt(
            PromptEntry prompt,
            PromptTraceResult compiledInput)
        {
            string template = prompt.UserTemplate!;
            var builder = new AnnotatedStringBuilder();
            int cursor = 0;
            int placeholderIndex;

            while ((placeholderIndex = template.IndexOf(
                CompiledInputPlaceholder,
                cursor,
                StringComparison.Ordinal)) >= 0)
            {
                builder.Append(
                    template.Substring(cursor, placeholderIndex - cursor),
                    prompt.SourceFile,
                    DirectorPromptKey);
                builder.Append(compiledInput);
                cursor = placeholderIndex + CompiledInputPlaceholder.Length;
            }

            if (cursor == 0)
            {
                throw new InvalidOperationException(
                    "emotional-reaction-director must include {compiled_reaction_input}.");
            }

            builder.Append(template.Substring(cursor), prompt.SourceFile, DirectorPromptKey);
            return TrimTrace(new PromptTraceResult(builder.ToString(), builder.Spans));
        }

        internal PromptTraceResult CompilePerformanceRepetitionRepairPrompt(PromptTraceResult basePrompt)
        {
            PromptEntry repair = _catalog.TryGet(DateeResponseRepetitionRepairPromptKey)
                ?? throw new InvalidOperationException(
                    $"prompt-catalog: missing required runtime prompt key '{DateeResponseRepetitionRepairPromptKey}'. The yaml file is incomplete or missing.");
            if (string.IsNullOrWhiteSpace(repair.SystemPrompt))
            {
                throw new InvalidOperationException(
                    $"prompt-catalog: runtime prompt key '{DateeResponseRepetitionRepairPromptKey}' has no system_prompt. Check the yaml file.");
            }

            var builder = new AnnotatedStringBuilder();
            builder.Append(basePrompt);
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(repair.SystemPrompt!.Trim(), repair.SourceFile, DateeResponseRepetitionRepairPromptKey);
            return TrimTrace(new PromptTraceResult(builder.ToString(), builder.Spans));
        }

        private PromptTraceResult CompilePreviousAcceptedDirections(DateeContext context)
        {
            if (context.PreviousAcceptedEmotionalDirections.Count == 0)
            {
                PromptEntry empty = _catalog.TryGet(PreviousDirectionEmptyPromptKey)
                    ?? throw new InvalidOperationException(
                        $"prompt-catalog: missing required runtime prompt key '{PreviousDirectionEmptyPromptKey}'.");
                return TraceLiteral(empty.SystemPrompt!.Trim(), empty.SourceFile, PreviousDirectionEmptyPromptKey);
            }

            PromptEntry line = _catalog.TryGet(PreviousDirectionLinePromptKey)
                ?? throw new InvalidOperationException(
                    $"prompt-catalog: missing required runtime prompt key '{PreviousDirectionLinePromptKey}'.");
            var builder = new AnnotatedStringBuilder();
            foreach (CharacterEmotionalDirectionSummary summary in context.PreviousAcceptedEmotionalDirections)
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.Append(
                    PromptCatalog.Substitute(
                        line.SystemPrompt!.Trim(),
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["turn"] = summary.Turn.ToString(CultureInfo.InvariantCulture),
                            ["primary_emotion"] = summary.PrimaryEmotion,
                            ["secondary_emotion"] = summary.SecondaryEmotion,
                            ["regulatory_state"] = summary.RegulatoryState,
                            ["activation"] = summary.Activation.ToString(CultureInfo.InvariantCulture),
                            ["trajectory"] = summary.Trajectory,
                            ["impulse"] = summary.Impulse,
                        }),
                    line.SourceFile,
                    PreviousDirectionLinePromptKey);
            }

            return new PromptTraceResult(builder.ToString(), builder.Spans);
        }

        private static PromptTraceResult RenderTemplateWithTrace(
            string template,
            string sourceFile,
            string key,
            IReadOnlyDictionary<string, PromptTraceResult> values)
        {
            foreach (string placeholder in values.Keys)
            {
                if (template.IndexOf(placeholder, StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        $"prompt-catalog: runtime prompt key '{key}' must include required token '{placeholder}'.");
                }
            }

            var builder = new AnnotatedStringBuilder();
            int cursor = 0;
            while (cursor < template.Length)
            {
                KeyValuePair<string, PromptTraceResult>? next = null;
                int nextIndex = template.Length;
                foreach (KeyValuePair<string, PromptTraceResult> value in values)
                {
                    int index = template.IndexOf(value.Key, cursor, StringComparison.Ordinal);
                    if (index >= 0 && index < nextIndex)
                    {
                        next = value;
                        nextIndex = index;
                    }
                }

                if (!next.HasValue)
                {
                    builder.Append(template.Substring(cursor), sourceFile, key);
                    break;
                }

                builder.Append(template.Substring(cursor, nextIndex - cursor), sourceFile, key);
                builder.Append(next.Value.Value);
                cursor = nextIndex + next.Value.Key.Length;
            }

            return TrimTrace(new PromptTraceResult(builder.ToString(), builder.Spans));
        }

        private PromptTraceResult CompileDirectorSystemPrompt(
            string dateeSystemPrompt,
            PromptTraceResult directorSystemPrompt)
        {
            PromptEntry wrapper = _catalog.TryGet(DirectorSystemWrapperPromptKey)
                ?? throw new InvalidOperationException(
                    $"prompt-catalog: missing required runtime prompt key '{DirectorSystemWrapperPromptKey}'. The yaml file is incomplete or missing.");
            if (string.IsNullOrWhiteSpace(wrapper.SystemPrompt))
            {
                throw new InvalidOperationException(
                    $"prompt-catalog: runtime prompt key '{DirectorSystemWrapperPromptKey}' has no system_prompt. Check the yaml file.");
            }

            var values = new Dictionary<string, PromptTraceResult>(StringComparer.Ordinal)
            {
                {
                    "{datee_system_prompt}",
                    TraceLiteral(
                        dateeSystemPrompt,
                        PromptTraceDiagnosticContract.RuntimeDateeContextSource,
                        "DateeSystemPrompt")
                },
                { "{director_system_prompt}", directorSystemPrompt },
            };
            foreach (string placeholder in values.Keys)
            {
                if (wrapper.SystemPrompt!.IndexOf(placeholder, StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        $"prompt-catalog: runtime prompt key '{DirectorSystemWrapperPromptKey}' must include required token '{placeholder}'.");
                }
            }

            var builder = new AnnotatedStringBuilder();
            string template = wrapper.SystemPrompt!;
            int cursor = 0;
            while (cursor < template.Length)
            {
                KeyValuePair<string, PromptTraceResult>? next = null;
                int nextIndex = template.Length;
                foreach (var value in values)
                {
                    int index = template.IndexOf(value.Key, cursor, StringComparison.Ordinal);
                    if (index >= 0 && index < nextIndex)
                    {
                        next = value;
                        nextIndex = index;
                    }
                }

                if (!next.HasValue)
                {
                    builder.Append(template.Substring(cursor), wrapper.SourceFile, DirectorSystemWrapperPromptKey);
                    break;
                }

                builder.Append(
                    template.Substring(cursor, nextIndex - cursor),
                    wrapper.SourceFile,
                    DirectorSystemWrapperPromptKey);
                builder.Append(next.Value.Value);
                cursor = nextIndex + next.Value.Key.Length;
            }

            return TrimTrace(new PromptTraceResult(builder.ToString(), builder.Spans));
        }

        private static PromptTraceResult TrimTrace(PromptTraceResult trace)
        {
            string trimmedText = trace.Text.Trim();
            int leadingTrimCount = trace.Text.Length - trace.Text.TrimStart().Length;
            int retainedEnd = leadingTrimCount + trimmedText.Length;
            AnnotatedSpan[] trimmedSpans = trace.Spans
                .Select(span => new
                {
                    Span = span,
                    Start = Math.Max(span.Start, leadingTrimCount),
                    End = Math.Min(span.End, retainedEnd),
                })
                .Where(item => item.End > item.Start)
                .Select(item => new AnnotatedSpan(
                    item.Start - leadingTrimCount,
                    item.End - leadingTrimCount,
                    item.Span.SourceFile,
                    item.Span.Key))
                .ToArray();

            return new PromptTraceResult(trimmedText, trimmedSpans);
        }

        private static PromptTraceResult TraceLiteral(string text, string? sourceFile, string key)
            => new PromptTraceResult(
                text,
                new[] { new AnnotatedSpan(0, text.Length, sourceFile, key) });

        private static IReadOnlyDictionary<string, string> BuildDirectorMetadata(
            PromptEntry prompt,
            PromptTraceResult compiled,
            DateeContext context)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "phase", LlmPhase.EmotionalDirector },
                { "prompt_key", DirectorPromptKey },
                { "system_prompt_source", prompt.SourceFile ?? string.Empty },
                { "user_template_source", prompt.SourceFile ?? string.Empty },
                { "turn", context.CurrentTurn.ToString(CultureInfo.InvariantCulture) },
            };

            string sources = string.Join(
                ",",
                compiled.Spans
                    .Select(span => span.SourceFile ?? string.Empty)
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Where(PromptTraceDiagnosticContract.IsSafeSource)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(source => source, StringComparer.Ordinal));
            if (sources.Length > 0)
            {
                metadata["compiled_input_sources"] = sources;
            }

            string keys = string.Join(
                ",",
                compiled.Spans
                    .Select(span => span.Key ?? string.Empty)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Where(PromptTraceDiagnosticContract.IsSafeTraceKey)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(key => key, StringComparer.Ordinal));
            if (keys.Length > 0)
            {
                metadata["compiled_input_keys"] = keys;
            }

            return metadata;
        }
    }
}
