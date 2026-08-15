#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

results_dir="$repo/TestResults/issue-1370"
mkdir -p "$results_dir"

run_required_tests() {
  local project="$1"
  local filter="$2"
  local log_name="$3"
  shift 3
  local trx="$results_dir/$log_name"

  dotnet test "$project" \
    --filter "$filter" \
    --results-directory "$results_dir" \
    --logger "trx;LogFileName=$log_name"

  if [[ ! -f "$trx" ]]; then
    echo "TRX was not produced: $trx" >&2
    exit 1
  fi

  python3 - "$trx" "$@" <<'PYVERIFY'
import sys
import xml.etree.ElementTree as ET

trx = sys.argv[1]
required = sys.argv[2:]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = ET.parse(trx).getroot()
classes = {}
for unit in root.findall(".//t:UnitTest", ns):
    method = unit.find("t:TestMethod", ns)
    if method is None:
        continue
    class_name = method.attrib.get("className", "")
    for required_name in required:
        if required_name in class_name:
            classes[required_name] = classes.get(required_name, 0) + 1
for required_name in required:
    count = classes.get(required_name, 0)
    if count < 1:
        raise SystemExit(f"Filter matched zero tests for required class {required_name} in {trx}")
    print(f"{required_name} matched {count} tests")
PYVERIFY
}

run_required_tests \
  "tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj" \
  "FullyQualifiedName~AgentJournals" \
  "core-agent-journals.trx" \
  "AgentJournalAttemptValidationTests" \
  "AgentJournalContractValidationTests" \
  "AgentJournalCredentialShapeSecurityTests" \
  "AgentJournalSourceIdentitySecurityTests" \
  "AgentJournalStateValidationTests" \
  "AgentJournalUtf16BoundaryValidationTests" \
  "PromptTraceAgentJournalAdapterTests" \
  "PromptTraceAgentJournalSecurityTests" \
  "PromptTraceSourceIdentityResolverTests"

run_required_tests \
  "tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj" \
  "FullyQualifiedName~AgentJournals|FullyQualifiedName~Issue1066_PromptTraceTests|FullyQualifiedName~PromptTraceCallBindingTests" \
  "adapters-agent-journals.trx" \
  "AgentJournalPiCodecRoundTripTests" \
  "AgentJournalPiCompatibilityTests" \
  "AgentJournalPiContextIsolationTests" \
  "AgentJournalPiInvalidPayloadTests" \
  "AgentJournalPiUtf16BoundaryTests" \
  "Issue1066_PromptTraceTests" \
  "PromptTraceCallBindingTests"

fixture_dir="tests/Pinder.LlmAdapters.Tests/Fixtures/AgentJournals"
for fixture in llm-invocation.v1.json llm-result.v1.json message-link.v1.json; do
  path="$fixture_dir/$fixture"
  [[ -f "$path" ]] || { echo "Missing canonical fixture $fixture" >&2; exit 1; }
  grep -Eq '"[a-z0-9_]+":' "$path" || { echo "Fixture does not look snake_case: $fixture" >&2; exit 1; }
  echo "fixture present: $fixture"
done

core_fixture="tests/Pinder.Core.Tests/Fixtures/AgentJournals/structural-prompt-trace.json"
[[ -f "$core_fixture" ]] || { echo "Missing structural PromptTrace fixture" >&2; exit 1; }
grep -Fq '"source_file":"data/prompts/structural.yaml"' "$core_fixture" \
  || { echo "Structural PromptTrace fixture does not use the production source path" >&2; exit 1; }
echo "fixture present: structural-prompt-trace.json"

docs="docs/agent-journals.md"
for required in \
  "pinder.llm-invocation.v1" \
  "pinder.llm-result.v1" \
  "pinder.message-link.v1" \
  "UTF-16" \
  'Unknown future `pinder.*`' \
  "netstandard2.0" \
  "filesystem paths" \
  "zero provider-context"; do
  grep -Fq "$required" "$docs" || { echo "docs/agent-journals.md is missing required text: $required" >&2; exit 1; }
done

dotnet build Pinder.Core.sln
git diff --check

echo "issue #1370 verifier completed"
