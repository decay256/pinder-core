"""Build the stage timeline: picked speech segments, audience reactions, room, bed.

Runs in the Qwen venv (numpy, scipy, librosa, transformers). Reaction takes are ranked
with LAION CLAP against their prompt; the top three per reaction rotate so the same laugh
is not heard twice in a row.

Output: out/<stem>.wav, out/<stem>.mp3, out/<stem>.timeline.json (stem defaults to run16-stage)
"""
import json
import subprocess
from pathlib import Path

import librosa
import numpy as np
import soundfile as sf
import torch
import yaml
from scipy.signal import butter, fftconvolve, sosfilt

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "out"
SR = 48000
rng = np.random.default_rng(16)

import sys

# argv: [picks.json] [output stem]; default is the per-line render
DRY = "--dry" in sys.argv  # the room and the mic, no audience: half-second beat at laugh points
argv = [a for a in sys.argv[1:] if a != "--dry"]
PICKS = Path(argv[0]) if argv else OUT / "lines" / "picks.json"
STEM = argv[1] if len(argv) > 1 else "run16-stage"
picks = json.loads(PICKS.read_text(encoding="utf-8"))
cues = yaml.safe_load((ROOT / "src" / "cues.yaml").read_text(encoding="utf-8"))
reactions = yaml.safe_load((ROOT / "src" / "laughs.yaml").read_text(encoding="utf-8"))["reactions"]

PAN = {"zyx": -0.3, "sable": 0.3}
REACT_DB = {"titter": -7, "laugh": -2, "big": 0, "huge": 1.5, "ooh": -4, "groan": -4, "aww": -5, "applause": -1}


def db(x):
    return 10 ** (x / 20)


def to_stereo(x):
    return np.stack([x, x]) if x.ndim == 1 else x


def load(path, trim_db=40):
    w, sr = sf.read(path, dtype="float32", always_2d=True)
    w = w.T
    if sr != SR:
        w = librosa.resample(w, orig_sr=sr, target_sr=SR)
    if trim_db:
        _, (a, b) = librosa.effects.trim(w.mean(0), top_db=trim_db)
        w = w[:, a:b]
    return w


def fade(w, fin=0.01, fout=0.05):
    n_in, n_out = int(fin * SR), int(fout * SR)
    w = w.copy()
    if n_in:
        w[:, :n_in] *= np.linspace(0, 1, n_in)
    if n_out:
        w[:, -n_out:] *= np.linspace(1, 0, n_out)
    return w


def room_ir(rt60=0.55, seconds=1.2):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    env = np.exp(-6.91 * t / rt60)
    ir = rng.standard_normal((2, n)) * env
    ir = sosfilt(butter(2, 6000, "low", fs=SR, output="sos"), ir)
    for d, g in [(0.011, 0.5), (0.017, 0.4), (0.023, 0.3)]:  # early reflections off a low ceiling
        ir[:, int(d * SR)] += g
    ir[:, 0] = 0
    return ir / np.abs(ir).sum(axis=1, keepdims=True) * 4


IR = room_ir()


def reverb(w, wet):
    tail = np.stack([fftconvolve(w[c], IR[c])[: w.shape[1] + IR.shape[1]] for c in range(2)])
    out = np.zeros_like(tail)
    out[:, : w.shape[1]] += w
    return out + wet * tail


def mic_chain(mono):
    x = sosfilt(butter(2, 110, "high", fs=SR, output="sos"), mono)
    pres = sosfilt(butter(2, [2200, 5000], "band", fs=SR, output="sos"), x)
    x = x + 0.35 * pres
    x = x / (np.sqrt(np.mean(x**2)) + 1e-9) * db(-20)
    x = np.tanh(2.2 * x) / 2.2  # a little PA drive
    return x


def pan(mono, p):
    a = (p + 1) * np.pi / 4
    return np.stack([np.cos(a) * mono, np.sin(a) * mono])


# --- rank reaction takes with CLAP ---------------------------------------------------
from transformers import ClapModel, ClapProcessor

clap = ClapModel.from_pretrained("laion/larger_clap_general").to("cuda").eval()
proc = ClapProcessor.from_pretrained("laion/larger_clap_general")
bank, ranking = {}, {}
for name, r in reactions.items():
    paths = sorted((OUT / "laughs" / name).glob("take*.wav"))
    auds = [librosa.resample(load(p, trim_db=None).mean(0), orig_sr=SR, target_sr=48000) for p in paths]
    with torch.no_grad():
        ti = proc(text=[r["prompt"]], return_tensors="pt", padding=True).to("cuda")
        ai = proc(audios=auds, sampling_rate=48000, return_tensors="pt").to("cuda")
        te = clap.get_text_features(**ti)
        ae = clap.get_audio_features(**ai)
        sims = torch.nn.functional.cosine_similarity(ae, te).cpu().numpy()
    order = np.argsort(-sims)
    ranking[name] = [(paths[i].name, round(float(sims[i]), 3)) for i in order]
    bank[name] = [fade(load(paths[i], trim_db=35), 0.005, 0.8).mean(0) for i in order[:4]]
del clap
torch.cuda.empty_cache()
use_count = {k: 0 for k in bank}


