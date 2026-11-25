"""Directed render: one VoiceDesign utterance per turn, acted from performance.yaml,
takes chosen by measured delivery, beats edited to length.

Why VoiceDesign: it is the only Qwen3-TTS mode that reads an acting instruction. The
cost is that each call re-imagines the voice from its description, so every take is
gated on speaker similarity to the frozen voice (the clone prompts from design_voices.py).

Per take the script measures, against the markup in performance.yaml:
  words     Whisper large-v3-turbo WER (gate)
  voice     speaker-embedding similarity to own vs other voice (gate)
  phrasing  did the model actually break the phrase at each marked beat
  pace      articulation rate against the turn's pace target
  landing   pitch at the end of each landing phrase relative to the turn (fall), or the
            rise across the last word of a ^ question
  stress    loudness and pitch of each *operative* word against the turn's other words
and keeps the best-scoring take that passes both gates. The winner is then edited: the
silence at each beat is set to its marked length, laugh points get a half-second beat,
any other pause longer than 0.5 s is shortened to 0.35 s.

usage: python direct.py [--takes N] [--turns t01,t07] [--rescore]
Output: out/direct/<turn>/take*.wav, out/direct/<turn>/edit.wav, out/direct/takes.json,
        out/direct/picks.json (segments cut at laugh points, for mix.py)
"""
import argparse
import difflib
import json
import re

import librosa
import numpy as np
import soundfile as sf
import torch

from common import OUT, load_asr, load_tts, load_yaml, norm_words, speaker_embedding, transcribe, wer

ap = argparse.ArgumentParser()
ap.add_argument("--takes", type=int, default=10)
ap.add_argument("--turns", default="")
ap.add_argument("--rescore", action="store_true", help="score existing takes, do not generate")
args = ap.parse_args()

perf = load_yaml("performance.yaml")["turns"]
voices = load_yaml("voices.yaml")
if args.turns:
    keep = set(args.turns.split(","))
    perf = [t for t in perf if t["id"] in keep]
DDIR = OUT / "direct"
BEAT = {"/": 0.25, "//": 0.5, "///": 0.9}
REACT_BEAT = 0.5
PACE = {"slow": 2.4, "measured": 3.0, "brisk": 3.5, "racing": 4.1}  # words/s while speaking


# --- markup ---------------------------------------------------------------------------
def parse(script):
    """Return synthesis text and per-word annotations.

    words[i] = dict(w=surface, stress, beat_after (s or None), react_after, rise)
    """
    toks = re.findall(r"/{1,3}|\{\w+\}|\^|[^\s/{}^]+", script)
    words, out = [], []
    for tok in toks:
        if tok in BEAT:
            words[-1]["beat_after"] = BEAT[tok]
            # a held beat is suspended, not final: ellipsis, so the pitch does not close
            if not re.search(r"[,.!?:;]$", out[-1]):
                out[-1] += "," if tok == "/" else "..."
        elif tok.startswith("{"):
            words[-1]["react_after"] = tok[1:-1]
        elif tok == "^":
            words[-1]["rise"] = True
        else:
            stress = tok.startswith("*") or "*" in tok
            clean = tok.replace("*", "")
            out.append(clean)
            words.append(dict(w=clean, stress=stress, beat_after=None, react_after=None, rise=False))
    text = " ".join(out)
    return text, words


def check_words_against_cues():
    """The performance script must say exactly Carr's words from cues.yaml."""
    cues = load_yaml("cues.yaml")["lines"]
    ref = [(l["speaker"], w) for l in cues for s in l["segments"] for w in norm_words(s["text"])]
    hyp = [(t["speaker"], w) for t in load_yaml("performance.yaml")["turns"] for w in norm_words(parse(t["script"])[0])]
    if ref != hyp:
        sm = difflib.SequenceMatcher(None, ref, hyp, autojunk=False)
        for op in sm.get_opcodes():
            if op[0] != "equal":
                print("script differs from cues:", op, ref[op[1]:op[2]], hyp[op[3]:op[4]])
        raise SystemExit("performance.yaml does not match cues.yaml")


