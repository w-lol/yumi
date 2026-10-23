"""Bounded RAM-only audio. Speaker-safe mode is explicitly half duplex."""
from __future__ import annotations
import queue
import re
import threading
import time
from collections import deque
import numpy as np
from .voice import configuration, speaker_id
from .delivery import speech_text, STYLES


class Utterances:
    def __init__(self, threshold=0.015, rate=16000):
        self.threshold, self.rate = threshold, rate
        self.frames = []
        self.pre = deque(maxlen=5)
        self.silence = 0
        self.voiced = 0
        self.noise = 0.003

    def clear(self):
        self.frames.clear()
        self.pre.clear()
        self.silence = self.voiced = 0

    def feed(self, frame):
        rms = float(np.sqrt(np.mean(frame * frame) + 1e-12))
        threshold = max(self.threshold, self.noise * 3)
        speaking = rms > threshold
        if not speaking and not self.frames:
            self.noise = 0.98 * self.noise + 0.02 * min(rms, self.threshold)
        if speaking:
            if not self.frames:
                self.frames.extend(self.pre)
                self.pre.clear()
            self.frames.append(frame.copy())
            self.voiced += len(frame)
            self.silence = 0
        elif self.frames:
            self.frames.append(frame.copy())
            self.silence += len(frame)
        else:
            self.pre.append(frame.copy())
        count = sum(len(x) for x in self.frames)
        if self.frames and (self.silence >= self.rate * 0.55 or count >= self.rate * 12):
            result = np.concatenate(self.frames) if self.voiced >= self.rate * 0.20 else None
            self.clear()
            return rms, result, speaking
        return rms, None, speaking


