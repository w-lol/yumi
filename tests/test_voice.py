from pathlib import Path
import tempfile
import unittest
import sherpa_onnx
from yumi.audio import Audio
from yumi.paths import Paths
from yumi.settings import Settings
from yumi.voice import configuration, speaker_id


class VoiceTests(unittest.TestCase):
    def test_kokoro_selection_and_legacy_piper_folder(self):
        with tempfile.TemporaryDirectory(prefix='Voice assets with spaces ') as tmp:
            root = Path(tmp)
            for name in ('model.onnx', 'tokens.txt'):
                (root / name).touch()
            (root / 'espeak-ng-data').mkdir()
            config, backend = configuration(root, sherpa_onnx)
            self.assertEqual(backend, 'piper')
            self.assertEqual(config.model.vits.model, str(root / 'model.onnx'))
            (root / 'voices.bin').touch()
            with self.assertRaisesRegex(RuntimeError, 'lexicon'):
                configuration(root, sherpa_onnx)
            (root / 'lexicon-us-en.txt').touch()
            config, backend = configuration(root, sherpa_onnx)
            self.assertEqual(backend, 'kokoro')
            self.assertEqual(config.model.kokoro.voices, str(root / 'voices.bin'))
            settings = Settings(Paths(root / 'app'))
            audio = Audio(Paths(root / 'app'), settings, lambda *_: None, lambda _: None)
            audio.tts_backend = backend
            self.assertEqual(audio.speaker_id, 3)  # af_heart in the pinned voice table
            settings.set('kokoro_voice', 'af_bella')
            self.assertEqual(audio.speaker_id, 2)
            audio.tts_backend = 'piper'
            self.assertEqual(audio.speaker_id, 0)

    def test_invalid_voice_and_ambiguous_assets_rejected(self):
        with self.assertRaises(ValueError):
            speaker_id('voice-from-another-model')
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for name in ('first.onnx', 'second.onnx', 'tokens.txt'):
                (root / name).touch()
            (root / 'espeak-ng-data').mkdir()
            with self.assertRaisesRegex(RuntimeError, 'ambiguous'):
                configuration(root, sherpa_onnx)
