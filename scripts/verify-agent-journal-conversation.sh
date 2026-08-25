#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

if [ "$#" -gt 0 ]; then
  results_dir="$1"
elif [ -n "${EIGENTAKT_KEEP_DIR:-}" ]; then
  results_dir="${EIGENTAKT_KEEP_DIR}/CORE-1375"
else
  results_dir="${repo}/TestResults/agent-journal-conversation"
fi
mkdir -p "$results_dir"
stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
trx_name="core-1375-conversation-$stamp.trx"
trx="$results_dir/$trx_name"
log="$results_dir/core-1375-conversation-$stamp.log"

python3 - <<'PY'
from pathlib import Path
import re

root = Path.cwd()
inventory_path = root / "src/Pinder.LlmAdapters/AgentJournals/GameRunConversationJournalInventory.cs"
inventory = inventory_path.read_text(encoding="utf-8")
expected = {
    "game.datee.performance",
    "game.avatar.reply",
    "game.emotional-director",
    "game.avatar.emotional-director",
    "game.prefetch.option-branch",
    "game.speculation.option-branch",
    "game.emotional-director.branch-disposed",
}
actual = set(re.findall(r'public const string \w+ = "([^"]+)";', inventory))
if actual != expected:
    raise SystemExit(f"approved call-path inventory mismatch: expected={sorted(expected)} actual={sorted(actual)}")

test_path = root / "tests/Pinder.LlmAdapters.Tests/AgentJournals/Recording/Issue1375_ConversationJournalWiringTests.cs"
tests = test_path.read_text(encoding="utf-8")
for forbidden in ("CompleteSyntheticAsync", "new AgentJournalRecorder"):
    if forbidden in tests:
        raise SystemExit(f"CORE-1375 lifecycle matrix bypasses production adapter via {forbidden}")

required_fragments = {
    "src/Pinder.Core/Conversation/GameSession.cs": (
        "config.AgentJournalContext ?? new GameRunAgentJournalContext",
        '"game-run-" + Guid.NewGuid()',
    ),
    "src/Pinder.Core/Conversation/GameSession.Clone.cs": (
        "public GameSession Clone(",
        "GameRunConversationBranchKind branchKind",
        "branchKind != GameRunConversationBranchKind.Prefetch",
        "branchKind != GameRunConversationBranchKind.Speculative",
        "_agentJournalContext.ForBranch",
    ),
    "tests/Pinder.Core.Tests/Phase5/Phase5_FastGameplayInvariantTests.cs": (
        "GameRunConversationBranchKind.Prefetch",
        "GameRunConversationBranchKind.Speculative",
        "AdapterReplacingClone_RejectsAmbiguousBranchPurpose",
    ),
    "src/Pinder.LlmAdapters/PinderLlmAdapter.cs": (
        "ResolveConversationCallPath(context.AgentJournalContext",
        "GameRunConversationJournalInventory.DateePerformance",
        "GameRunConversationJournalInventory.AvatarReply",
        "StartBranchDisposalJournalAsync",
    ),
    "src/Pinder.LlmAdapters/PinderLlmAdapter.EmotionalDirector.cs": (
        "GameRunConversationJournalInventory.EmotionalDirector",
    ),
    "src/Pinder.LlmAdapters/PinderLlmAdapter.CharacterEmotionalDirector.cs": (
        "entryIds.AssistantEntryId",
        "CompleteAcceptedAsync(responseText, semanticEntryId)",
    ),
    "src/Pinder.LlmAdapters/PinderLlmAdapter.AvatarEmotionalDirector.cs": (
        "GameRunConversationJournalInventory.AvatarEmotionalDirector",
        "avatar-private-analysis",
    ),
    "src/Pinder.LlmAdapters/PinderLlmAdapter.AgentJournals.cs": (
        "GameRunConversationBranchKind.Prefetch",
        "GameRunConversationBranchKind.Speculative",
        "GameRunConversationJournalInventory.DirectorBranchDisposed",
        "AgentJournalCallScope.Disabled",
    ),
}
for relative, fragments in required_fragments.items():
    text = (root / relative).read_text(encoding="utf-8")
    missing = [fragment for fragment in fragments if fragment not in text]
    if missing:
        raise SystemExit(f"static no-bypass check failed for {relative}: missing {missing}")

clone_source = (root / "src/Pinder.Core/Conversation/GameSession.Clone.cs").read_text(encoding="utf-8")
if re.search(r"public\s+GameSession\s+Clone\s*\(\s*ILlmAdapter\s+llm\s*\)", clone_source):
    raise SystemExit("ambiguous Clone(ILlmAdapter) can silently preserve main journal context")
for forbidden in ("CloneForPrefetch", "CloneForSpeculation"):
    if forbidden in clone_source:
        raise SystemExit(f"test-only clone boundary remains in production source: {forbidden}")
if "parent.Clone(" not in tests or "GameRunConversationBranchKind.Prefetch" not in tests \
        or "GameRunConversationBranchKind.Speculative" not in tests:
    raise SystemExit("journal scenarios do not exercise the explicit production clone boundary")

production = "\n".join(
    path.read_text(encoding="utf-8")
    for base in (root / "src/Pinder.Core", root / "src/Pinder.LlmAdapters")
    for path in base.rglob("*.cs")
)
for forbidden in ("AgentJournalGameRunId", "_agentJournalDefaultGameRunId", "_agentJournalInvocationSequence"):
    if forbidden in production:
        raise SystemExit(f"adapter-wide correlation state remains: {forbidden}")

print("CORE-1375 static no-bypass inventory passed: approved_paths=7 production_matrix=true")
PY

dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj \
  --filter "FullyQualifiedName~Issue1375_ConversationJournalWiringTests" \
  --results-directory "$results_dir" \
  --logger "trx;LogFileName=$trx_name" | tee "$log"

python3 - "$trx" <<'PY'
import sys
import xml.etree.ElementTree as ET

trx = sys.argv[1]
required = [
    "accepted_datee",
    "accepted_avatar",
    "prefetch_branch_clone",
    "speculative_branch_clone",
    "identical_prompt_retry",
    "avatar_emotional_director",
    "validation_rejected",
    "cancelled",
    "provider_failed",
    "director_branch_disposed",
    "semantic_link_context_isolation",
]
root = ET.parse(trx).getroot()
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
names = [node.attrib.get("testName", "") for node in root.findall(".//t:UnitTestResult", ns)]
counts = {scenario: sum(scenario in name for name in names) for scenario in required}
missing = [scenario for scenario, count in counts.items() if count == 0]
if missing:
    raise SystemExit("CORE-1375 verifier saw zero tests for: " + ", ".join(missing))
counters = root.find(".//t:Counters", ns)
if counters is None or counters.attrib.get("failed") != "0":
    raise SystemExit("CORE-1375 verifier TRX is missing counters or contains failures")
print("CORE-1375 scenario counts: " + " ".join(f"{name}={count}" for name, count in counts.items()))
print(
    "CORE-1375 conversation verifier passed: total={total} passed={passed} failed={failed} trx={trx}".format(
        total=counters.attrib.get("total", "?"),
        passed=counters.attrib.get("passed", "?"),
        failed=counters.attrib.get("failed", "?"),
        trx=trx,
    )
)
PY
