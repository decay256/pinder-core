#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

python3 "$script_dir/verify-agent-journal-provenance-builders.py"

trx_dir="$repo_root/TestResults"
mkdir -p "$trx_dir"
dotnet test "$repo_root/tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj" \
  --filter "FullyQualifiedName~PromptBuilderPropagationTests" \
  --logger "trx;LogFileName=CORE-1378.trx" \
  --results-directory "$trx_dir"

python3 - "$trx_dir/CORE-1378.trx" <<'PY'
import pathlib
import sys
import xml.etree.ElementTree as ET

trx = pathlib.Path(sys.argv[1])
if not trx.exists():
    raise SystemExit(f"Expected TRX was not written: {trx}")
root = ET.parse(trx).getroot()
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
results = [
    result for result in root.findall(".//t:UnitTestResult", ns)
    if "PromptBuilderPropagationTests" in (result.attrib.get("testName") or "")
]
if not results:
    raise SystemExit("CORE-1378 TRX result count was zero.")
if any(result.attrib.get("outcome") != "Passed" for result in results):
    raise SystemExit("CORE-1378 TRX has failing PromptBuilderPropagationTests results.")
groups = {
    name for result in results for name in ["1", "2", "3", "4", "5"]
    if f"AC{name}_" in result.attrib.get("testName", "")
}
if len(groups) != 5:
    raise SystemExit(f"CORE-1378 TRX AC group count {len(groups)} != 5.")
print(f"CORE-1378 trx_result_count={len(results)}")
print(f"CORE-1378 trx_ac_group_count={len(groups)}")
PY
