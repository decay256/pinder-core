> Scope: changed-code audit topic 6/11 (`prompt-hardcoding`) over the 22 explicitly listed current changed files in `A:\Data\ClaudeCodex\pinder-web\pinder-core`.

### Finding 1: DATEE response length instruction remains hardcoded in the runtime builder
**File**: `src/Pinder.LlmAdapters/SessionDocumentBuilder.Trace.cs:577`
**Issue**: `BuildDateePromptCore` constructs reusable LLM instruction prose in C# instead of loading it from prompt config:
`string lengthHint = $"Keep it to a natural text-message length. " + $"Do not exceed {ceiling} characters regardless of your texting style. " + $"The texting-style length axis in your system prompt is a stylistic guideline, NOT a hard engine cap - " + $"the engine-specified ceiling above takes precedence over any style axis that would run longer.";`
The rendered text is then injected into the YAML-backed `datee-response-instruction` through `{length_hint}` at `src/Pinder.LlmAdapters/SessionDocumentBuilder.Trace.cs:611`, while the prompt catalog only owns the placeholder.
**Impact**: This prompt wording is not admin-editable and can drift from the YAML catalog despite the sprint's requirement that reusable prompt prose live in configurable prompt data. It also weakens trace/source attribution because the span is attributed to `datee-response-instruction` even though the actual instruction text is source-owned.
**Urgency**: U3 - topic default; the behavior is currently intentional and bounded, but it is prompt/config hygiene debt on the DATEE performance path touched by this sprint.
**Fixer-Agent Action Plan**: Move the reusable length-guidance prose into `data/prompts/templates.yaml` as a catalog entry or nested template that accepts `{ceiling}`. Have `SessionDocumentBuilder` render that configured template, trace the span to the new prompt key/source, and leave C# responsible only for computing the numeric ceiling.
