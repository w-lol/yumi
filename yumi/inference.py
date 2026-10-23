from __future__ import annotations
import json
import os
import secrets
import socket
import subprocess
import threading
import time
import uuid
from collections import deque
import httpx


class FinalTextFilter:
    """Incremental suppression of reasoning/control tags, including split tags."""
    def __init__(self):
        self.buffer = ''
        self.hidden = None
        self.blocked = False

    def feed(self, text, final=False):
        if self.blocked:
            return ''
        self.buffer += text
        out = ''
        while self.buffer:
            if self.hidden:
                end = '</' + self.hidden + '>'
                pos = self.buffer.lower().find(end)
                if pos < 0:
                    self.buffer = self.buffer[-len(end):]
                    break
                self.buffer = self.buffer[pos + len(end):]
                self.hidden = None
                continue
            pos = self.buffer.find('<')
            if pos == -1:
                out += self.buffer
                self.buffer = ''
                break
            out += self.buffer[:pos]
            self.buffer = self.buffer[pos:]
            end = self.buffer.find('>')
            if end == -1:
                if len(self.buffer) > 100:
                    self.buffer = ''
                break
            tag = self.buffer[1:end].strip().lower()
            self.buffer = self.buffer[end + 1:]
            if tag in ('think', 'analysis', 'reasoning'):
                self.hidden = tag
            elif tag in ('tool_call', 'function_call'):
                self.hidden = tag
            # Other markup tags are discarded, never spoken.
        if final:
            self.buffer = ''
        return out


class LocalModel:
    def __init__(self, paths, settings, event):
        self.paths, self.settings, self.event = paths, settings, event
        self.process = None
        self.port = None
        self.key = secrets.token_urlsafe(32)
        self.key_file = self.paths.tmp / ("server-key-" + uuid.uuid4().hex + ".txt")
        self.response = None
        self.ready = False
        self.lock = threading.Lock()
        self.cancel_event = threading.Event()
        self.restart_times = deque(maxlen=3)
        self.job = None
        self.lifecycle_lock = threading.RLock()

    @property
    def url(self):
        return f'http://127.0.0.1:{self.port}'

    def start(self, cancel=None):
        with self.lifecycle_lock:
            return self._start(cancel)

    def _start(self, cancel=None):
        if self.process and self.process.poll() is None and self.ready:
            return
        self.stop()
        executable = 'llama-server.exe' if os.name == 'nt' else 'llama-server'
        candidates = list((self.paths.root / 'runtime' / 'llama').rglob(executable))
        model_files = list((self.paths.models / 'chat').glob('*.gguf'))
        model = next((p for p in model_files if 'mmproj' not in p.name), None)
        projector = next((p for p in model_files if 'mmproj' in p.name), None)
        if not candidates or not model:
            raise RuntimeError('Local model/runtime missing. Run setup or Repair. Text controls and memory remain available.')
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0))
            self.port = sock.getsockname()[1]
        key_file = self.key_file
        key_file.write_text(self.key, encoding='utf-8')
        if os.name != 'nt':
            key_file.chmod(0o600)
        command = [str(candidates[0]), '-m', str(model), '--host', '127.0.0.1',
                   '--port', str(self.port), '--api-key-file', str(key_file),
                   '-c', str(self.settings['context']), '-ngl', str(self.settings['gpu_layers']),
                   '-t', str(self.settings['threads']), '--parallel', '1', '--log-disable',
                   '--reasoning', 'off', '--chat-template-kwargs', '{"enable_thinking":false}']
        if projector:
            command.extend(['--mmproj', str(projector)])
        flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
        self.process = subprocess.Popen(command, cwd=candidates[0].parent,
                                        stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                        stderr=subprocess.DEVNULL, creationflags=flags)
        if os.name == 'nt':
            from .windows import attach_kill_job
            self.job = attach_kill_job(self.process)
        deadline = time.monotonic() + 180
        with httpx.Client(trust_env=False, timeout=2) as client:
            while time.monotonic() < deadline:
                if cancel and cancel.is_set():
                    self.stop()
                    raise RuntimeError('Startup cancelled')
                if self.process.poll() is not None:
                    raise RuntimeError('llama-server exited; check GPU driver/native DLLs or select CPU and restart')
                try:
                    response = client.get(self.url + '/health', headers={'Authorization': 'Bearer ' + self.key})
                    if response.status_code == 200:
                        self.ready = True
                        self.event('model_ready', {'backend': 'llama.cpp', 'model': model.name})
                        return
                except httpx.HTTPError:
                    pass
                if cancel:
                    cancel.wait(0.25)
                else:
                    time.sleep(0.25)
        self.stop()
        raise TimeoutError('Model startup exceeded 180 seconds')

    def stream(self, messages, cancel, max_tokens=None):
        if not self.ready or not self.process or self.process.poll() is not None:
            self.ready = False
            raise RuntimeError('Local model is unavailable; use Restart')
        with self.lock:
            with httpx.Client(trust_env=False, timeout=httpx.Timeout(90, connect=3)) as client:
                with client.stream('POST', self.url + '/v1/chat/completions',
                                   headers={'Authorization': 'Bearer ' + self.key},
                                   json={'messages': messages, 'stream': True,
                                         'temperature': self.settings['temperature'],
                                         'chat_template_kwargs': {'enable_thinking': False},
                                         'max_tokens': max_tokens or self.settings['max_tokens']}) as response:
                    self.response = response
                    response.raise_for_status()
                    clean = FinalTextFilter()
                    try:
                        for line in response.iter_lines():
                            if cancel.is_set():
                                break
                            if not line.startswith('data: '):
                                continue
                            if line[6:] == '[DONE]':
                                break
                            record = json.loads(line[6:])
                            choices = record.get('choices', [])
                            if not choices:
                                continue
                            delta = choices[0].get('delta', {})
                            # reasoning_content and tool_calls are never delivered.
                            text = clean.feed(delta.get('content') or '')
                            if text:
                                yield text
                    finally:
                        self.response = None

    def cancel(self):
        # Close the client stream off the UI thread; stale chunks are gated by turn ID.
        response = self.response
        if response:
            def close():
                try:
                    response.close()
                except Exception:
                    pass
            threading.Thread(target=close, daemon=True).start()

    def stop(self):
        with self.lifecycle_lock:
            self._stop()

    def _stop(self):
        self.ready = False
        self.cancel()
        process, self.process = self.process, None
        if process and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=3)
        if self.job:
            import ctypes
            ctypes.windll.kernel32.CloseHandle(self.job)
            self.job = None
        key_file = self.key_file
        key_file.unlink(missing_ok=True)
