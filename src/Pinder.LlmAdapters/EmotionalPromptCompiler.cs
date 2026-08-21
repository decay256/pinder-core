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
    /// Public DTO for private emotional direction used to compile the visible performance prompt.
    /// </summary>
    public class EmotionalPrivateDirection
    {
        public EmotionalPrivateDirection(
            string primaryEmotion,
            string intensity,
            string underlyingFeeling,
            string interpretation,
            string impulse,
            string restraint,
            string responsePosture)
        {
            PrimaryEmotion = primaryEmotion;
            Intensity = intensity;
            UnderlyingFeeling = underlyingFeeling;
            Interpretation = interpretation;
            Impulse = impulse;
            Restraint = restraint;
            ResponsePosture = responsePosture;
        }

        public string PrimaryEmotion { get; }
        public string Intensity { get; }
        public string UnderlyingFeeling { get; }
        public string Interpretation { get; }
        public string Impulse { get; }
        public string Restraint { get; }
        public string ResponsePosture { get; }
    }

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
        public const string DirectorSystemWrapperPromptKey =
            "emotional-reaction-director-system-wrapper";
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
            PromptTraceResult directorSystemPrompt = TrimTrace(
                TraceLiteral(prompt.SystemPrompt!, prompt.SourceFile, DirectorPromptKey));
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
            EmotionalPrivateDirection direction,
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
            EmotionalPrivateDirection direction)
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
            builder.Append(
                repairPrompt!.Trim(),
                repair.SourceFile,
                repairKey);
            return TrimTrace(new PromptTraceResult(builder.ToString(), builder.Spans));
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