check_words_against_cues()


# --- acoustic helpers -------------------------------------------------------------------
HOP = 0.01


def frame_db(w, sr):
    hop = int(HOP * sr)
    rms = librosa.feature.rms(y=w, frame_length=2 * hop, hop_length=hop, center=True)[0]
    return 20 * np.log10(rms + 1e-6)


def silence_between(fdb, t0, t1, floor):
    """Longest run of frames below floor inside [t0, t1]; returns (start, end) in s or None."""
    a, b = max(0, int(t0 / HOP)), min(len(fdb), int(t1 / HOP) + 1)
    best, cur = None, None
    for i in range(a, b):
        if fdb[i] < floor:
            cur = (cur[0], i + 1) if cur else (i, i + 1)
            if best is None or cur[1] - cur[0] > best[1] - best[0]:
                best = cur
        else:
            cur = None
    return None if best is None else (best[0] * HOP, best[1] * HOP)


def align(words, ts_words):
    """Map script words to faster-whisper word timestamps; interpolate unmatched ones."""
    ref = [norm_words(x["w"]) for x in words]
    flat, owner = [], []
    for i, toks in enumerate(ref):
        for tok in toks:
            flat.append(tok)
            owner.append(i)
    hyp, times = [], []
    for text, s, e in ts_words:
        for tok in norm_words(text):
            hyp.append(tok)
            times.append((s, e))
    span = [[None, None] for _ in words]
    for a, b, n in difflib.SequenceMatcher(None, flat, hyp, autojunk=False).get_matching_blocks():
        for k in range(n):
            i = owner[a + k]
            s, e = times[b + k]
            span[i][0] = s if span[i][0] is None else min(span[i][0], s)
            span[i][1] = e if span[i][1] is None else max(span[i][1], e)
    known = [i for i, sp in enumerate(span) if sp[0] is not None]
    if not known:
        return None
    for i, sp in enumerate(span):  # unmatched words: borrow neighbours' edges
        if sp[0] is None:
            prev = max([k for k in known if k < i], default=None)
            nxt = min([k for k in known if k > i], default=None)
            sp[0] = span[prev][1] if prev is not None else span[nxt][0]
            sp[1] = span[nxt][0] if nxt is not None else span[prev][1]
    return span


def semitones(a, b):
    return 12 * np.log2(a / b)


