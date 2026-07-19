### Finding 1: Default GM System Prompt Still Lives In C# Fallbacks
**File**: `pinder-core/src/Pinder.LlmAdapters/GameDefinition.Defaults.cs:12`
**Issue**: `GameDefinition.PinderDefaults` embeds the full GM system prompt and role descriptions directly in source (`gameMasterPrompt: @"== GAME MASTER == ... You are the Game Master ..."` plus `playerAvatarRoleDescription` and `dateeRoleDescription`). Although the comment labels this as test/dev fallback, `SessionSystemPromptBuilder.BuildPlayerAvatarEx` and `BuildDateeEx` still use `gameDef ?? GameDefinition.PinderDefaults`, and pinder-web's `CharacterSynthesisService` falls back to `GameDefinition.PinderDefaults.GameMasterPrompt` when `_sessions?.GameDefinition` is absent.
**Impact**: A missing or null YAML game definition can still route live LLM/system-prompt behavior through stale C# prose, bypassing the prompt catalog and making prompt review/deployment inconsistent.
**Urgency**: U3 - topic default; maintainability and prompt-governance drift, with evidence of reachable fallback paths but no immediate production failure by itself.
**Fixer-Agent Action Plan**: Move the default GM prompt and role descriptions into `data/prompts` or `game-definition.yaml`, replace `PinderDefaults` prompt text with catalog-loaded defaults or a fail-loud sentinel, update `SessionSystemPromptBuilder` and `CharacterSynthesisService` to require an explicit game definition outside tests, then run the existing prompt-wiring/session-builder tests plus a startup smoke test with the YAML present and absent.

### Finding 2: Dialogue Options Prompt Builder Injects Behavioral Directives Inline
**File**: `pinder-core/src/Pinder.LlmAdapters/SessionDocumentBuilder.Trace.cs:162`
**Issue**: `BuildDialogueOptionsPromptTrace` adds several LLM-facing instructions as raw C# strings instead of catalog entries: `"ACTIVE TRAP INSTRUCTIONS (taint ALL generated options regardless of stat):"`, `"REQUIRED: Include at least one Rizz option."`, `"One option using {stat} should explicitly capitalize on this moment"`, and `"YOUR TEXTING STYLE - follow this exactly, no deviations:"`.
**Impact**: Core dialogue-option steering can drift from the YAML prompt catalog; prompt authors cannot tune these directives without a code change, and prompt trace provenance points at source-generated fragments rather than editable prompt files.
**Urgency**: U3 - topic default; live prompt hygiene issue, but the instructions are deterministic and not a correctness break on their own.
**Fixer-Agent Action Plan**: Add named prompt-template keys for these game-state directives under the prompt catalog, expose them through `PromptTemplates`, render dynamic values with the existing template substitution helper, and update prompt trace tests to assert the new source spans reference the YAML keys.

### Finding 3: Datee Response Length Rule Is Hardcoded In The Live Prompt
**File**: `pinder-core/src/Pinder.LlmAdapters/SessionDocumentBuilder.Trace.cs:527`
**Issue**: The datee response prompt builds `lengthHint` in source with raw model instructions: `"Do not exceed {ceiling} characters regardless of your texting style"` and `"the engine-specified ceiling above takes precedence..."`, then injects it into `PromptTemplates.DateeResponseInstruction`.
**Impact**: The response-length policy is split between catalog text and C# prose, so prompt editors cannot adjust the wording, precedence, or localization of this live instruction from the centralized prompt files.
**Urgency**: U3 - topic default; prompt maintainability drift in a production LLM path.
**Fixer-Agent Action Plan**: Move the length-hint wording into a catalog template such as `datee-response-length-hint`, substitute `{ceiling}` through the catalog rendering helper, and add a trace/provenance assertion that the length hint originates from the prompt catalog.

### Finding 4: Horniness Catastrophe Reinforcement Is Appended From A Loader Constant
**File**: `pinder-core/src/Pinder.LlmAdapters/StatDeliveryInstructions.cs:270`
**Issue**: `StatDeliveryInstructions.LoadFrom` appends `CatastropheReinforcement` as a C# const prompt fragment (`"The structure is a normal Tinder question. The content is the joke. The character is utterly unaware."`) onto the YAML-loaded `horniness_overlay` catastrophe instruction.
**Impact**: The effective prompt sent to the LLM is not fully represented in `data/delivery-instructions.yaml`; reviewers editing the YAML will miss a source-only behavioral override that changes one tier's output.
**Urgency**: U3 - topic default; catalog/source split for a narrow overlay prompt.
**Fixer-Agent Action Plan**: Move the catastrophe reinforcement into the `horniness_overlay.catastrophe` YAML entry or a dedicated shared YAML key, remove the C# const append, and update loader tests to compare the fully composed prompt against YAML-derived content only.