class Audio:
    def __init__(self, paths, settings, event, input_text, presentation=None):
        self.paths, self.settings, self.event, self.input_text = paths, settings, event, input_text
        self.presentation = presentation or (lambda: {"energy":0.5})
        self.active = threading.Event()
        self.shutdown_event = threading.Event()
        self.silenced = threading.Event()
        self.blocks = queue.Queue(maxsize=40)
        self.speech = queue.Queue(maxsize=8)
        self.playback = queue.Queue(maxsize=32)
        self.epoch = 0
        self.capture_epoch = 0
        self.speaking = False
        self.last_played = -1000.0
        self.reference = deque(maxlen=12)
        self.tts = self.stt = None
        self.tts_backend = None
        self.input_stream = self.output_stream = None
        self.output_level = 0.0
        self.input_level = 0.0
        self.mouth = {'aa': 0.0, 'ih': 0.0, 'ou': 0.0}
        self.speech_style = 'neutral'
        self.threads = []
        self.current = np.empty(0, np.float32)
        self.current_epoch = 0
        self.current_offset = 0
        self.rate = 22050
        self.last_spoken_text = ''
        self.stats = {}
        self.last_interrupt = 0
        self.last_end_of_speech = 0.0
        self.turn_started_at = 0.0
        self.first_played_epoch = -1
        self.interrupted_at = None

    @staticmethod
    def devices():
        import sounddevice as sd
        return [dict(d, index=i) for i, d in enumerate(sd.query_devices())]

    def load(self):
        import os
        os.environ['ORT_DISABLE_TELEMETRY'] = '1'
        import onnxruntime
        onnxruntime.disable_telemetry_events()
        import sherpa_onnx
        voice = self.paths.inside(self.settings['voice_folder'])
        config, backend = configuration(voice, sherpa_onnx)
        if not config.validate():
            raise RuntimeError('Invalid local voice configuration')
        tts = sherpa_onnx.OfflineTts(config)
        selected = speaker_id(self.settings['kokoro_voice']) if backend == 'kokoro' else 0
        if not 0 <= selected < tts.num_speakers:
            raise RuntimeError('Selected voice is not present in this model; run setup/repair')
        self.tts = tts
        self.tts_backend = backend
        self.rate = self.tts.sample_rate
        from faster_whisper import WhisperModel
        self.stt = WhisperModel(str(self.paths.models / 'whisper'), device='cpu', compute_type='int8',
                                cpu_threads=2, num_workers=1, local_files_only=True)
        self.event('audio_ready', {'sample_rate': self.rate, 'backend': backend})

    @property
    def speaker_id(self):
        return speaker_id(self.settings['kokoro_voice']) if self.tts_backend == 'kokoro' else 0

    def start(self):
        import sounddevice as sd
        if self.active.is_set():
            return
        self.silenced.clear()
        if self.output_stream is None:
            self.output_stream = sd.OutputStream(samplerate=self.rate, channels=1, dtype='float32',
                blocksize=512, device=self.settings['output_device'], callback=self._output)
            self.output_stream.start()
        self.active.set()
        if self.settings['mic_enabled']:
            try:
                self.input_stream = sd.InputStream(samplerate=16000, channels=1, dtype='float32',
                    blocksize=480, device=self.settings['input_device'], callback=self._input)
                self.input_stream.start()
            except Exception:
                self.active.clear()
                raise
        if not self.threads:
            for target in (self._recognize, self._synthesize):
                thread = threading.Thread(target=target, daemon=True)
                thread.start()
                self.threads.append(thread)

    def _input(self, indata, frames, info, status):
        if not self.active.is_set():
            return
        if self.settings['audio_mode'] == 'speaker_safe' and (
                self.speaking or time.monotonic() - self.last_played < 0.65):
            return
        try:
            self.blocks.put_nowait((self.capture_epoch, indata[:, 0].copy()))
        except queue.Full:
            # Dropping oldest prevents unbounded latency.
            try:
                self.blocks.get_nowait()
            except queue.Empty:
                pass

    def _output(self, outdata, frames, info, status):
        outdata.fill(0)
        if not self.active.is_set() or self.silenced.is_set():
            self.output_level = 0
            self.speaking = False
            if self.interrupted_at is not None:
                self.stats['interruption_to_silent_callback_ms'] = (time.monotonic()-self.interrupted_at)*1000
                self.interrupted_at = None
            return
        filled = 0
        while filled < frames:
            if self.current_epoch != self.epoch or self.current_offset >= len(self.current):
                try:
                    epoch, self.current = self.playback.get_nowait()
                    self.current_epoch, self.current_offset = epoch, 0
                    if epoch != self.epoch:
                        self.current = np.empty(0, np.float32)
                        continue
                except queue.Empty:
                    break
            count = min(frames - filled, len(self.current) - self.current_offset)
            outdata[filled:filled+count, 0] = self.current[self.current_offset:self.current_offset+count] * self.settings['volume']
            self.current_offset += count
            filled += count
        self.output_level = float(np.sqrt(np.mean(outdata * outdata) + 1e-12))
        energy = min(1.0, self.output_level * 14)
        spectrum = np.abs(np.fft.rfft(outdata[:, 0]))
        total = float(spectrum.sum()) + 1e-9
        self.mouth = {'aa': energy, 'ou': energy * float(spectrum[:12].sum()) / total,
                      'ih': energy * float(spectrum[12:70].sum()) / total}
        self.speaking = filled > 0
        if filled:
            self.last_played = time.monotonic()
            if self.first_played_epoch != self.epoch and self.turn_started_at:
                self.first_played_epoch = self.epoch
                self.stats['input_to_first_played_audio_ms'] = (self.last_played-self.turn_started_at)*1000
        self.reference.append(outdata[:, 0].copy())

    def _recognize(self):
        vad = Utterances(self.settings['vad_threshold'])
        last_epoch = self.capture_epoch
        while not self.shutdown_event.is_set():
            try:
                epoch, frame = self.blocks.get(timeout=0.1)
            except queue.Empty:
                if not self.active.is_set():
                    vad.clear()
                continue
            if epoch != self.capture_epoch or not self.active.is_set():
                vad.clear()
                continue
            if epoch != last_epoch:
                vad.clear()
                last_epoch = epoch
            vad.threshold = self.settings['vad_threshold']
            rms, utterance, voiced = vad.feed(frame)
            self.input_level = rms
            if voiced and self.speaking and self.settings['audio_mode'] == 'headphones':
                if time.monotonic() - self.last_interrupt > 1:
                    self.last_interrupt = time.monotonic()
                    self.event('barge_in', {})
            if utterance is None or self.stt is None:
                continue
            self.last_end_of_speech = time.monotonic() - 0.55
            start = time.perf_counter()
            self.event('recognizing', {})
            try:
                segments, info = self.stt.transcribe(utterance, language='en', beam_size=1,
                    vad_filter=False, condition_on_previous_text=False, temperature=0)
                text = ' '.join(s.text.strip() for s in segments if s.no_speech_prob < 0.7).strip()
                self.stats['recognition_ms'] = (time.perf_counter() - start) * 1000
                if text and epoch == self.capture_epoch and self.active.is_set():
                    self.input_text(text)
            except Exception as exc:
                self.event('error', {'component': 'recognition', 'message': str(exc), 'error_type': type(exc).__name__})
            finally:
                utterance.fill(0)
                del utterance

    def say(self, text):
        text = speech_text(text)
        if not text: return
        if not self.settings['voice'] or not self.active.is_set() or not self.tts:
            return
        if self.silenced.is_set():
            return
        try:
            style = self.settings['speech_style']
            if style == 'auto': style = self.speech_style
            self.speech.put_nowait((self.epoch, text[:700], style))
        except queue.Full:
            self.event('speech_backpressure', {'reason': 'Speech queue full; remaining text stays in chat'})

    def new_turn(self, started_at=None):
        self.interrupt()
        self.turn_started_at = started_at or time.monotonic()
        self.silenced.clear()

    def _synthesize(self):
        while not self.shutdown_event.is_set():
            try:
                epoch, text, style = self.speech.get(timeout=0.1)
            except queue.Empty:
                continue
            if epoch != self.epoch or self.silenced.is_set() or not self.active.is_set():
                continue
            for word, pronunciation in self.settings['pronunciation'].items():
                text = text.replace(word, pronunciation)
            self.last_spoken_text = text
            start = time.perf_counter()
            first = True
            def callback(samples, progress):
                nonlocal first
                if epoch != self.epoch or self.silenced.is_set() or not self.active.is_set():
                    return 0
                if first:
                    self.stats['synthesis_first_chunk_ms'] = (time.perf_counter() - start) * 1000
                    first = False
                samples = np.asarray(samples, dtype=np.float32)
                for offset in range(0, len(samples), self.rate // 4):
                    while epoch == self.epoch and self.active.is_set():
                        try:
                            self.playback.put((epoch, samples[offset:offset+self.rate//4].copy()), timeout=0.1)
                            break
                        except queue.Full:
                            continue
                    if epoch != self.epoch:
                        return 0
                return 1
            try:
                speed = self.settings['voice_rate'] * STYLES.get(style, 1.0)
                audio = self.tts.generate(text, sid=self.speaker_id, speed=max(0.6,min(1.6,speed)), callback=callback)
                self.stats['synthesis_ms'] = (time.perf_counter() - start) * 1000
                self.stats['synthesis_rtf'] = (time.perf_counter() - start) / max(0.001, len(audio.samples) / audio.sample_rate)
                del audio
            except Exception as exc:
                self.event('error', {'component': 'speech', 'message': str(exc), 'error_type': type(exc).__name__})

    @staticmethod
    def drain(q):
        while True:
            try:
                value = q.get_nowait()
                del value
            except queue.Empty:
                break

    def interrupt(self, clear_capture=False):
        self.interrupted_at = time.monotonic()
        self.epoch += 1
        self.silenced.set()
        self.speaking = False
        self.output_level = 0
        self.current = np.empty(0, np.float32)
        self.mouth = {'aa': 0.0, 'ih': 0.0, 'ou': 0.0}
        self.current_offset = 0
        for q in (self.speech, self.playback):
            self.drain(q)
        self.reference.clear()
        if clear_capture:
            self.capture_epoch += 1
            self.drain(self.blocks)

    def pause(self):
        self.active.clear()
        self.interrupt(clear_capture=True)
        stream, self.input_stream = self.input_stream, None
        if stream:
            stream.abort()
            stream.close()
        self.input_level = 0

    def close(self):
        self.pause()
        self.shutdown_event.set()
        if self.output_stream:
            self.output_stream.abort()
            self.output_stream.close()
            self.output_stream = None