def take_reaction(name):
    """The model is mono: two different takes, one each side, read as a wider, bigger room.

    Returns the stereo reaction and how long the performer waits (laughs.yaml hold).
    Generated laughs decay far more slowly than a real room, so the tail is faded from
    just before the performer comes back in, as an engineer pulls the audience mics down.
    """
    takes = bank[name]
    i = use_count[name]
    use_count[name] += 1
    a, b = takes[i % len(takes)], takes[(i + 1) % len(takes)]
    b = np.concatenate([np.zeros(int(0.03 * SR), np.float32), b])  # the far side of the room is late
    n = max(len(a), len(b))
    a, b = np.pad(a, (0, n - len(a))), np.pad(b, (0, n - len(b)))
    hold = reactions[name]["hold"]
    if hold == 0:  # walk-on / walk-off applause runs its length
        hold = len(a) / SR
    cut, tail = int(max(0.3, hold - 0.2) * SR), int(1.3 * SR)
    st = np.stack([0.85 * a + 0.4 * b, 0.4 * a + 0.85 * b])[:, : cut + tail]
    if st.shape[1] > cut:
        st[:, cut:] *= np.linspace(1, 0, st.shape[1] - cut)
    return st * db(REACT_DB.get(name, 0) - 3), hold


# --- timeline --------------------------------------------------------------------------
events = []  # (start_s, stereo, kind, label)
t = 0.0
if DRY:
    t = 0.8
else:
    opening, _ = take_reaction(cues.get("opening", "applause"))
    events.append((1.0, opening, "crowd", "opening"))
    t = 1.0 + min(opening.shape[1] / SR, 6.0)

prev_speaker, prev_line = None, None
for p in picks:
    line_no = int(p["id"].split("_")[0])
    if "gap" in p:  # directed render: gaps and beats are already set per turn
        gap = p["gap"]
    elif prev_speaker is None:
        gap = 0.0
    elif p["speaker"] != prev_speaker:
        gap = 0.3
    elif line_no == prev_line:
        gap = 0.12
    else:
        gap = 0.28
    t += gap + float(p.get("pause", 0) or 0)
    seg_path = Path(p["path"]) if "path" in p else OUT / "lines" / p["id"] / p["take"]
    mono = load(seg_path).mean(0)
    voice = pan(mic_chain(mono), PAN[p["speaker"]])
    events.append((t, voice, "speech", p["id"]))
    end = t + mono.shape[0] / SR
    react = p["react"]
    if react != "none" and DRY:
        t = end + 0.5
    elif react != "none":
        rw, hold = take_reaction(react)
        events.append((end + 0.18, rw, "crowd", f"{p['id']}:{react}"))
        t = end + 0.18 + hold
    else:
        t = end
    prev_speaker, prev_line = p["speaker"], line_no

if DRY:
    total = t + 2.0
else:
    closing, _ = take_reaction("applause")
    events.append((t + 1.2, closing, "crowd", "closing applause"))
    total = t + 1.2 + closing.shape[1] / SR + 2.0

n = int(total * SR) + SR
speech_bus = np.zeros((2, n), np.float32)
crowd_bus = np.zeros((2, n), np.float32)
for start, w, kind, _ in events:
    a = int(start * SR)
    bus = speech_bus if kind == "speech" else crowd_bus
    bus[:, a : a + w.shape[1]] += w[:, : n - a]

# duck the crowd under speech, as a live engineer riding the audience mics would
act = np.abs(speech_bus).mean(0)
act = np.convolve(act, np.ones(int(0.12 * SR)) / int(0.12 * SR), mode="same")
act = np.clip(act / (act.max() * 0.15 + 1e-9), 0, 1)
crowd_bus *= 1 - 0.7 * act

# room-tone bed
mm = [load(p, trim_db=None).mean(0) for p in sorted((OUT / "laughs" / "murmur").glob("take*.wav"))[:2]]
murmur = np.stack([mm[0], mm[-1][: len(mm[0])] if len(mm[-1]) >= len(mm[0]) else np.pad(mm[-1], (0, len(mm[0]) - len(mm[-1])))])
reps = int(np.ceil(n / murmur.shape[1]))
bed = np.tile(murmur, (1, reps))[:, :n] * db(-26 if not DRY else -40)
bed *= 1 - 0.6 * act

mix = reverb(speech_bus, 0.15)[:, :n] + reverb(crowd_bus, 0.08)[:, :n] + bed
mix[:, -int(2 * SR):] *= np.linspace(1, 0, int(2 * SR))
mix /= np.abs(mix).max() / 0.89

wav = OUT / f"{STEM}.wav"
sf.write(wav, mix.T, SR, subtype="PCM_24")
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(wav), "-af", "loudnorm=I=-16:TP=-1.5",
                "-b:a", "192k", str(OUT / f"{STEM}.mp3")], check=True)
(OUT / f"{STEM}.timeline.json").write_text(json.dumps(
    {"duration_s": round(total, 1), "reaction_ranking": ranking,
     "events": [(round(s, 2), k, lbl) for s, _, k, lbl in events]}, indent=1), encoding="utf-8")
print(f"wrote {wav} ({total/60:.1f} min)")
