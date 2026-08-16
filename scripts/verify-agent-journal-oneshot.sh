#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

results_dir="$repo/TestResults/agent-journal-oneshot"
mkdir -p "$results_dir"

trx="$results_dir/core-1376-oneshot.trx"
dotnet test "tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj" \
  --filter "FullyQualifiedName~GameRunOneShotJournalWiringTests" \
  --results-directory "$results_dir" \
  --logger "trx;LogFileName=core-1376-oneshot.trx"

dotnet test "tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj" \
  --filter "FullyQualifiedName~AdoptStateFrom_RequiredTurnClone_PreservesOneShotFactoryForFutureSteering" \
  --results-directory "$results_dir" \
  --logger "trx;LogFileName=core-1376-required-turn-adoption.trx"

if [[ ! -f "$trx" ]]; then
  echo "TRX was not produced: $trx" >&2
  exit 1
fi

python3 - "$trx" <<'PYVERIFY'
import sys
import xml.etree.ElementTree as ET

trx = sys.argv[1]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = ET.parse(trx).getroot()
result_names = [
    result.attrib.get("testName", "")
    for result in root.findall(".//t:UnitTestResult", ns)
]

groups = [
    "dialogue_options",
    "dramatic_arc_setup",
    "success_improvement",
    "steering_question",
    "horniness_question",
    "retry",
    "provider_failure",
    "validation_or_skipped_output",
    "cancellation",
    "abandoned_or_disposal",
    "excluded_owner_guard",
    "dormant_interest_guard",
    "legacy_parity_guard",
]

for group in groups:
    count = sum(1 for name in result_names if group in name)
    if count < 1:
        raise SystemExit(f"{group}: matched zero tests in {trx}")
    print(f"{group}: {count}")
PYVERIFY

python3 <<'PYSTATIC'
from pathlib import Path
import sys

repo = Path.cwd()

def fail(message: str) -> None:
    print(message, file=sys.stderr)
    raise SystemExit(1)

def read(relative: str) -> str:
    path = repo / relative
    if not path.exists():
        fail(f"Missing required file: {relative}")
    return path.read_text()

adapter = read("src/Pinder.LlmAdapters/PinderLlmAdapter.cs")
setup = read("src/Pinder.SessionSetup/LlmDramaticArcGenerator.cs")
records = read("src/Pinder.Core/Diagnostics/AgentJournals/AgentJournalRecords.cs")
validation = read("src/Pinder.Core/Diagnostics/AgentJournals/AgentJournalValidation.cs")
recorder = read("src/Pinder.Core/Diagnostics/AgentJournals/AgentJournalRecorder.cs")
turn = read("src/Pinder.Core/Conversation/TurnOrchestrator.cs")
delivery = read("src/Pinder.Core/Conversation/DeliveryStage.cs")
steering = read("src/Pinder.Core/Conversation/SteeringEngine.cs")
game_session = read("src/Pinder.Core/Conversation/GameSession.cs")
game_session_clone = read("src/Pinder.Core/Conversation/GameSession.Clone.cs")
game_config = read("src/Pinder.Core/Conversation/GameSessionConfig.cs")
factory = read("src/Pinder.Core/Diagnostics/AgentJournals/GameRunOneShotJournalContextFactory.cs")
usage_provider = read("src/Pinder.Core/Interfaces/ITokenUsageProvider.cs")
builder = read("src/Pinder.LlmAdapters/AgentJournals/GameRunPromptDocumentBuilder.cs")
tests = read("tests/Pinder.LlmAdapters.Tests/AgentJournals/Recording/GameRunOneShotJournalWiringTests.cs")

required_test_names = [
    "dialogue_options_records_no_session_owner_and_output",
    "dramatic_arc_setup_records_no_session_owner_and_output",
    "success_improvement_records_replacement",
    "steering_question_records_append_check_context",
    "horniness_question_records_append_check_context",
    "retry_records_distinct_attempts",
    "provider_failure_records_failed_terminal",
    "validation_or_skipped_output_records_rejection_or_skips_without_provider",
    "validation_or_skipped_output_success_improvement_without_template_records_zero_usage_skip",
    "cancellation_records_cancelled_terminal",
    "abandoned_or_disposal_records_abandoned_no_session",
    "excluded_owner_guard_rejects_forbidden_agent_session_in_no_session_context",
    "dormant_interest_guard_static_no_production_callers",
    "legacy_parity_guard_prompt_traces_remain",
    "fail_closed_when_context_is_supplied_without_sink",
    "production_call_site_wiring_guard",
    "excluded_owner_guard_rejects_unsafe_host_correlation_identifiers",
]
for name in required_test_names:
    if name not in tests:
        fail(f"Missing focused regression scenario: {name}")
