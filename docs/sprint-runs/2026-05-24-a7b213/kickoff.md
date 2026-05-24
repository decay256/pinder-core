# Sprint 2026-05-24-a7b213 — Global Monolith Decomposition Sprint

**Authorization:** Daniel via Discord #pinder authorized this comprehensive refactoring run.
Standing-yes for the full drain envelope per SELF-UNBLOCK-BY-DEFAULT.

## Sprint Metadata

- **Sprint id:** `2026-05-24-a7b213`
- **Started:** `2026-05-24T08:20:00Z`
- **Orchestrator model:** `google/gemini-3.5-flash`
- **Status:** Phase 0.5 loading routing policy and running preflight checks
- **Policy:** model-routing.yaml version 6
- **implementer_rung_floor:** 1

## Theme

Decomposition of ALL remaining large monolith files (>500 LOC) in both repositories to enforce strong separation of concerns, keeping all sub-tasks lightweight and Gemma-friendly.

## Scope

The sprint covers the following files ordered by size (descending):

1. **`Refactor-1`:** Refactor `pinder-web/src/pinder-backend/test_main.py` (3048 LOC) to reduce size below 500 LOC.
2. **`Refactor-2`:** Refactor `pinder-web/src/Pinder.GameApi/Services/ActiveSession.cs` (2374 LOC) to reduce size below 500 LOC.
3. **`Refactor-3`:** Refactor `pinder-core/rules/tools/test_rules_pipeline.py` (2054 LOC) to reduce size below 500 LOC.
4. **`Refactor-4`:** Refactor `pinder-web/src/Pinder.GameApi/Controllers/SessionsController.cs` (1971 LOC) to reduce size below 500 LOC.
5. **`Refactor-5`:** Refactor `pinder-core/session-runner/Program.cs` (1963 LOC) to reduce size below 500 LOC.
6. **`Refactor-6`:** Refactor `pinder-core/rules/tools/_enrich.py` (1855 LOC) to reduce size below 500 LOC.
7. **`Refactor-7`:** Refactor `pinder-web/frontend/src/pages/AdminPage.tsx` (1581 LOC) to reduce size below 500 LOC.
8. **`Refactor-8`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ShadowGrowthSpecTests.cs` (1379 LOC) to reduce size below 500 LOC.
9. **`Refactor-9`:** Refactor `pinder-web/src/pinder-backend/main.py` (1219 LOC) to reduce size below 500 LOC.
10. **`Refactor-10`:** Refactor `pinder-web/src/Pinder.GameApi/Data/GameSessionRepository.cs` (1179 LOC) to reduce size below 500 LOC.
11. **`Refactor-11`:** Refactor `pinder-web/frontend/src/pages/GameScreen.tsx` (1122 LOC) to reduce size below 500 LOC.
12. **`Refactor-12`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicLlmAdapterIssue208Tests.cs` (1085 LOC) to reduce size below 500 LOC.
13. **`Refactor-13`:** Refactor `pinder-core/src/Pinder.Core/Conversation/TurnProcessor.cs` (1068 LOC) to reduce size below 500 LOC.
14. **`Refactor-14`:** Refactor `pinder-core/src/Pinder.Core/Conversation/GameSession.cs` (1058 LOC) to reduce size below 500 LOC.
15. **`Refactor-15`:** Refactor `pinder-web/frontend/src/components/TurnResultDisplay.tsx` (1057 LOC) to reduce size below 500 LOC.
16. **`Refactor-16`:** Refactor `pinder-web/src/Pinder.GameApi/Services/TurnAuditWriter.cs` (959 LOC) to reduce size below 500 LOC.
17. **`Refactor-17`:** Refactor `pinder-web/frontend/src/types/session.ts` (958 LOC) to reduce size below 500 LOC.
18. **`Refactor-18`:** Refactor `pinder-web/src/pinder-backend/routes/sessions.py` (943 LOC) to reduce size below 500 LOC.
19. **`Refactor-19`:** Refactor `pinder-core/tests/Pinder.Rules.Tests/SpecComplianceTests.cs` (923 LOC) to reduce size below 500 LOC.
20. **`Refactor-20`:** Refactor `pinder-core/tests/Pinder.Core.Tests/XpTrackingSpecTests.cs` (906 LOC) to reduce size below 500 LOC.
21. **`Refactor-21`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/Issue544_EngineInjectionSpecTests.cs` (900 LOC) to reduce size below 500 LOC.
22. **`Refactor-22`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ComboSpecTests.cs` (898 LOC) to reduce size below 500 LOC.
23. **`Refactor-23`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/SessionDocumentBuilderSpecTests.cs` (890 LOC) to reduce size below 500 LOC.
24. **`Refactor-24`:** Refactor `pinder-web/frontend/src/pages/AnatomyEditor.tsx` (862 LOC) to reduce size below 500 LOC.
25. **`Refactor-25`:** Refactor `pinder-core/tests/Pinder.Core.Tests/PlayerResponseDelaySpecTests.cs` (831 LOC) to reduce size below 500 LOC.
26. **`Refactor-26`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ShadowThresholdSpecTests.cs` (820 LOC) to reduce size below 500 LOC.
27. **`Refactor-27`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicClientSpecTests.cs` (813 LOC) to reduce size below 500 LOC.
28. **`Refactor-28`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ScoringPlayerAgentSpecTests.cs` (807 LOC) to reduce size below 500 LOC.
29. **`Refactor-29`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicLlmAdapterTests.cs` (801 LOC) to reduce size below 500 LOC.
30. **`Refactor-30`:** Refactor `pinder-core/tests/Pinder.Core.Tests/Integration/FullConversationIntegrationTest.cs` (764 LOC) to reduce size below 500 LOC.
31. **`Refactor-31`:** Refactor `pinder-core/tests/Pinder.Core.Tests/Issue415_CharacterDefinitionLoaderSpecTests.cs` (763 LOC) to reduce size below 500 LOC.
32. **`Refactor-32`:** Refactor `pinder-core/tests/Pinder.Core.Tests/PlayerAgentSpecTests.cs` (753 LOC) to reduce size below 500 LOC.
33. **`Refactor-33`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ShadowThresholdGameSessionTests.cs` (750 LOC) to reduce size below 500 LOC.
34. **`Refactor-34`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicLlmAdapterSpecTests.cs` (744 LOC) to reduce size below 500 LOC.
35. **`Refactor-35`:** Refactor `pinder-web/frontend/src/pages/SessionSetupPage.tsx` (735 LOC) to reduce size below 500 LOC.
36. **`Refactor-36`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/OpenAi/OpenAiStreamingTransportTests.cs` (728 LOC) to reduce size below 500 LOC.
37. **`Refactor-37`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/Issue543_SessionSystemPromptSpecTests.cs` (724 LOC) to reduce size below 500 LOC.
38. **`Refactor-38`:** Refactor `pinder-core/tests/Pinder.Core.Tests/Issue836_TextingStyleAggregationRuleTests.cs` (707 LOC) to reduce size below 500 LOC.
39. **`Refactor-39`:** Refactor `pinder-web/src/Pinder.GameApi.Tests/Services/Issue306PrefetchNextTurnTests.cs` (700 LOC) to reduce size below 500 LOC.
40. **`Refactor-40`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ShadowGrowthEventTests.cs` (684 LOC) to reduce size below 500 LOC.
41. **`Refactor-41`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicStreamingTransportTests.cs` (680 LOC) to reduce size below 500 LOC.
42. **`Refactor-42`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ShadowReductionSpecTests.cs` (659 LOC) to reduce size below 500 LOC.
43. **`Refactor-43`:** Refactor `pinder-web/src/Pinder.GameApi.Tests/Services/TurnAuditWriterTests.cs` (656 LOC) to reduce size below 500 LOC.
44. **`Refactor-44`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/SessionDocumentBuilderTests.cs` (649 LOC) to reduce size below 500 LOC.
45. **`Refactor-45`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ShadowReductionTests.cs` (648 LOC) to reduce size below 500 LOC.
46. **`Refactor-46`:** Refactor `pinder-core/src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` (639 LOC) to reduce size below 500 LOC.
47. **`Refactor-47`:** Refactor `pinder-core/tests/Pinder.Core.Tests/Wave0SpecTests.cs` (636 LOC) to reduce size below 500 LOC.
48. **`Refactor-48`:** Refactor `pinder-web/src/Pinder.GameApi/Services/SessionStore.cs` (630 LOC) to reduce size below 500 LOC.
49. **`Refactor-49`:** Refactor `pinder-web/src/Pinder.GameApi.Tests/Controllers/SetupStatusStreamTests.cs` (611 LOC) to reduce size below 500 LOC.
50. **`Refactor-50`:** Refactor `pinder-web/frontend/src/components/CharacterSheetModal.tsx` (609 LOC) to reduce size below 500 LOC.
51. **`Refactor-51`:** Refactor `pinder-web/src/Pinder.GameApi.Tests/Services/ActiveSessionSetupStreamTests.cs` (601 LOC) to reduce size below 500 LOC.
52. **`Refactor-52`:** Refactor `pinder-web/frontend/src/i18n/generated/en.ts` (597 LOC) to reduce size below 500 LOC.
53. **`Refactor-53`:** Refactor `pinder-web/src/Pinder.GameApi.Tests/Controllers/ShareControllerTests.cs` (593 LOC) to reduce size below 500 LOC.
54. **`Refactor-54`:** Refactor `pinder-web/src/pinder-backend/routes/endpoints/admin_content_write.py` (592 LOC) to reduce size below 500 LOC.
55. **`Refactor-55`:** Refactor `pinder-core/tests/Pinder.Core.Tests/LlmPlayerAgentTests.cs` (591 LOC) to reduce size below 500 LOC.
56. **`Refactor-56`:** Refactor `pinder-web/src/Pinder.GameApi.Tests/Data/ActiveSessionPersistenceTests.cs` (590 LOC) to reduce size below 500 LOC.
57. **`Refactor-57`:** Refactor `pinder-web/src/Pinder.GameApi.Tests/Controllers/ReplayPrivacyTests.cs` (588 LOC) to reduce size below 500 LOC.
58. **`Refactor-58`:** Refactor `pinder-core/src/Pinder.LlmAdapters/OpenAi/OpenAiLlmAdapter.cs` (583 LOC) to reduce size below 500 LOC.
59. **`Refactor-59`:** Refactor `pinder-web/src/Pinder.GameApi/Models/TurnDtos.cs` (582 LOC) to reduce size below 500 LOC.
60. **`Refactor-60`:** Refactor `pinder-core/tests/Pinder.RemoteAssets.Tests/EigencoreCharacterStoreWriteTests.cs` (575 LOC) to reduce size below 500 LOC.
61. **`Refactor-61`:** Refactor `pinder-core/tests/Pinder.Core.Tests/Issue463_RuleResolverWiringTests.cs` (573 LOC) to reduce size below 500 LOC.
62. **`Refactor-62`:** Refactor `pinder-core/tests/Pinder.Core.Tests/TrapTaintInjectionTests.cs` (571 LOC) to reduce size below 500 LOC.
63. **`Refactor-63`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ScoringPlayerAgentShadowTests.cs` (570 LOC) to reduce size below 500 LOC.
64. **`Refactor-64`:** Refactor `pinder-core/tests/Pinder.Core.Tests/Issue351_PickReasoningTests.cs` (558 LOC) to reduce size below 500 LOC.
65. **`Refactor-65`:** Refactor `pinder-web/frontend/e2e/turn-progress.spec.ts` (556 LOC) to reduce size below 500 LOC.
66. **`Refactor-66`:** Refactor `pinder-web/src/Pinder.GameApi.Tests/Services/Issue501FastModeRegressionTests.cs` (553 LOC) to reduce size below 500 LOC.
67. **`Refactor-67`:** Refactor `pinder-core/tests/Pinder.Core.Tests/ScoringPlayerAgentTests.cs` (547 LOC) to reduce size below 500 LOC.
68. **`Refactor-68`:** Refactor `pinder-web/frontend/src/api/client.ts` (546 LOC) to reduce size below 500 LOC.
69. **`Refactor-69`:** Refactor `pinder-web/frontend/src/components/OptionSelectionWidget.tsx` (543 LOC) to reduce size below 500 LOC.
70. **`Refactor-70`:** Refactor `pinder-core/tests/Pinder.Rules.Tests/GameDefinitionYamlContentTests.cs` (543 LOC) to reduce size below 500 LOC.
71. **`Refactor-71`:** Refactor `pinder-core/tests/Pinder.Core.Tests/RulesSpec/RulesSpecValidationTests.cs` (540 LOC) to reduce size below 500 LOC.
72. **`Refactor-72`:** Refactor `pinder-core/src/Pinder.Core/Prompts/TextingStyleAggregator.cs` (531 LOC) to reduce size below 500 LOC.
73. **`Refactor-73`:** Refactor `pinder-web/frontend/src/pages/AdminPage.uiHelpers.tsx` (530 LOC) to reduce size below 500 LOC.
74. **`Refactor-74`:** Refactor `pinder-web/src/pinder-backend/tests/test_admin_content_anatomy.py` (522 LOC) to reduce size below 500 LOC.
75. **`Refactor-75`:** Refactor `pinder-core/tests/Pinder.Core.Tests/TellBonusTests.cs` (519 LOC) to reduce size below 500 LOC.
76. **`Refactor-76`:** Refactor `pinder-web/src/Pinder.GameApi/Services/CharacterRepository.cs` (513 LOC) to reduce size below 500 LOC.
77. **`Refactor-77`:** Refactor `pinder-core/src/Pinder.LlmAdapters/GameDefinition.cs` (513 LOC) to reduce size below 500 LOC.
78. **`Refactor-78`:** Refactor `pinder-core/tests/Pinder.LlmAdapters.Tests/EngineInjectionBlockTests.cs` (511 LOC) to reduce size below 500 LOC.
79. **`Refactor-79`:** Refactor `pinder-web/src/Pinder.GameApi.Tests/Services/Issue305TextDiffsTests.cs` (510 LOC) to reduce size below 500 LOC.

## Running configuration

All upstream progress events will be reported continuously to the originating Discord channel `#pinder`.
No markdown tables will be used in updates, adhering to user's layout preferences.
WORKSPACE-ISOLATION is enforced: each subagent runs in its own `/tmp/work-*` worktree.
All tickets start at Rung 1 (Gemini 3.5 Flash) directly on the Google native API.
The run continues sequentially through all tickets, using context-budget-abort to save state and resume if the 180k token guard triggers.