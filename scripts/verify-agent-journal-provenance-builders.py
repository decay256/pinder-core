#!/usr/bin/env python3
import hashlib
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "contracts" / "agent-journal-provenance-builders.v1.json"
OWNERSHIP_MANIFEST = ROOT / "contracts" / "agent-journal-invocation-ownership.v1.json"
FIXTURE = ROOT / "tests" / "Pinder.LlmAdapters.Tests" / "Fixtures" / "AgentJournals" / "Provenance" / "prompt-builder-goldens.v1.json"
TEST_FILE = ROOT / "tests" / "Pinder.LlmAdapters.Tests" / "AgentJournals" / "Provenance" / "PromptBuilderPropagationTests.cs"

REQUIRED_IDS = [
    "session.system",
    "session.user",
    "datee.emotional-director.system",
    "datee.emotional-director.user",
    "datee.performance",
    "dialogue-options.system",
    "dialogue-options.user",
    "game.setup.dramatic-arc",
    "delivery.success-improvement",
    "delivery.steering-question",
    "delivery.horniness-question",
    "datee.interest-change-beat.dormant",
]

EXPECTED_DELIVERY_CONSUMERS = {
    "delivery.success-improvement": "game_run_delivery_one_shot_record",
    "delivery.steering-question": "game_run_delivery_append_one_shot_record",
    "delivery.horniness-question": "game_run_delivery_append_one_shot_record",
}


def fail(message: str) -> None:
    print(f"CORE-1378 verifier failed: {message}", file=sys.stderr)
    sys.exit(1)


def read_json(path: pathlib.Path):
    if not path.exists():
        fail(f"missing {path.relative_to(ROOT)}")
    return json.loads(path.read_text(encoding="utf-8"))


def normalized(path: pathlib.Path) -> str:
    return path.relative_to(ROOT).as_posix()


