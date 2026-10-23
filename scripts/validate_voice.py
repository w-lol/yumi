"""Real Kokoro synthesis without audio devices or waveform files (also runs on Windows CI)."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import sys
import time
import traceback

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from install_assets import download, safe_extract
from yumi.paths import Paths, atomic_json
from yumi.voice import configuration, speaker_id, KOKORO_VOICES


def run(root, output, prepare=False):
    paths = Paths(root)
    paths.activate()
    if prepare:
        entry = json.loads((ROOT / 'assets/bootstrap-artifacts.json').read_text())['voice']
        archive = paths.root / 'downloads' / entry['filename']
        download(entry['url'], archive, entry['sha256'], entry['bytes'])
        # A dedicated disposable validation root; never touch an installed voice.
        voice = paths.models / 'voice'
        if voice.exists():
            raise RuntimeError('--prepare needs a fresh validation root')
        safe_extract(archive, voice)
    import numpy as np
    import onnxruntime
    onnxruntime.disable_telemetry_events()
    import sherpa_onnx
    config, backend = configuration(paths.models / 'voice', sherpa_onnx)
    assert backend == 'kokoro' and config.validate()
    start = time.perf_counter()
    tts = sherpa_onnx.OfflineTts(config)
    result = {'platform': sys.platform, 'backend': backend, 'sample_rate': tts.sample_rate,
              'speakers': tts.num_speakers, 'load_seconds': time.perf_counter() - start,
              'samples': [], 'status': 'passed'}
    assert tts.sample_rate == 24000
    assert all(speaker_id(name) < tts.num_speakers for name in KOKORO_VOICES)
    for name in ('af_heart', 'af_bella'):
        chunks = []
        start = time.perf_counter()
        def callback(samples, progress):
            chunks.append({'samples': len(samples), 'seconds': time.perf_counter() - start})
            return 1
        audio = tts.generate('Hello, I am Yumi. Your local voice is ready.',
                             sid=speaker_id(name), speed=1.0, callback=callback)
        samples = np.asarray(audio.samples, dtype=np.float32)
        assert len(samples) > 2400 and np.isfinite(samples).all()
        assert np.max(np.abs(samples)) > 0.01 and chunks
        result['samples'].append({'voice': name, 'audio_seconds': len(samples) / audio.sample_rate,
                                  'synthesis_seconds': time.perf_counter() - start,
                                  'first_callback_seconds': chunks[0]['seconds']})
        samples.fill(0)
        del audio
    atomic_json(output, result)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=ROOT)
    parser.add_argument('--output', type=Path, default=ROOT / 'logs/kokoro-validation.json')
    parser.add_argument('--prepare', action='store_true')
    args = parser.parse_args()
    try:
        run(args.root, args.output, args.prepare)
    except Exception:
        if os.environ.get('GITHUB_ACTIONS') == 'true':
            detail = traceback.format_exc().replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
            print(f'::error::{detail}', flush=True)
        raise
