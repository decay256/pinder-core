#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

results_dir="${RESULTS_DIR:-artifacts/test-results/issue-1372-host-compatible}"
mkdir -p "$results_dir"

parse_trx() {
  local trx_path="$1"
  local group_name="$2"
  local minimum_count="$3"
  python3 - "$trx_path" "$group_name" "$minimum_count" <<'PY'
import sys
import xml.etree.ElementTree as ET

trx_path, group_name, minimum_count = sys.argv[1], sys.argv[2], int(sys.argv[3])
root = ET.parse(trx_path).getroot()
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
counters = root.find(".//t:Counters", ns)
if counters is None:
    raise SystemExit(f"{group_name}: TRX counters were not found in {trx_path}")
executed = int(counters.attrib.get("executed", "0"))
failed = int(counters.attrib.get("failed", "0"))
if executed < minimum_count:
    raise SystemExit(
        f"{group_name}: expected at least {minimum_count} executed tests, got {executed}"
    )
if failed != 0:
    raise SystemExit(f"{group_name}: expected zero failed tests, got {failed}")
print(f"{group_name}: executed={executed} failed={failed} trx={trx_path}")
PY
}

run_test_group() {
  local group_name="$1"
  local filter="$2"
  local minimum_count="$3"
  local trx_name="issue-1372-${group_name}.trx"
  local group_results_dir="$results_dir/$group_name"

  mkdir -p "$group_results_dir"
  dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj \
    --filter "$filter" \
    --logger "trx;LogFileName=$trx_name" \
    --results-directory "$group_results_dir"

  parse_trx "$group_results_dir/$trx_name" "$group_name" "$minimum_count"
}

run_test_group \
  "materializer-branch" \
  "FullyQualifiedName~Pinder.LlmAdapters.Tests.AgentJournals.Materialization.AgentJournalMaterializerBranchGoldenTests" \
  7

run_test_group \
  "materializer-compatibility" \
  "FullyQualifiedName~Pinder.LlmAdapters.Tests.AgentJournals.Materialization.AgentJournalMaterializerCompatibilityTests" \
  9

run_test_group \
  "materializer-side-effects" \
  "FullyQualifiedName~Pinder.LlmAdapters.Tests.AgentJournals.Materialization.AgentJournalMaterializerSideEffectTests" \
  7

run_test_group \
  "pi-session" \
  "FullyQualifiedName~Pinder.LlmAdapters.Tests.Issue54_PiConversationSessionTests" \
  2

required_fixtures=(
  ambiguous-roots.snapshot.json
  child-before-parent.snapshot.json
  cycle.snapshot.json
  duplicate-ids.snapshot.json
  invalid-parentage.snapshot.json
  parent-first-equivalent.snapshot.json
  self-parent.snapshot.json
)
snapshot_fixture_dir="tests/Pinder.LlmAdapters.Tests/AgentJournals/Materialization/Fixtures/snapshots"
for fixture in "${required_fixtures[@]}"; do
  if [ ! -f "$snapshot_fixture_dir/$fixture" ]; then
    echo "Missing required tree-validation fixture: $fixture" >&2
    exit 1
  fi
done
echo "fixture-inventory: ${#required_fixtures[@]} required tree fixtures present"

fixture_dir="tests/Pinder.LlmAdapters.Tests/AgentJournals/Materialization/Fixtures/normalized"
first_hashes="$results_dir/normalized-fixtures.first.sha256"
second_hashes="$results_dir/normalized-fixtures.second.sha256"

sha256sum "$fixture_dir"/*.json | sort > "$first_hashes"
sha256sum "$fixture_dir"/*.json | sort > "$second_hashes"
cmp -s "$first_hashes" "$second_hashes"
echo "normalized-fixtures: deterministic hash comparison passed"
cat "$first_hashes"

materializer_sources=(src/Pinder.LlmAdapters/AgentJournals/AgentJournalMaterializer*.cs)
for source in "${materializer_sources[@]}"; do
  if [ ! -f "$source" ]; then
    echo "Missing production materializer source: $source" >&2
    exit 1
  fi
done

forbidden_pattern='\bTransport\b|PiLlmTransport|PiProviderTransportFactory|BuildContextAsync|AgentHarness|PiConversationSession|Fixture'
if rg -n "$forbidden_pattern" "${materializer_sources[@]}"; then
  echo "Production materializer references forbidden provider/live-session/context/fixture symbols." >&2
  exit 1
fi
echo "static-boundary: no forbidden provider/live-session/context/fixture references"

dotnet build Pinder.Core.sln

git diff --check

echo "verify-issue-1372 host-compatible verifier passed"