def main() -> int:
    manifest = read_json(MANIFEST)
    rows = manifest.get("rows", [])
    ids = [row.get("id") for row in rows]
    if manifest.get("schema_version") != "agent-journal-provenance-builders.v1":
        fail("unexpected schema_version")
    if ids != REQUIRED_IDS:
        fail(f"builder ids mismatch: {ids}")
    if manifest.get("manifest_count") != 12 or len(rows) != 12:
        fail("manifest count is not exactly 12")
    live_count = sum(1 for row in rows if row.get("status") == "live_production")
    dormant_count = sum(1 for row in rows if row.get("status") == "provider_capable_dormant")
    if live_count != 11:
        fail(f"live builder count {live_count} != 11")
    if dormant_count != 1:
        fail(f"dormant guard count {dormant_count} != 1")
    rows_by_id = {row.get("id"): row for row in rows}
    ownership = read_json(OWNERSHIP_MANIFEST)
    ownership_by_id = {row.get("id"): row for row in ownership.get("rows", [])}
    for builder_id, expected_consumer in EXPECTED_DELIVERY_CONSUMERS.items():
        actual_consumer = rows_by_id[builder_id].get("recorder_consumer")
        ownership_id = rows_by_id[builder_id].get("source_ownership_row")
        ownership_consumer = (ownership_by_id.get(ownership_id) or {}).get("journal_destination")
        if ownership_consumer != expected_consumer:
            fail(f"#1373 {ownership_id} journal_destination unexpectedly changed to {ownership_consumer!r}")
        if actual_consumer != expected_consumer:
            fail(
                f"{builder_id} recorder_consumer {actual_consumer!r} "
                f"!= #1373 {expected_consumer!r}"
            )

    symbol_count = 0
    for row in rows:
        impl = row.get("implementation", {})
        file_value = impl.get("file")
        pattern = impl.get("symbol_pattern")
        if not file_value or not pattern:
            fail(f"{row.get('id')} missing implementation file or symbol_pattern")
        file_path = ROOT / file_value
        if not file_path.exists():
            fail(f"{row.get('id')} file missing: {file_value}")
        if not re.search(pattern, file_path.read_text(encoding="utf-8")):
            fail(f"{row.get('id')} symbol pattern not found in {file_value}: {pattern}")
        symbol_count += 1

    fixture = read_json(FIXTURE)
    fixture_ids = [row.get("id") for row in fixture]
    if fixture_ids != REQUIRED_IDS:
        fail("golden fixture ids do not match manifest ids")
    golden_count = sum(1 for row in fixture if row.get("status") == "live_production")
    if golden_count != 11:
        fail(f"golden fixture count {golden_count} != 11")

    golden_document_count = 0
    golden_range_count = 0
    for row in fixture:
        builder_id = row.get("id")
        before = row.get("beforeDocuments")
        after = row.get("afterDocuments")
        if not isinstance(before, list) or not isinstance(after, list):
            fail(f"{builder_id} golden payload is missing beforeDocuments/afterDocuments")
        if row.get("status") == "provider_capable_dormant":
            if before or after:
                fail(f"{builder_id} dormant golden row must not contain documents")
            continue
        if not before or len(before) != len(after):
            fail(f"{builder_id} before/after document order or count mismatch")

        for order, (before_document, after_document) in enumerate(zip(before, after)):
            if before_document.get("order") != order or after_document.get("order") != order:
                fail(f"{builder_id} document order {order} is not canonical")
            if before_document.get("role") != after_document.get("role"):
                fail(f"{builder_id} document {order} role changed")
            text = after_document.get("text")
            if not isinstance(text, str) or before_document.get("text") != text:
                fail(f"{builder_id} document {order} emitted text changed")
            expected_hash = "sha256:" + hashlib.sha256(text.encode("utf-8")).hexdigest()
            if after_document.get("contentHash") != expected_hash:
                fail(f"{builder_id} document {order} content hash mismatch")
            document_id = after_document.get("documentId")
            if not document_id or not after_document.get("kind"):
                fail(f"{builder_id} document {order} missing identity")

            ranges = after_document.get("ranges")
            if not isinstance(ranges, list) or (text and not ranges):
                fail(f"{builder_id} document {order} has no range payload")
            utf16_boundaries = {0}
            utf16_cursor = 0
            for character in text:
                utf16_cursor += len(character.encode("utf-16-le")) // 2
                utf16_boundaries.add(utf16_cursor)
            coverage_cursor = 0
            for range_index, provenance_range in enumerate(ranges):
                start = provenance_range.get("startUtf16")
                end = provenance_range.get("endUtf16")
                if start != coverage_cursor or not isinstance(end, int) or end <= start:
                    fail(f"{builder_id} document {order} range {range_index} breaks coverage")
                if start not in utf16_boundaries or end not in utf16_boundaries:
                    fail(f"{builder_id} document {order} range {range_index} splits a UTF-16 scalar")
                if provenance_range.get("documentId") != document_id:
                    fail(f"{builder_id} document {order} range {range_index} document id mismatch")
                range_kind = provenance_range.get("rangeKind")
                source = provenance_range.get("source")
                if range_kind not in {"configured", "runtime_generated"} or not isinstance(source, dict):
                    fail(f"{builder_id} document {order} range {range_index} classification missing")
                required_source_keys = {
                    "kind", "sourceId", "keyPath", "revision", "contentHash", "editorTargetId"
                }
                if set(source) != required_source_keys:
                    fail(f"{builder_id} document {order} range {range_index} source payload incomplete")
                if range_kind == "configured":
                    if source.get("kind") not in {"configuration", "catalog"}:
                        fail(f"{builder_id} document {order} configured range source kind mismatch")
                    if not source.get("revision") and not source.get("contentHash"):
                        fail(f"{builder_id} document {order} configured range lacks revision/hash")
                elif source.get("kind") != "runtime_generated":
                    fail(f"{builder_id} document {order} runtime range source kind mismatch")
                coverage_cursor = end
            if coverage_cursor != utf16_cursor:
                fail(f"{builder_id} document {order} range coverage is not full UTF-16 length")
            golden_document_count += 1
            golden_range_count += len(ranges)

    for builder_id, key_path in {
        "delivery.steering-question": "SteeringContext.DeliveredMessage",
        "delivery.horniness-question": "HorninessQuestionContext.DeliveredMessage",
    }.items():
        fixture_row = next(row for row in fixture if row.get("id") == builder_id)
        delivered_ranges = [
            provenance_range
            for document in fixture_row["afterDocuments"]
            for provenance_range in document["ranges"]
            if provenance_range["source"]["keyPath"] == key_path
        ]
        if not delivered_ranges:
            fail(f"{builder_id} has no explicit delivered_message golden range")

    dormant = next(row for row in rows if row.get("id") == "datee.interest-change-beat.dormant")
    guard = dormant.get("dormant_activation_guard") or {}
    allowed = set(guard.get("allowed_files") or [])
    pattern = guard.get("pattern")
    if not pattern:
        fail("dormant guard missing pattern")
    illegal_hits = []
    for path in (ROOT / "src").rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        if re.search(pattern, text):
            rel = normalized(path)
            if rel not in allowed:
                illegal_hits.append(rel)
    if illegal_hits:
        fail("dormant interest-change production activation found: " + ", ".join(illegal_hits))

    test_text = TEST_FILE.read_text(encoding="utf-8") if TEST_FILE.exists() else ""
    if "Assert.Equal(checkedIn, regenerated);" not in test_text or "SerializeGoldenFixture()" not in test_text:
        fail("focused test does not compare regenerated golden bytes with the checked-in fixture")
    if "AC4_DeliveryQuestionBuildersRequireAndAnnotateDeliveredMessage" not in test_text:
        fail("missing delivered_message fail-closed regression")
    ac_groups = sorted(set(re.findall(r"AC([1-5])_", test_text)))
    if len(ac_groups) != 5:
        fail(f"nonzero AC group count {len(ac_groups)} != 5")

    print(f"CORE-1378 manifest_count={len(rows)}")
    print(f"CORE-1378 live_builder_count={live_count}")
    print(f"CORE-1378 dormant_guard_count={dormant_count}")
    print(f"CORE-1378 symbol_match_count={symbol_count}")
    print(f"CORE-1378 golden_fixture_count={golden_count}")
    print(f"CORE-1378 golden_document_count={golden_document_count}")
    print(f"CORE-1378 golden_range_count={golden_range_count}")
    print("CORE-1378 golden_byte_regeneration_check=focused_test")
    print(f"CORE-1378 nonzero_ac_count={len(ac_groups)}")
    print("CORE-1378 dormant_activation_check=passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