### Finding 5: Character Pursuer Opening Prompt Is Embedded In The Narrative Harness
**File**: `pinder-core/src/Pinder.NarrativeHarness/PursuerActor.cs:90`
**Issue**: `CharacterPursuerActor.OpeningLineAsync` hardcodes the LLM user prompt for the pursuer opening: `"You are texting on a dating app and you are sending the FIRST message..."`, including sentence-count and no-narration instructions, instead of loading it from `data/prompts/narrative.yaml` or the prompt catalog used by the rest of the harness.
**Impact**: Admin/narrative testbed behavior can diverge from centralized prompt content, and changing the pursuer-opening instruction requires rebuilding code rather than editing the prompt catalog.
**Urgency**: U3 - topic default; dev/admin harness prompt drift, not a player-facing production path.
**Fixer-Agent Action Plan**: Add a `pursuer_opening_user_template` key to the narrative prompt YAML/catalog, render `{display_name}` through the existing prompt substitution utility, and update narrative harness tests to assert the template is loaded from data rather than source.

### Finding 6: Generic LLM Pursuer System Prompt Is A Source Constant
**File**: `pinder-core/src/Pinder.NarrativeHarness/PursuerActor.cs:179`
**Issue**: `GenericLlmPursuerActor` defines `private const string PursuerSystem = "You are a witty, curious person texting someone on a dating app..."` and sends it as the system prompt for the generic pursuer fallback.
**Impact**: The fallback persona prompt is hidden in source while the harness otherwise has a YAML-backed narrative prompt path, so prompt changes or reviews can miss one of the available LLM personas.
**Urgency**: U3 - topic default; harness-only fallback, but still a raw system prompt embedded in source.
**Fixer-Agent Action Plan**: Move the generic pursuer system prompt into `data/prompts/narrative.yaml` or a named prompt-catalog entry, inject it into `GenericLlmPursuerActor` during construction, and add a missing-template failure test so the fallback cannot silently recreate source prompt text.

### Finding 7: Datee Harness Turn Prompt Is Duplicated Inline
**File**: `pinder-core/src/Pinder.NarrativeHarness/HarnessRunner.cs:198`
**Issue**: `BuildCharacterUserMessage` hardcodes the datee harness turn instruction: `"You are texting on a dating app. This is the conversation so far. Reply ONLY as yourself..."` with no-narration/stage-direction rules.
**Impact**: The narrative harness has centralized prompt loading for the static narrative prompt but still keeps the recurring turn prompt in code, making harness behavior harder to tune consistently with cataloged prompts.
**Urgency**: U3 - topic default; prompt-catalog hygiene issue in a harness path.
**Fixer-Agent Action Plan**: Add a `datee_turn_user_template` entry to the narrative prompt YAML, render transcript and character-name placeholders through the existing loader, and add a regression test that editing the YAML changes the generated harness user message.

### Finding 8: Confession Menu Injection Text Is Hardcoded Outside The Prompt Catalog
**File**: `pinder-core/src/Pinder.NarrativeHarness/ConfessionMenu.cs:293`
**Issue**: `RenderIngestibleBlock` builds an LLM-ingested instruction block directly in C#: `"Below is your private inventory..."`, `"You are NOT required to disclose any of them"`, and `"Reach for whichever one genuinely fits the moment..."`.
**Impact**: A behaviorally important part of the narrative harness prompt is not represented in `data/prompts/narrative.yaml`, so prompt authors cannot review or revise confession-ingestion rules from the catalog.
**Urgency**: U3 - topic default; harness prompt maintainability issue.
**Fixer-Agent Action Plan**: Move the confession block preamble to the narrative YAML/catalog with placeholders for confession count and menu entries, render it through `NarrativePromptLoader`, and verify generated transcript fixtures still include the same confession list with YAML-sourced provenance.

Audit summary: inspected pinder-core and pinder-web at commits `e96a75f4` and `1a7acb38` for raw LLM prompt/system-instruction strings embedded in source. Counts: U1=0, U2=0, U3=8. No approved-exception suppressions were needed. pinder-web production code mostly proxies, edits, or displays cataloged prompt content; the only dependent-repo prompt-hardcoding evidence found was the `CharacterSynthesisService` fallback to pinder-core's `GameDefinition.PinderDefaults`, covered in Finding 1. Identical-output verification passed for the mirrored Obsidian file.
