#!/usr/bin/env python3
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


REQUIRED_IDS = [
    "game.datee.performance",
    "game.avatar.reply",
    "game.emotional-director",
    "game.dialogue-options",
    "game.setup.dramatic-arc",
    "game.prefetch.option-branch",
    "game.speculation.option-branch",
    "character.synthesis",
    "admin.temporary-chat",
    "admin.prompt-speculation",
    "narrative.harness",
    "session.simulation",
    "game.delivery.success-improvement",
    "game.delivery.horniness-question",
    "game.delivery.steering-question",
    "game.datee.interest-change-beat",
]

REQUIRED_FIELDS = [
    "id",
    "status",
    "status_evidence",
    "activation_rule",
    "owner",
    "owner_description",
    "pi_agent_session",
    "journal_destination",
    "context_membership",
    "player_delivery",
    "visibility",
    "retention_policy_key",
    "required_owner_ids",
    "required_correlation_ids",
    "forbidden_owner_ids",
    "provenance_builder_ids",
    "implementation_matchers",
    "verifier_group",
]


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def read_text(path: Path) -> str:
    if not path.exists():
        raise AssertionError(f"Missing file: {path}")
    return path.read_text(encoding="utf-8")


def rel(repo: Path, path: Path) -> str:
    return path.relative_to(repo).as_posix()


def as_list(value):
    if value is None:
        return []
    if isinstance(value, list):
        return value
    return [value]


def source_lines(repo: Path, file_name: str, pattern: str) -> list[str]:
    path = repo / file_name
    text = read_text(path)
    regex = re.compile(pattern)
    return [
        f"{file_name}:{idx}"
        for idx, line in enumerate(text.splitlines(), start=1)
        if regex.search(line)
    ]


def no_production_caller(repo: Path, row: dict, matcher: dict) -> list[str]:
    regex = re.compile(matcher["pattern"])
    allowed_files = set(matcher["allowed_files"])
    violations: list[str] = []
    for root in matcher["search_roots"]:
        root_path = repo / root
        if not root_path.exists():
            continue
        for path in root_path.rglob("*.cs"):
            name = rel(repo, path)
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            if name.startswith(("tests/", "docs/", "contracts/")):
                continue
            for idx, line in enumerate(read_text(path).splitlines(), start=1):
                if regex.search(line) and name not in allowed_files:
                    violations.append(f"{name}:{idx}")
    if violations:
        raise AssertionError(
            f"Dormant caller guard failed for {row['id']}: {', '.join(violations)}"
        )
    return [f"dormant-no-caller-proof:{row['id']}"]


def matcher_results(repo: Path, row: dict, matcher: dict) -> list[str]:
    kind = matcher["kind"]
    if kind in {"symbol", "production_call"}:
        pattern = matcher.get("pattern", "")
        if pattern == ".*":
            raise AssertionError(f"Catch-all matcher forbidden for {row['id']}")
        return source_lines(repo, matcher["file"], pattern)
    if kind == "web_review_anchor":
        text = read_text(repo / matcher["file"])
        anchor = matcher["anchor"]
        if anchor not in text:
            raise AssertionError(f"Missing web review anchor {anchor} for {row['id']}")
        return [f"{matcher['file']}:anchor:{anchor}"]
    if kind == "no_production_caller":
        return no_production_caller(repo, row, matcher)
    raise AssertionError(f"Unknown matcher kind {kind} for {row['id']}")


def add_candidate(
    candidates: list[dict],
    file_name: str,
    line_no: int,
    line: str,
    reason: str,
) -> None:
    candidates.append(
        {
            "key": f"{file_name}:{line_no}",
            "file": file_name,
            "line": line_no,
            "text": line.strip(),
            "reason": reason,
        }
    )