def measure(w, sr, words, ts_words, pace_target):
    span = align(words, ts_words)
    if span is None:
        return None
    fdb = frame_db(w, sr)
    floor = max(np.percentile(fdb, 98) - 38, np.percentile(fdb, 5) + 6)
    f0, vflag, _ = librosa.pyin(w, fmin=65, fmax=520, sr=sr, frame_length=2048, hop_length=int(HOP * sr))
    f0 = np.where(vflag, f0, np.nan)
    turn_f0 = np.nanmedian(f0)

    def word_f0(i, part=slice(None)):
        a, b = int(span[i][0] / HOP), int(span[i][1] / HOP) + 1
        seg = f0[a:b][part]
        seg = seg[~np.isnan(seg)]
        return seg

    # gaps between consecutive words, measured as true silence
    gaps = []
    for i in range(len(words) - 1):
        sil = silence_between(fdb, span[i][1] - 0.12, span[i + 1][0] + 0.12, floor)
        gaps.append(sil)

    # phrasing: a break where the script asks for one
    want = [i for i, x in enumerate(words[:-1]) if x["beat_after"] or x["react_after"]]
    broke = [i for i in want if gaps[i] and gaps[i][1] - gaps[i][0] >= 0.08]
    phrasing = len(broke) / len(want) if want else 1.0
    # unwanted breaks in the middle of a phrase cost a little
    stray = [i for i, g in enumerate(gaps) if i not in want and g and g[1] - g[0] >= 0.25
             and not re.search(r"[,.!?:;]$", words[i]["w"])]

    # pace: words per second of speaking time (silences over 0.15 s excluded)
    total = span[-1][1] - span[0][0]
    paused = sum(g[1] - g[0] for g in gaps if g and g[1] - g[0] >= 0.15)
    rate = len(words) / max(0.5, total - paused)
    pace = -abs(np.log(rate / pace_target)) / np.log(1.25)  # 0 on target, -1 at 25% off

    # landing: pitch at the end of each landing phrase
    lands, land_scores = [], []
    for i, x in enumerate(words):
        if not (x["react_after"] or i == len(words) - 1):
            continue
        if x["rise"]:
            seg = word_f0(i)
            if len(seg) < 4:
                continue
            q = max(1, len(seg) // 3)
            st = semitones(np.median(seg[-q:]), np.median(seg[:q]))
            land_scores.append(np.clip(st / 3, -1, 1))
        else:
            seg = word_f0(i)[-10:]  # last ~100 ms of voicing in the word
            if len(seg) < 3:
                land_scores.append(-0.5)  # no pitch to land on: whispered or swallowed
                lands.append((x["w"], None))
                continue
            st = semitones(np.median(seg), turn_f0)
            land_scores.append(np.clip(-st / 3, -1, 1))
        lands.append((x["w"], round(float(st), 1)))
    landing = float(np.mean(land_scores)) if land_scores else 0.0

    # stress on operative words. Mid-phrase: louder and higher than the turn's other words.
    # On a landing word a comedian leans by lengthening it while the pitch goes down and the
    # voice often gets quieter, so there it is measured as duration per letter against the
    # turn's median.
    peaks_db = np.array([fdb[int(sp[0] / HOP): int(sp[1] / HOP) + 1].max(initial=-120) for sp in span])
    peaks_f0 = np.array([np.max(s) if len(s := word_f0(i)) else np.nan for i in range(len(words))])
    letters = np.array([max(1, len(re.sub(r"[^a-z]", "", x["w"].lower()))) for x in words])
    per_letter = np.array([(sp[1] - sp[0]) for sp in span]) / letters
    st_scores, stresses = [], []
    for i, x in enumerate(words):
        if not x["stress"]:
            continue
        if x["react_after"] or i == len(words) - 1:
            lengthen = per_letter[i] / np.median(per_letter)
            val = (lengthen - 1) / 0.4
            stresses.append((x["w"], "len", round(float(lengthen), 2)))
        else:
            d_db = peaks_db[i] - np.median(peaks_db)
            d_st = semitones(peaks_f0[i], np.nanmedian(peaks_f0)) if not np.isnan(peaks_f0[i]) else 0.0
            val = (d_db / 3 + d_st / 3) / 2
            stresses.append((x["w"], round(float(d_db), 1), round(float(d_st), 1)))
        st_scores.append(np.clip(val, -1, 1))
    stress = float(np.mean(st_scores)) if st_scores else 0.0

    return dict(span=span, gaps=gaps, floor=float(floor), phrasing=phrasing, stray=len(stray), rate=round(rate, 2),
                pace=float(pace), landing=landing, lands=lands, stress=stress, stresses=stresses)


def edit(w, sr, words, m):
    """Set each beat's silence to its marked length; trim lead-in and tail."""
    span, gaps = m["span"], m["gaps"]
    fdb = frame_db(w, sr)
    floor = m["floor"]
    voiced = np.where(fdb >= floor)[0]
    head = max(0.0, voiced[0] * HOP - 0.03) if len(voiced) else 0.0
    tail = min(len(w) / sr, voiced[-1] * HOP + 0.12) if len(voiced) else len(w) / sr
    cuts = []  # (cut_start, cut_end, silence_to_insert, word_index)
    for i, g in enumerate(gaps):
        x = words[i]
        target = x["beat_after"] or (REACT_BEAT if x["react_after"] else None)
        if g is None:
            if target is None:
                continue
            # the model ran the words together: cut at the quietest frame between them
            a, b = int((span[i][1] - 0.08) / HOP), int((span[i + 1][0] + 0.08) / HOP) + 1
            k = a + int(np.argmin(fdb[a:b])) if b > a else a
            g = (k * HOP, k * HOP)
        length = g[1] - g[0]
        if target is None:
            if length <= 0.5:
                continue
            target = 0.35
        # keep 40 ms of decay at each side of the silence, replace the middle
        a, b = g[0] + min(0.04, length / 2), g[1] - min(0.04, length / 2)
        cuts.append((a, b, max(0.0, target - (a - g[0]) - (g[1] - b)), i))
    pieces, t, times = [], head, {}
    out_t = 0.0
    fade = int(0.008 * sr)
    for a, b, ins, i in cuts:
        seg = w[int(t * sr): int(a * sr)].copy()
        if len(seg) > 2 * fade:
            seg[-fade:] *= np.linspace(1, 0, fade)
            if pieces:
                seg[:fade] *= np.linspace(0, 1, fade)
        pieces.append(seg)
        out_t += len(seg) / sr
        times[i] = out_t + ins / 2  # middle of the inserted beat: where a laugh cut goes
        pieces.append(np.zeros(int(ins * sr), np.float32))
        out_t += ins
        t = b
    seg = w[int(t * sr): int(tail * sr)].copy()
    if pieces and len(seg) > fade:
        seg[:fade] *= np.linspace(0, 1, fade)
    pieces.append(seg)
    return np.concatenate(pieces).astype(np.float32), times


# --- render ---------------------------------------------------------------------------
if not args.rescore:
    design = load_tts("design")
    for t in perf:
        d = DDIR / t["id"]
        d.mkdir(parents=True, exist_ok=True)
        have = len(list(d.glob("take*.wav")))
        if have >= args.takes:
            continue
        text, _ = parse(t["script"])
        v = voices[t["speaker"]]
        instruct = f"{v['base']} {t['act']}"
        n = args.takes - have
        for start in range(0, n, 5):
            k = min(5, n - start)
            wavs, sr = design.generate_voice_design(
                text=[text] * k, instruct=[instruct] * k, language=["English"] * k)
            for j, w in enumerate(wavs):
                sf.write(d / f"take{have + start + j}.wav", w, sr)
        print(f"rendered {t['id']}: {args.takes} takes", flush=True)
    del design
    torch.cuda.empty_cache()

# --- score ----------------------------------------------------------------------------
from faster_whisper import WhisperModel

asr = load_asr()
fw = WhisperModel("small.en", device="cpu", compute_type="int8")
base = load_tts("base")
cent = {}
for vname, v in voices.items():
    es = [torch.load(OUT / "voices" / vname / f"{m}.prompt.pt", weights_only=False)["ref_spk_embedding"].float().flatten()
          for m in v["modes"]]
    c = torch.stack([e / e.norm() for e in es]).mean(0)
    cent[vname] = (c / c.norm()).cpu()

report_path = DDIR / "takes.json"
report = json.loads(report_path.read_text(encoding="utf-8")) if report_path.exists() else {}
for t in perf:
    text, words = parse(t["script"])
    other = next(v for v in voices if v != t["speaker"])
    rows = []
    for p in sorted((DDIR / t["id"]).glob("take*.wav"), key=lambda p: int(p.stem[4:])):
        w, sr = sf.read(p, dtype="float32")
        hyp = transcribe(asr, w, sr)
        e = wer(text, hyp)
        emb = speaker_embedding(base, w, sr)
        own, oth = float(emb @ cent[t["speaker"]]), float(emb @ cent[other])
        segs, _ = fw.transcribe(w if sr == 16000 else librosa.resample(w, orig_sr=sr, target_sr=16000),
                                language="en", word_timestamps=True, condition_on_previous_text=False)
        ts = [[x.word, x.start, x.end] for s in segs for x in s.words]
        m = measure(w, sr, words, ts, PACE[t["pace"]])
        row = dict(take=p.name, wer=round(e, 3), own=round(own, 4), other=round(oth, 4), hyp=hyp)
        if m:
            row.update({k: m[k] for k in ("phrasing", "stray", "rate", "pace", "landing", "lands", "stress", "stresses")})
            # the landing carries most weight: a punchline that lifts at the end is the
            # most audible way a take gets the tone wrong
            row["delivery"] = round(m["phrasing"] + m["pace"] + 1.5 * m["landing"] + 0.5 * m["stress"]
                                    - 0.15 * m["stray"], 3)
        rows.append(row)
    # gates: the words, and the voice (own clearly above the other voice, near the frozen voice)
    best_own = max(r["own"] for r in rows)
    for r in rows:
        r["ok"] = bool(r["wer"] <= 0.1 and "delivery" in r and r["own"] - r["other"] >= 0.008
                       and r["own"] >= min(0.975, best_own - 0.01))
    passing = [r for r in rows if r["ok"]] or [r for r in rows if "delivery" in r]
    win = max(passing, key=lambda r: r["delivery"] + 4 * (r["own"] - r["other"]))
    report[t["id"]] = dict(pick=win["take"], passing=sum(r["ok"] for r in rows), takes=rows)
    print(f"{t['id']} {t['speaker']:5s} pick {win['take']:10s} pass {sum(r['ok'] for r in rows)}/{len(rows)} "
          f"wer {win['wer']:.2f} own {win['own']:.3f} oth {win['other']:.3f} | phr {win['phrasing']:.2f} "
          f"rate {win['rate']:.2f}/{PACE[t['pace']]} land {win['landing']:+.2f} {win['lands']} "
          f"stress {win['stress']:+.2f}", flush=True)
    # edit the winner
    w, sr = sf.read(DDIR / t["id"] / win["take"], dtype="float32")
    segs, _ = fw.transcribe(librosa.resample(w, orig_sr=sr, target_sr=16000), language="en",
                            word_timestamps=True, condition_on_previous_text=False)
    m = measure(w, sr, words, [[x.word, x.start, x.end] for s in segs for x in s.words], PACE[t["pace"]])
    ew, times = edit(w, sr, words, m)
    sf.write(DDIR / t["id"] / "edit.wav", ew, sr)
    report[t["id"]]["react_cuts"] = {str(i): round(s, 3) for i, s in times.items() if words[i]["react_after"]}
    report[t["id"]]["sr"] = sr
report_path.write_text(json.dumps(report, indent=1), encoding="utf-8")

# --- segments for the stage mix: cut each edited turn at its laugh points ---------------
all_turns = load_yaml("performance.yaml")["turns"]
if all(t["id"] in report for t in all_turns):
    picks = []
    sdir = DDIR / "segments"
    sdir.mkdir(exist_ok=True)
    for n, t in enumerate(all_turns):
        _, words = parse(t["script"])
        ew, sr = sf.read(DDIR / t["id"] / "edit.wav", dtype="float32")
        cuts = report[t["id"]]["react_cuts"]
        bounds, start, k = [], 0.0, 0
        texts, cur = [], []
        for i, x in enumerate(words):
            cur.append(x["w"])
            if x["react_after"] or i == len(words) - 1:
                end = cuts.get(str(i), len(ew) / sr) if i < len(words) - 1 else len(ew) / sr
                bounds.append((start, end, x["react_after"] or "none", " ".join(cur)))
                start, cur = end, []
        for k, (a, b, react, seg_text) in enumerate(bounds):
            sid = f"{n + 1:02d}_{k}"
            sf.write(sdir / f"{sid}.wav", ew[int(a * sr): int(b * sr)], sr)
            picks.append(dict(id=sid, turn=t["id"], speaker=t["speaker"], text=seg_text, react=react,
                              path=str(sdir / f"{sid}.wav"),
                              gap=(0.0 if k else (0.3 if n else 0.0) + float(t.get("pre", 0)))))
    (DDIR / "picks.json").write_text(json.dumps(picks, indent=1), encoding="utf-8")
    print(f"wrote {len(picks)} segments for the mix")
