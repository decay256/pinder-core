#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

results_dir="$repo/TestResults/issue-1371"
mkdir -p "$results_dir"

trx="$results_dir/agent-journal-recorder.trx"
dotnet test "tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj" \
  --filter "FullyQualifiedName~AgentJournals.Recording" \
  --results-directory "$results_dir" \
  --logger "trx;LogFileName=agent-journal-recorder.trx"

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
unit_names = []
result_names = []
for unit in root.findall(".//t:UnitTest", ns):
    method = unit.find("t:TestMethod", ns)
    if method is not None:
        unit_names.append(method.attrib.get("className", "") + "." + method.attrib.get("name", ""))
for result in root.findall(".//t:UnitTestResult", ns):
    result_names.append(result.attrib.get("testName", ""))

checks = {
    "AC1 dual projection identity": [
        ["StartAndAcceptedCompletion_ProjectSameRecordInstancesToPiAndHost"],
        ["RecorderProjection_AppendsJournalCustomEntriesWithoutProviderContext"],
    ],
    "AC2 accepted terminal": [["TerminalMatrix_EmitsExactlyOneTerminalResult", "accepted"]],
    "AC2 validation rejected terminal": [["TerminalMatrix_EmitsExactlyOneTerminalResult", "validation_rejected"]],
    "AC2 provider failed terminal": [["TerminalMatrix_EmitsExactlyOneTerminalResult", "provider_failed"]],
    "AC2 cancelled terminal": [["TerminalMatrix_EmitsExactlyOneTerminalResult", "cancelled"]],
    "AC2 disposed abandoned terminal": [["DisposeWithoutCompletion_EmitsAbandonedOnce"]],
    "AC3 idempotency and separate attempts": [["StableRecordIds_MakeDuplicateHostDeliveryIdempotent"]],
    "AC4 best effort sink policy": [["BestEffortSinkFailure_EmitsDiagnosticAndPreservesTerminalState"]],
    "AC4 fail closed sink policy": [["FailClosedSinkFailure_ThrowsTypedPersistenceFailureBeforeAcceptance"]],
    "AC4 sink-after-Pi retry matrix": [["SinkPolicyAfterPiProjectionMatrix_FailClosedRetriesHostWithoutDuplicatePiProjection"]],
    "AC4 invocation start post-Pi retry": [["InvocationStartPostPiHostFailure_RetryIsSingleFlightAndReturnsSameAttempt"]],
    "AC4 explicit start retry contract": [["RetryStartWithoutPendingInvocation_FailsExplicitly"]],
    "AC5 Pi projection failure": [["PiProjectionFailure_PreventsSuccessfulJournalCommit"]],
    "AC5 failed terminal retry": [["FailedTerminalProjection_DoesNotCacheCompletionAndCanRetry"]],
    "AC5 validation-before-cache retry": [["InvalidTerminalPayload_DoesNotCacheCompletionBeforeValidationSucceeds"]],
    "AC6 cancellation bound": [["CancelledTerminalCleanup_UsesBoundedIndependentToken"]],
    "AC7 immutable input snapshot": [["InvocationSnapshot_IsolatedFromCallerDocumentAndRangeMutation"]],
    "AC7 context isolation": [["RecorderProjection_AppendsJournalCustomEntriesWithoutProviderContext"]],
    "AC7 null Agent Session projection": [["NullAgentSession_SkipsPiProjectionButStillAllowsHostSink"]],
}

all_names = unit_names + result_names
for label, groups in checks.items():
    total = 0
    for needles in groups:
        count = sum(1 for name in all_names if all(needle in name for needle in needles))
        if count < 1:
            raise SystemExit(f"{label} matched zero tests for {needles} in {trx}")
        total += count
    print(f"{label}: {total}")
PYVERIFY

dotnet build Pinder.Core.sln

scan_files=(
  "src/Pinder.Core/Diagnostics/AgentJournals/AgentJournalRecorder.cs"
  "src/Pinder.Core/Diagnostics/AgentJournals/IAgentJournalSink.cs"
)

for f in "${scan_files[@]}"; do
  [[ -f "$f" ]] || { echo "Missing recorder file: $f" >&2; exit 1; }
done

if rg -n "Anthropic|OpenAI|Gemini|Claude|ILlmTransport|LlmTransport|Pinder\\.LlmAdapters|Pi\\.Agent|Pi\\.AI|HttpClient|provider-specific" "${scan_files[@]}"; then
  echo "Provider-specific namespace/type leaked into provider-neutral recorder." >&2
  exit 1
fi
echo "provider-neutral recorder static scan passed"

if rg -n "SendWithDiagnosticsAsync|SendStructuredWithDiagnosticsAsync|GetDateeResponseAsync|GenerateDialogueOptionsAsync|ApplyFailureCorruptionAsync|SnapshotRecordingLlmTransport|RecordingLlmTransport" \
  src/Pinder.Core/Diagnostics/AgentJournals \
  src/Pinder.LlmAdapters/AgentJournals \
  tests/Pinder.LlmAdapters.Tests/AgentJournals/Recording; then
  echo "Verifier scope appears to claim or perform runtime call-site wiring." >&2
  exit 1
fi
echo "runtime call-site wiring static absence check passed"

if ! rg -n "CustomEntry|AppendCustomEntryAsync" src/Pinder.LlmAdapters/AgentJournals/PiAgentJournalProjectionSink.cs >/dev/null; then
  echo "Pi projection adapter does not append custom entries." >&2
  exit 1
fi
echo "Pi custom-entry projection scan passed"

git diff --check
untracked_owned_files=(
  "scripts/verify-issue-1371.ps1"
  "scripts/verify-issue-1371.sh"
  "src/Pinder.Core/Diagnostics/AgentJournals/AgentJournalRecorder.cs"
  "src/Pinder.Core/Diagnostics/AgentJournals/IAgentJournalSink.cs"
  "src/Pinder.LlmAdapters/AgentJournals/PiAgentJournalProjectionSink.cs"
  "tests/Pinder.LlmAdapters.Tests/AgentJournals/Recording/AgentJournalRecorderLifecycleTests.cs"
  "tests/Pinder.LlmAdapters.Tests/AgentJournals/Recording/PiAgentJournalProjectionSinkTests.cs"
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
echo "untracked owned-file whitespace check passed"

echo "issue #1371 verifier completed"