def static_scan_candidates(repo: Path) -> list[dict]:
    candidates: list[dict] = []
    for root in ("src", "session-runner", "tools"):
        root_path = repo / root
        if not root_path.exists():
            continue
        for path in root_path.rglob("*.cs"):
            name = rel(repo, path)
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            if name.startswith("src/Pinder.RemoteAssets/"):
                continue
            if re.match(
                r"^src/Pinder.LlmAdapters/(PiLlmTransport|ThinkingStrippingLlmTransport|PunctuationNormalizingTransport|PiProviderTransportFactory)\.cs$",
                name,
            ):
                continue
            if name.startswith("src/Pinder.Core/Interfaces/"):
                continue
            if name == "src/Pinder.Core/Conversation/NullLlmAdapter.cs":
                continue
            if re.match(r"^src/Pinder.SessionSetup/(I|Synthesis/I)", name):
                continue
            if name == "src/Pinder.SessionSetup/LlmOptionalTextGeneration.cs":
                continue

            for idx, line in enumerate(read_text(path).splitlines(), start=1):
                if line.lstrip().startswith("//"):
                    continue
                if name.startswith("src/Pinder.Core/Conversation/") and re.search(
                    r"Get(DialogueOptions|DateeResponse|SuccessImprovement|SteeringQuestion|HorninessQuestion)Async\(",
                    line,
                ):
                    add_candidate(candidates, name, idx, line, "core-conversation-call")
                elif re.match(
                    r"^src/Pinder.LlmAdapters/PinderLlmAdapter(\.EmotionalDirector)?\.cs$",
                    name,
                ) and re.search(
                    r"Get(DialogueOptions|DateeResponse|InterestChangeBeat|SuccessImprovement|SteeringQuestion|HorninessQuestion)Async\(|GenerateEmotionalDirectionAsync\(",
                    line,
                ):
                    add_candidate(candidates, name, idx, line, "adapter-provider-path")
                elif name.startswith("src/Pinder.SessionSetup/") and re.search(
                    r"public async Task<.*GenerateAsync\(|SynthesizeAsync\(|LlmOptionalTextGeneration\.RunAsync\(",
                    line,
                ):
                    add_candidate(candidates, name, idx, line, "setup-synthesis-provider-path")
                elif name.startswith("src/Pinder.NarrativeHarness/") and re.search(
                    r"public async Task<HarnessRunResult> RunAsync\(|CharacterPursuerActor|GenericLlmPursuerActor|_transport\.SendAsync\(",
                    line,
                ):
                    add_candidate(candidates, name, idx, line, "harness-provider-path")
                elif name == "session-runner/LlmPlayerAgent.cs" and re.search(
                    r"public sealed class LlmPlayerAgent|PiProviderTransportFactory\.Create|SendStructuredAsync\(",
                    line,
                ):
                    add_candidate(candidates, name, idx, line, "simulation-provider-path")
    return candidates


def run_dotnet_ownership_tests(repo: Path, results_dir: Path) -> Path:
    results_dir.mkdir(parents=True, exist_ok=True)
    trx_name = "agent-journal-ownership-host.trx"
    project = repo / "tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj"
    subprocess.run(
        [
            "dotnet",
            "test",
            str(project),
            "--filter",
            "FullyQualifiedName~OwnershipManifestTests",
            "--results-directory",
            str(results_dir),
            "--logger",
            f"trx;LogFileName={trx_name}",
        ],
        cwd=repo,
        check=True,
    )
    trx_path = results_dir / trx_name
    if not trx_path.exists():
        raise AssertionError(f"Test TRX was not produced: {trx_path}")
    return trx_path


def trx_test_count(path: Path) -> int:
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = root.find(".//t:Counters", ns)
    if counters is None:
        raise AssertionError(f"TRX counters missing: {path}")
    return int(counters.attrib["total"])


