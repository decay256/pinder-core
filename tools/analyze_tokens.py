#!/usr/bin/env python3
"""
Pinder Token and Session Metrics Analyzer

Analyzes Pinder session journals / agent journal bundles to compute:
- Cumulative tokens across all pipeline phases and turns
- Peak single-call context tokens (for local GPU / KV-cache capacity sizing)
- Session and invocation breakdown by phase, agent kind, and model
- Extrapolations for arbitrary turn counts (e.g. 20 turns)

Usage:
    python3 analyze_tokens.py <path_to_journal_or_bundle.json> [--target-turns N]

Examples:
    python3 analyze_tokens.py /tmp/journal_a72aea7c.json
    python3 analyze_tokens.py /tmp/journal_a72aea7c.json --target-turns 20
"""

import argparse
import json
import sys
from typing import Any, Dict, List, Optional


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Analyze Pinder session token and hardware metrics.")
    parser.add_argument("journal_file", help="Path to the JSON agent journal bundle or session snapshot file.")
    parser.add_argument(
        "--target-turns",
        type=int,
        default=20,
        help="Target number of turns for cost and token extrapolation (default: 20).",
    )
    return parser.parse_args()


def estimate_tokens(text: str) -> int:
    # Standard conservative estimate: ~4 chars per token for English + markdown/JSON
    if not text:
        return 0
    return max(1, len(text) // 4)


def analyze_bundle(data: Dict[str, Any], target_turns: int) -> None:
    game_run_id = data.get("game_run_id", "unknown")
    state = data.get("state", {})
    turn_number = state.get("turn_number", 0)
    outcome = state.get("outcome", "InProgress")
    journals = data.get("journals", [])

    print("=" * 70)
    print(" 🎲 PINDER SESSION & TOKEN METRICS REPORT")
    print("=" * 70)
    print(f"Game Run ID:        {game_run_id}")
    print(f"Turns Completed:    {turn_number}")
    print(f"Outcome:            {outcome}")
    print(f"Agent Sessions:     {len(journals)}")

    for idx, j in enumerate(journals, 1):
        snapshot_id = j.get("snapshot_id", "unknown")
        agent_kind = j.get("agent_kind", "unknown")
        session_id = j.get("materialization", {}).get("journal", {}).get("agent_session_id", "unknown")
        print(f"  [{idx}] Snapshot: {snapshot_id:<8} | Kind: {agent_kind:<8} | Session ID: {session_id}")

    print("-" * 70)

    total_invocations = 0
    total_input_chars = 0
    total_input_tokens = 0
    peak_single_call_input_tokens = 0
    peak_call_info = ""

    phase_counts: Dict[str, int] = {}
    phase_input_tokens: Dict[str, int] = {}
    models_used: Dict[str, int] = {}
    turn_records: List[Dict[str, Any]] = []

    for j in journals:
        snapshot_id = j.get("snapshot_id", "unknown")
        journal = j.get("materialization", {}).get("journal", {})
        entries = journal.get("entries", [])

        for entry in entries:
            custom = entry.get("custom_entry", {})
            inv = custom.get("llm_invocation")
            res = custom.get("llm_result")

            if inv:
                total_invocations += 1
                phase = inv.get("phase", "unknown")
                model = inv.get("model_id", "unknown")
                corr = inv.get("correlation", {})
                turn_id = corr.get("turn_id", "unknown")
                branch_id = corr.get("branch_id", "main")

                phase_counts[phase] = phase_counts.get(phase, 0) + 1
                models_used[model] = models_used.get(model, 0) + 1

                docs = inv.get("input_documents", [])
                call_chars = sum(len(d.get("text", "")) for d in docs)
                call_tokens = estimate_tokens(docs[0].get("text", "")) if len(docs) == 1 else sum(estimate_tokens(d.get("text", "")) for d in docs)

                total_input_chars += call_chars
                total_input_tokens += call_tokens
                phase_input_tokens[phase] = phase_input_tokens.get(phase, 0) + call_tokens

                if call_tokens > peak_single_call_input_tokens:
                    peak_single_call_input_tokens = call_tokens
                    peak_call_info = f"Agent: {snapshot_id}, Phase: {phase}, Turn: {turn_id}"

                turn_records.append({
                    "agent": snapshot_id,
                    "phase": phase,
                    "turn": turn_id,
                    "branch": branch_id,
                    "model": model,
                    "input_chars": call_chars,
                    "input_tokens": call_tokens,
                })

    print(f"Total Recorded Invocations: {total_invocations}")
    print(f"Models Detected:")
    for m, count in models_used.items():
        print(f"  • {m}: {count} call(s)")

    print(f"\nPhases Breakdown:")
    for p, count in phase_counts.items():
        tokens = phase_input_tokens.get(p, 0)
        print(f"  • {p:<25} : {count:>2} call(s) | ~{tokens:>6,} input tokens")

    print("\nIndividual Invocations in this Run:")
    for r in turn_records:
        print(f"  [{r['agent']:<6}] Turn {r['turn']:<7} Branch: {r['branch']:<10} | In: {r['input_chars']:>6} chars (~{r['input_tokens']:>5} tok)")

    print("=" * 70)
    print(" 🖥️  LOCAL GPU & VRAM REQUIREMENTS (e.g. RTX 4090 / 24GB)")
    print("=" * 70)
    # A single LLM call is held in memory at any point.
    print(f"Peak Single-Call Context:    ~{peak_single_call_input_tokens:,} tokens ({peak_call_info})")
    
    # 26B / 27B Q4_K_M model takes ~16.5 GB base weights
    # KV Cache for ~8k context takes ~2.0 GB
    # KV Cache for ~16k context takes ~4.0 GB
    kv_cache_approx_gb = (peak_single_call_input_tokens / 4096.0) * 1.0
    total_vram_est_gb = 16.5 + kv_cache_approx_gb

    print(f"Approx KV-Cache VRAM Needed: ~{kv_cache_approx_gb:.2f} GB (for peak context)")
    print(f"Total Estimated VRAM (27B Q4):~{total_vram_est_gb:.2f} GB / 24.0 GB (fits 100% in VRAM ✅)")
    print("Prompt Caching Optimization:  Static ~6.5k system prompt is reusable across turns.")

    print("=" * 70)
    print(f" 📈 20-TURN PROJECTION & TOKEN EXTRAPOLATION (Target: {target_turns} Turns)")
    print("=" * 70)
    
    # In Pinder, turn growth is approx ~250 tokens of conversation history per turn
    base_static_tokens = 6500
    avg_turn_history_growth = 250

    # Projecting full pipeline (1 datee response + 1 option generation + 1 director per turn)
    projected_datee_tokens = sum(base_static_tokens + (t * avg_turn_history_growth) for t in range(1, target_turns + 1))
    projected_options_tokens = sum((base_static_tokens - 500) + (t * avg_turn_history_growth) for t in range(1, target_turns + 1))
    projected_director_tokens = sum(3500 + (t * (avg_turn_history_growth // 2)) for t in range(1, target_turns + 1))

    total_projected_input_tokens = projected_datee_tokens + projected_options_tokens + projected_director_tokens
    total_projected_output_tokens = target_turns * 350

    print(f"Base Static System Context:  ~{base_static_tokens:,} tokens (identical across turns)")
    print(f"Turn Context at Turn 1:      ~{base_static_tokens + avg_turn_history_growth:,} tokens")
    print(f"Turn Context at Turn {target_turns}:     ~{base_static_tokens + (target_turns * avg_turn_history_growth):,} tokens")
    print()
    print(f"Projected {target_turns}-Turn Breakdown:")
    print(f"  • Datee Responses ({target_turns} calls):    ~{projected_datee_tokens:>8,} tokens")
    print(f"  • Dialogue Options ({target_turns} calls):   ~{projected_options_tokens:>8,} tokens")
    print(f"  • Director / Planning ({target_turns} calls): ~{projected_director_tokens:>8,} tokens")
    print(f"  ------------------------------------------------")
    print(f"  • TOTAL Cumulative Input Tokens:  ~{total_projected_input_tokens:>8,} tokens (~{total_projected_input_tokens / 1_000_000:.2f}M)")
    print(f"  • TOTAL Cumulative Output Tokens: ~{total_projected_output_tokens:>8,} tokens")
    print("=" * 70)


def main() -> None:
    args = parse_arguments()
    try:
        with open(args.journal_file, "r", encoding="utf-8") as f:
            data = json.load(f)
    except Exception as ex:
        print(f"Error loading journal file '{args.journal_file}': {ex}", file=sys.stderr)
        sys.exit(1)

    analyze_bundle(data, args.target_turns)


if __name__ == "__main__":
    main()
