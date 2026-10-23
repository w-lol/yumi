from __future__ import annotations
import base64
import threading
import time


class Perception:
    def __init__(self, settings, event, observe):
        self.settings, self.event, self.observe = settings, event, observe
        self.active = threading.Event()
        self.shutdown_event = threading.Event()
        self.preview = None
        self.latest = {}
        self.camera = None
        self.camera_index = None
        self.last_sample = 0
        self.thread = threading.Thread(target=self._run, daemon=True)
        self.thread.start()

    def pause(self):
        self.active.clear()
        self.preview = None
        self.latest.clear()

    def _run(self):
        import cv2
        import mss
        camera_retry = 0
        while not self.shutdown_event.wait(0.25):
            if not self.active.is_set():
                if self.camera:
                    self.camera.release()
                    self.camera = None
                continue
            frame = None
            try:
                if self.settings['camera_enabled']:
                    if self.camera and self.camera_index != self.settings['camera_index']:
                        self.camera.release()
                        self.camera = None
                    if self.camera is None and time.monotonic() >= camera_retry:
                        self.camera_index = self.settings['camera_index']
                        self.camera = cv2.VideoCapture(self.camera_index)
                        self.camera.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
                        self.camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
                        if not self.camera.isOpened():
                            self.camera.release()
                            self.camera = None
                            camera_retry = time.monotonic() + 30
                            self.event('device_error', {'component': 'camera', 'message': 'Camera unavailable; retry in 30 seconds'})
                    if self.camera:
                        ok, frame = self.camera.read()
                        if not ok:
                            self.camera.release()
                            self.camera = None
                            self.preview = None
                            self.latest.pop('camera', None)
                            camera_retry = time.monotonic() + 5
                            self.event('device_error', {'component': 'camera', 'message': 'Camera disconnected'})
                        elif self.active.is_set():
                            self.preview = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB) if self.settings['camera_preview'] else None
                elif self.camera:
                    self.camera.release()
                    self.camera = None
                    self.preview = None
                    self.latest.pop('camera', None)
                if not self.active.is_set():
                    self.preview = None
                    continue
                if time.monotonic() - self.last_sample < self.settings['vision_interval']:
                    continue
                self.last_sample = time.monotonic()
                last_source = getattr(self, 'last_sample_source', 'screen')
                if frame is not None and (last_source != 'camera' or not self.settings['screen_enabled']):
                    self._submit(frame, 'camera')
                    self.last_sample_source = 'camera'
                elif self.settings['screen_enabled'] and self.screen_allowed():
                    with mss.mss() as grabber:
                        import numpy as np
                        index = min(max(1, self.settings['monitor_index']), len(grabber.monitors)-1)
                        screen = np.asarray(grabber.grab(grabber.monitors[index]))[:, :, :3]
                        self._submit(screen, 'screen')
                    self.last_sample_source = 'screen'
            except Exception as exc:
                self.event('device_error', {'component': 'perception', 'message': str(exc), 'error_type': type(exc).__name__})
                self.shutdown_event.wait(5)
        if self.camera:
            self.camera.release()

    def screen_allowed(self):
        import os
        if not self.settings['screen_enabled']:
            return False
        exclusions = self.settings['screen_exclusions']
        if not exclusions:
            return True
        if os.name != 'nt':
            return False  # Fail closed if application exclusions cannot be enforced.
        try:
            import ctypes
            import psutil
            from ctypes import wintypes
            user = ctypes.windll.user32
            user.GetForegroundWindow.restype = wintypes.HWND
            handle = user.GetForegroundWindow()
            pid = wintypes.DWORD()
            user.GetWindowThreadProcessId(handle, ctypes.byref(pid))
            name = psutil.Process(pid.value).name().lower()
            return not any(item.lower() in name for item in exclusions)
        except Exception:
            return False

    def _submit(self, frame, source):
        import cv2
        scale = min(1, 640 / max(frame.shape[:2]))
        frame = cv2.resize(frame, None, fx=scale, fy=scale)
        ok, encoded = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 75])
        if ok and self.active.is_set():
            self.observe(source, base64.b64encode(encoded).decode('ascii'), time.time())

    def close(self):
        self.pause()
        self.shutdown_event.set()
        self.thread.join(timeout=2)
