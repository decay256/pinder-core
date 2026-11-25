"""Word timestamps for the one-pass render, with faster-whisper on the CPU.

The transformers Whisper pipeline crashes natively with return_timestamps="word" on
this machine, so this step runs in the qwen3-tts-server venv, which has faster-whisper.

Output: out/ttsd/words.json  [[word, start, end], ...]
"""
import json
from pathlib import Path

from faster_whisper import WhisperModel

OUT = Path(__file__).resolve().parent.parent / "out" / "ttsd"
model = WhisperModel("small.en", device="cpu", compute_type="int8")
segments, _ = model.transcribe(str(OUT / "run16-ttsd.wav"), language="en", word_timestamps=True,
                               condition_on_previous_text=False)
words = [[w.word, round(w.start, 3), round(w.end, 3)] for s in segments for w in s.words]
(OUT / "words.json").write_text(json.dumps(words), encoding="utf-8")
print(len(words), "words")
