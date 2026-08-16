#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

results_dir="${RESULTS_DIR:-$repo/TestResults/agent-journal-provenance-mechanism}"
trx_name="agent-journal-provenance-mechanism.trx"
trx_path="$results_dir/$trx_name"

section() {
  printf '\n== %s ==\n' "$1"
}

section "scope/no-call-site modification check"
allowed_paths=(
  "src/Pinder.Core/Text/AnnotatedInvocationDocument.cs"
  "src/Pinder.Core/Text/AnnotatedInvocationDocumentBuilder.cs"
  "src/Pinder.LlmAdapters/AgentJournals/PromptProvenanceAdapter.cs"
  "tests/Pinder.LlmAdapters.Tests/AgentJournals/Provenance/AnnotatedInvocationDocumentTests.cs"
  "scripts/verify-agent-journal-provenance-mechanism.sh"
  "scripts/verify-agent-journal-provenance-mechanism.ps1"
)

is_allowed_path() {
  local candidate="$1"
  local allowed
  for allowed in "${allowed_paths[@]}"; do
    if [[ "$candidate" == "$allowed" ]]; then
      return 0
    fi
  done
  return 1
}

changed_paths="$(
  {
    git diff --name-only --diff-filter=ACMRTUXB
    git ls-files --others --exclude-standard
  } | sort -u
)"

scope_errors=0
while IFS= read -r path; do
  [[ -z "$path" ]] && continue
  case "$path" in
    *.orig|*.rej)
      printf 'quarantine artifact ignored: %s\n' "$path"
      continue
      ;;
  esac
  if is_allowed_path "$path"; then
    printf 'owned change: %s\n' "$path"
  else
    printf 'unexpected change outside CORE-1374 ownership: %s\n' "$path" >&2
    scope_errors=1
  fi
done <<< "$changed_paths"
if [[ "$scope_errors" -ne 0 ]]; then
  exit 20
fi

section "netstandard release builds"
dotnet build src/Pinder.Core/Pinder.Core.csproj --configuration Release --nologo
dotnet build src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj --configuration Release --nologo

section "focused provenance and #1370 regression tests"
mkdir -p "$results_dir"
dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~AnnotatedInvocationDocumentTests|FullyQualifiedName~AgentJournalPiCodecRoundTripTests|FullyQualifiedName~AgentJournalPiContextIsolationTests" \
  --logger "trx;LogFileName=$trx_name" \
  --results-directory "$results_dir" \
  --nologo

section "AC group, #1370, and fixture/hash determinism counts"
python3 - "$trx_path" <<'PY'
import sys
import xml.etree.ElementTree as ET

trx_path = sys.argv[1]
root = ET.parse(trx_path).getroot()
ns = {"trx": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
results = root.findall(".//trx:UnitTestResult", ns)
if not results:
    raise SystemExit("TRX contained zero executed tests")

def name(result):
    return result.attrib.get("testName", "")

def count_contains(fragment):
    return sum(1 for result in results if fragment in name(result))

failures = []
for group in ("AC1_", "AC2_", "AC3_", "AC4_", "AC5_"):
    count = count_contains(group)
    print(f"{group}tests={count}")
    if count == 0:
        failures.append(f"{group} had zero tests")

ac2_raw_identity = count_contains(
    "AC2_AdjacentMergeableRangesWithWrongDocumentId_ReportRangeDocumentMismatch"
)
print(f"ac2_raw_range_identity_regression_tests={ac2_raw_identity}")
if ac2_raw_identity != 1:
    failures.append(
        f"raw-range identity regression expected 1 test, found {ac2_raw_identity}")

round_trip = (
    count_contains("Invocation_RoundTripsThroughPiCustomEntryAndMatchesFixture")
    + count_contains("Result_RoundTripsThroughPiCustomEntryAndMatchesFixture")
    + count_contains("MessageLink_RoundTripsThroughPiCustomEntryAndMatchesFixture")
)
context_isolation = count_contains("DiagnosticEntries_ContributeZeroMessagesThroughPiSessionContextBuilder")
fixture_hash = count_contains("AC4_CanonicalJsonAndHash_AreStableAcrossRuns")
print(f"issue1370_round_trip_tests={round_trip}")
print(f"issue1370_context_isolation_tests={context_isolation}")
print(f"fixture_hash_determinism_tests={fixture_hash}")
if round_trip == 0:
    failures.append("#1370 round-trip/context fixture tests had zero tests")
if context_isolation == 0:
    failures.append("#1370 context isolation tests had zero tests")
if fixture_hash == 0:
    failures.append("fixture/hash determinism tests had zero tests")

outcomes = {}
for result in results:
    outcome = result.attrib.get("outcome", "unknown")
    outcomes[outcome] = outcomes.get(outcome, 0) + 1
print("outcomes=" + ",".join(f"{key}:{outcomes[key]}" for key in sorted(outcomes)))
for result in results:
    if result.attrib.get("outcome") != "Passed":
        failures.append(f"{name(result)} outcome={result.attrib.get('outcome')}")
if failures:
    for failure in failures:
        print(failure, file=sys.stderr)
    raise SystemExit(21)
PY

section "git diff check"
git diff --check

section "verifier complete"
printf 'trx=%s\n' "$trx_path"