def verify_manifest(repo: Path) -> tuple[list[str], dict[str, int]]:
    manifest_path = repo / "contracts/agent-journal-invocation-ownership.v1.json"
    manifest = json.loads(read_text(manifest_path))
    if manifest["schema_version"] != "agent-journal-invocation-ownership.v1":
        raise AssertionError("Unexpected manifest schema_version")
    if manifest["closed_inventory"] is not True:
        raise AssertionError("Manifest must be closed_inventory=true")
    if manifest["inventory_size"] != 16:
        raise AssertionError("Manifest inventory_size must be 16")

    rows = manifest["rows"]
    ids = [row["id"] for row in rows]
    if ids != REQUIRED_IDS:
        raise AssertionError(f"Manifest ID inventory changed: {ids}")
    if len(set(ids)) != len(ids):
        raise AssertionError("Duplicate manifest IDs are forbidden")

    symbol_map: dict[str, set[str]] = {}
    all_matches: list[str] = []
    web_review_count = 0
    dormant_proofs = 0

    for row in rows:
        for field in REQUIRED_FIELDS:
            if field not in row:
                raise AssertionError(f"Row {row['id']} is missing {field}")
        for field in (
            "status_evidence",
            "required_owner_ids",
            "required_correlation_ids",
            "forbidden_owner_ids",
            "provenance_builder_ids",
            "implementation_matchers",
        ):
            if not as_list(row[field]):
                raise AssertionError(f"Row {row['id']} has empty {field}")

        owner_ids = set(row["required_owner_ids"])
        correlation_ids = set(row["required_correlation_ids"])
        if row["id"].startswith("game."):
            if "game_run_id" not in owner_ids or "game_run_id" not in correlation_ids:
                raise AssertionError(f"Game row {row['id']} must require/correlate game_run_id")
        else:
            if "game_run_id" in owner_ids or "game_run_id" in correlation_ids:
                raise AssertionError(f"Non-Game row {row['id']} must not require/correlate game_run_id")

        for matcher in row["implementation_matchers"]:
            matches = matcher_results(repo, row, matcher)
            if not matches:
                raise AssertionError(f"Matcher for {row['id']} produced zero matches")
            for match in matches:
                all_matches.append(f"{row['id']} -> {match}")
                if matcher["kind"] in {"symbol", "production_call"}:
                    symbol_map.setdefault(match, set()).add(row["id"])
                elif matcher["kind"] == "web_review_anchor":
                    web_review_count += 1
                elif matcher["kind"] == "no_production_caller":
                    dormant_proofs += 1

    live_count = sum(1 for row in rows if row["status"] == "live_production")
    dormant_count = sum(1 for row in rows if row["status"] == "provider_capable_dormant")
    dead_count = sum(1 for row in rows if row["status"] == "dead_with_proof")
    if live_count != 15 or dormant_count != 1 or dead_count != 0:
        raise AssertionError(
            f"Unexpected status counts: live={live_count}, dormant={dormant_count}, dead={dead_count}"
        )
    if dormant_proofs == 0:
        raise AssertionError("Dormant no-caller proof did not run")
    if web_review_count == 0:
        raise AssertionError("Web review match count is zero")

    candidates = static_scan_candidates(repo)
    if not candidates:
        raise AssertionError("Static production scan produced zero candidates")
    unmatched = [candidate for candidate in candidates if candidate["key"] not in symbol_map]
    duplicates = [
        f"{candidate['key']} -> {','.join(sorted(symbol_map[candidate['key']]))}"
        for candidate in candidates
        if candidate["key"] in symbol_map and len(symbol_map[candidate["key"]]) != 1
    ]
    if unmatched:
        raise AssertionError(
            "Unclassified production LLM paths: "
            + "; ".join(f"{item['key']} [{item['reason']}] {item['text']}" for item in unmatched)
        )
    if duplicates:
        raise AssertionError("Duplicate production LLM path ownership: " + "; ".join(duplicates))

    counts = {
        "manifest_count": len(rows),
        "live_count": live_count,
        "dormant_count": dormant_count,
        "dead_count": dead_count,
        "production_symbol_match_count": len(symbol_map),
        "static_scan_candidate_count": len(candidates),
        "web_review_match_count": web_review_count,
    }
    return all_matches, counts


def write_evidence(repo: Path, evidence_dir: Path | None, summary: list[str], matches: list[str]) -> None:
    if evidence_dir is None:
        return
    evidence_dir.mkdir(parents=True, exist_ok=True)
    (evidence_dir / "CORE-1373-host-compatible-local-verifier.txt").write_text(
        "\n".join(summary) + "\n",
        encoding="utf-8",
    )
    (evidence_dir / "CORE-1373-agent-journal-ownership-matches.txt").write_text(
        "\n".join(matches) + "\n",
        encoding="utf-8",
    )
    shutil.copyfile(
        repo / "contracts/agent-journal-invocation-ownership.v1.json",
        evidence_dir / "CORE-1373-agent-journal-invocation-ownership.v1.json",
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Verify the closed Agent Journal invocation ownership manifest.")
    parser.add_argument(
        "--evidence-dir",
        default=os.environ.get("EIGENTAKT_KEEP_DIR"),
        help="Optional directory for CORE-1373 evidence files.",
    )
    args = parser.parse_args()

    repo = repo_root()
    results_dir = repo / "TestResults/agent-journal-ownership-host"
    evidence_dir = Path(args.evidence_dir) if args.evidence_dir else None

    matches, counts = verify_manifest(repo)
    trx_path = run_dotnet_ownership_tests(repo, results_dir)
    test_count = trx_test_count(trx_path)
    if test_count <= 0:
        raise AssertionError("OwnershipManifestTests matched zero tests")

    subprocess.run(["git", "diff", "--check"], cwd=repo, check=True)

    summary = [
        "host-compatible local agent-journal ownership verifier completed",
        f"manifest_count={counts['manifest_count']}",
        f"live_count={counts['live_count']}",
        f"dormant_count={counts['dormant_count']}",
        f"dead_count={counts['dead_count']}",
        f"production_symbol_match_count={counts['production_symbol_match_count']}",
        f"static_scan_candidate_count={counts['static_scan_candidate_count']}",
        f"web_review_match_count={counts['web_review_match_count']}",
        "dormant_interest_change_no_caller_proof=passed",
        f"ownership_test_count={test_count}",
        "git_diff_check=passed",
    ]
    write_evidence(repo, evidence_dir, summary, matches)
    print("\n".join(summary))
    return 0


if __name__ == "__main__":
    sys.exit(main())