print("13 amended scenario tests are present")

required_execution_classes = [
    "game.dialogue-options",
    "game.setup.dramatic-arc",
    "game.delivery.success-improvement",
    "game.delivery.steering-question",
    "game.delivery.horniness-question",
]
for execution_class in required_execution_classes:
    if execution_class not in tests:
        fail(f"Missing execution class coverage: {execution_class}")
print("five approved live no-session Game Run execution classes are covered")

for needle in [
    "AgentJournalOneShotContext",
    "GameRunBundleOwner",
    "JournalDestination",
    "ExecutionClass",
    "OutputLinkId",
    "Context",
    "public string? AgentSessionId",
    "agentSessionId: null",
]:
    if needle not in records:
        fail(f"Missing no-session owner/correlation field in records: {needle}")

if "ForbiddenOwnerId" not in validation or "forbidden_owner_id" not in validation:
    fail("Validator is missing fail-closed forbidden owner/session guard")
if "Agent journal Pi projection requires a real Agent Session id." not in recorder:
    fail("Recorder no-session Pi projection fail-closed guard is missing")
print("static owner/no-fake-Pi guards passed")

production_checks = {
    "TurnOrchestrator dialogue options": (turn, ["GameRunOneShotJournalTaxonomy.DialogueOptions", "agentJournal: CreateAgentJournalContext("]),
    "DeliveryStage success improvement": (delivery, ["GameRunOneShotJournalTaxonomy.SuccessImprovement", "GameRunDeliveryOneShotRecord"]),
    "DeliveryStage horniness question": (delivery, ["GameRunOneShotJournalTaxonomy.HorninessQuestion", "GameRunDeliveryAppendOneShotRecord"]),
    "SteeringEngine steering question": (steering, ["GameRunOneShotJournalTaxonomy.SteeringQuestion", "agentJournal: CreateAgentJournalContext("]),
    "GameSession host composition": (game_session, ["config.AgentJournalOneShotContextFactory", "_agentJournalOneShotContextFactory"]),
    "GameSessionConfig host contract": (game_config, ["IAgentJournalOneShotContextFactory", "agentJournalOneShotContextFactory"]),
    "dramatic arc host composition": (setup, ["ResolveAgentJournalContext", "GameRunOneShotJournalTaxonomy.DramaticArcSetup", "AgentJournalOneShotContextFactory"]),
}
for label, (source, needles) in production_checks.items():
    for needle in needles:
        if needle not in source:
            fail(f"Missing production one-shot wiring for {label}: {needle}")
print("production call-site wiring guards passed")

adoption_start = game_session_clone.find("var preparedSteeringEngine = new SteeringEngine(")
adoption_end = game_session_clone.find("var preparedHorninessEngine", adoption_start)
if adoption_start < 0 or adoption_end < 0:
    fail("Required-turn adoption steering reconstruction was not found")
adoption_body = game_session_clone[adoption_start:adoption_end]
if "_agentJournalOneShotContextFactory" not in adoption_body:
    fail("Required-turn adoption does not preserve the one-shot context factory")
print("required-turn adoption one-shot factory guard passed")

for needle in ["TokenUsageMeasurement.Start(_transport)", "ToAgentJournalUsage(usageMeasurement)", "ValidateOneShotJournalConfiguration(context.AgentJournal)"]:
    if needle not in adapter:
        fail(f"Adapter usage/fail-closed guard missing: {needle}")
for needle in ["TokenUsageMeasurement.Start(_transport)", "ToAgentJournalUsage(usageMeasurement)", "ValidateOneShotJournalConfiguration(agentJournal)", "catch (Exception ex)"]:
    if needle not in setup:
        fail(f"Dramatic-arc usage/terminal/fail-closed guard missing: {needle}")
for needle in ["public sealed class TokenUsageMeasurement", "SessionTokenUsage? Complete()"]:
    if needle not in usage_provider:
        fail(f"Provider-neutral usage abstraction missing: {needle}")
if tests.count("AssertUsage(sink.Result)") < 5:
    fail("All five live one-shot tests must assert persisted usage")
