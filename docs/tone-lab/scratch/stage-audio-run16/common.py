"""Shared loaders for the run-16 stage-audio experiment (runs on the workstation 4090)."""
import re
from pathlib import Path

import numpy as np
import torch
import yaml

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
OUT = ROOT / "out"

MODEL_IDS = {
    "design": "Qwen/Qwen3-TTS-12Hz-1.7B-VoiceDesign",
    "base": "Qwen/Qwen3-TTS-12Hz-1.7B-Base",
}
ASR_ID = "openai/whisper-large-v3-turbo"


def load_yaml(name):
    return yaml.safe_load((SRC / name).read_text(encoding="utf-8"))


def load_tts(kind):
    from qwen_tts import Qwen3TTSModel

    return Qwen3TTSModel.from_pretrained(
        MODEL_IDS[kind], device_map="cuda:0", dtype=torch.bfloat16, attn_implementation="sdpa"
    )


def load_asr():
    from transformers import pipeline

    return pipeline(
        "automatic-speech-recognition", model=ASR_ID, torch_dtype=torch.float16, device="cuda:0"
    )


def transcribe(asr, wav, sr):
    import librosa

    if sr != 16000:
        wav = librosa.resample(wav.astype(np.float32), orig_sr=sr, target_sr=16000)
    return asr({"raw": wav.astype(np.float32), "sampling_rate": 16000}, generate_kwargs={"language": "en"})["text"]


_NUM = {"u s b c": "usbc", "a p r": "apr"}


_ONES = "zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen " \
        "sixteen seventeen eighteen nineteen".split()
_TENS = "_ _ twenty thirty forty fifty sixty seventy eighty ninety".split()


def _say(n):
    """Number to words in British style (212 -> two hundred and twelve)."""
    if n < 20:
        return _ONES[n]
    if n < 100:
        return _TENS[n // 10] + ("" if n % 10 == 0 else " " + _ONES[n % 10])
    if n < 1000:
        rest = n % 100
        return _ONES[n // 100] + " hundred" + ("" if rest == 0 else " and " + _say(rest))
    return str(n)


def _spell_numbers(s):
    # Whisper writes "£212", "29%", "$94", "140": spell them as the script does
    s = re.sub(r"[£�]\s*(\d+)", lambda m: _say(int(m[1])) + " pounds", s)
    s = re.sub(r"\$\s*(\d+)", lambda m: _say(int(m[1])) + " dollars", s)
    s = re.sub(r"(\d+)\s*%", lambda m: _say(int(m[1])) + " percent", s)
    s = re.sub(r"(\d+)-pound", lambda m: _say(int(m[1])) + " pound", s)
    s = re.sub(r"\b(\d+)\b", lambda m: _say(int(m[1])), s)
    s = re.sub(r"\ba hundred\b", "one hundred", s)
    s = re.sub(r"\b(pound|dollar)s\b", r"\1", s)
    return s


def norm_words(s):
    s = _spell_numbers(s.lower())
    for k, v in _NUM.items():
        s = s.replace(k, v)
    s = s.replace("usb-c", "usbc").replace("-", " ")
    s = re.sub(r"[^a-z0-9' ]+", " ", s)
    return s.split()


def wer(ref, hyp):
    r, h = norm_words(ref), norm_words(hyp)
    d = list(range(len(h) + 1))
    for i in range(1, len(r) + 1):
        prev, d[0] = d[0], i
        for j in range(1, len(h) + 1):
            cur = min(d[j] + 1, d[j - 1] + 1, prev + (r[i - 1] != h[j - 1]))
            prev, d[j] = d[j], cur
    return d[len(h)] / max(1, len(r))


def speaker_embedding(base_model, wav, sr):
    item = base_model.create_voice_clone_prompt(ref_audio=(wav, sr), x_vector_only_mode=True)[0]
    e = item.ref_spk_embedding.float().flatten()
    return (e / e.norm()).cpu()
