"""Local Kokoro v1.0 configuration using the existing sherpa-onnx runtime."""
from pathlib import Path

# Official sherpa-onnx scripts/kokoro/v1.0/generate_voices_bin.py mapping.
KOKORO_VOICES = (
    'af_alloy', 'af_aoede', 'af_bella', 'af_heart', 'af_jessica',
    'af_kore', 'af_nicole', 'af_nova', 'af_river', 'af_sarah', 'af_sky',
    'am_adam', 'am_echo', 'am_eric', 'am_fenrir', 'am_liam', 'am_michael',
    'am_onyx', 'am_puck', 'am_santa',
)


def speaker_id(name):
    if name not in KOKORO_VOICES:
        raise ValueError(f'Unknown Kokoro voice: {name}. Choose a voice in Voice & listening.')
    return KOKORO_VOICES.index(name)


def configuration(folder: Path, sherpa):
    models = sorted(folder.rglob('*.onnx'))
    tokens = next(folder.rglob('tokens.txt'), None)
    espeak = next(folder.rglob('espeak-ng-data'), None)
    if len(models) != 1 or not tokens or not espeak:
        raise RuntimeError('Voice assets are missing or ambiguous; run setup/repair')
    voices = next(folder.rglob('voices.bin'), None)
    if voices:
        lexicon = voices.parent / 'lexicon-us-en.txt'
        if not lexicon.is_file():
            raise RuntimeError('Kokoro English lexicon is missing; run setup/repair')
        backend = 'kokoro'
        model = sherpa.OfflineTtsModelConfig(kokoro=sherpa.OfflineTtsKokoroModelConfig(
            model=str(models[0]), voices=str(voices), tokens=str(tokens),
            data_dir=str(espeak), lexicon=str(lexicon), lang='en-us'),
            num_threads=2, provider='cpu', debug=False)
    else:
        # Keep previously selected custom Piper folders usable after an upgrade.
        backend = 'piper'
        model = sherpa.OfflineTtsModelConfig(vits=sherpa.OfflineTtsVitsModelConfig(
            model=str(models[0]), tokens=str(tokens), data_dir=str(espeak)),
            num_threads=2, provider='cpu', debug=False)
    return sherpa.OfflineTtsConfig(model=model, max_num_sentences=1), backend