print("usage propagation, fail-closed pairing, and broad terminal guards passed")
for needle in ["skipped_no_template", "CompleteValidationRejectedOneShotAsync", "new AgentJournalUsage(0, 0, 0)"]:
    if needle not in adapter:
        fail(f"Success-improvement skip journaling guard missing: {needle}")
print("success-improvement skip terminal guard passed")

correlation_fields = [
    "GameRunId", "AgentSessionId", "InvocationId", "OperationId", "AttemptId",
    "RequestId", "TurnId", "BranchId", "Owner", "JournalDestination",
    "ExecutionClass", "OutputLinkId",
]
for field in correlation_fields:
    if f"AddForbiddenLink(correlation.{field}" not in validation:
        fail(f"Host-controlled correlation field bypasses opaque identifier policy: {field}")
print("host-controlled correlation identifier guards passed")

adapter_checks = {
    "GetDialogueOptionsAsync": ["BuildDialogueOptions", "StartOneShotJournalAsync", "CompleteAcceptedOneShotAsync"],
    "GetSuccessImprovementAsync": ["BuildSuccessImprovementDocuments", "StartOneShotJournalAsync", "CompleteAcceptedOneShotAsync"],
    "GetSteeringQuestionAsync": ["BuildSteeringQuestionDocuments", "StartOneShotJournalAsync", "CompleteAcceptedOneShotAsync"],
    "GetHorninessQuestionAsync": ["BuildHorninessQuestionDocuments", "StartOneShotJournalAsync", "CompleteAcceptedOneShotAsync"],
}
for method, needles in adapter_checks.items():
    start = adapter.find(method)
    if start < 0:
        fail(f"Missing adapter method: {method}")
    next_method = min([pos for marker in adapter_checks if marker != method for pos in [adapter.find(marker, start + len(method))] if pos >= 0] or [len(adapter)])
    body = adapter[start:next_method]
    for needle in needles:
        if needle not in body:
            fail(f"{method} bypasses required one-shot journal wiring: {needle}")

for needle in ["BuildDramaticArcDocuments", "StartOneShotJournalAsync", "CompleteAcceptedOneShotAsync"]:
    if needle not in setup:
        fail(f"Dramatic arc setup bypasses required one-shot journal wiring: {needle}")
print("static bypass guards passed")

allowed_interest_files = {
    "src/Pinder.Core/Interfaces/ILlmAdapter.cs",
    "src/Pinder.Core/Conversation/NullLlmAdapter.cs",
    "src/Pinder.LlmAdapters/PinderLlmAdapter.cs",
}
interest_matches = []
for path in (repo / "src").rglob("*.cs"):
    rel = path.relative_to(repo).as_posix()
    if "GetInterestChangeBeatAsync(" in path.read_text() and rel not in allowed_interest_files:
        interest_matches.append(rel)
if interest_matches:
    fail("Dormant interest-change production caller appeared: " + ", ".join(sorted(interest_matches)))
print("dormant interest-change guard passed")

legacy_needles = [
    'InMemoryPromptTraceService.Instance.RecordTrace("dialogue-options"',
    "BuildSuccessImprovementDocuments",
    "BuildSteeringQuestionDocuments",
    "BuildHorninessQuestionDocuments",
]
for needle in legacy_needles:
    if needle not in builder:
        fail(f"Legacy parity/final document builder marker missing: {needle}")
print("legacy parity guard passed")
PYSTATIC

git diff --check

untracked_owned_files=(
  "scripts/verify-agent-journal-oneshot.ps1"
  "scripts/verify-agent-journal-oneshot.sh"
  "src/Pinder.Core/Diagnostics/AgentJournals/GameRunOneShotJournalContextFactory.cs"
  "tests/Pinder.LlmAdapters.Tests/AgentJournals/Recording/GameRunOneShotJournalWiringTests.cs"
)
untracked_check_failed=0
for f in "${untracked_owned_files[@]}"; do
  if [[ -f "$f" && "$(git ls-files --others --exclude-standard -- "$f")" == "$f" ]]; then
    output="$(git diff --check --no-index /dev/null "$f" 2>&1 || true)"
    if [[ -n "$output" ]]; then
      echo "$output"
      untracked_check_failed=1
    fi
  fi
done
if [[ "$untracked_check_failed" -ne 0 ]]; then
  exit 1
fi

echo "agent journal one-shot verifier completed"
