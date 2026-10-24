"""Lifecycle of the locally built player; never discovers or terminates other processes."""
from __future__ import annotations
import os
import subprocess
import threading


class UnityPlayer:
    def __init__(self, paths):
        self.paths = paths
        self.process = None
        self.lock = threading.Lock()

    def start(self):
        if os.name != 'nt':
            raise RuntimeError('The Unity desktop player requires Windows.')
        with self.lock:
            if self.process and self.process.poll() is None:
                return
            executable = self.paths.root / 'Builds/YumiAvatar/YumiAvatar.exe'
            if not executable.is_file():
                raise RuntimeError('Build the original avatar locally using scripts/Build-UnityAvatar.ps1 first. This update includes source tools, not a verified Unity executable.')
            startup = subprocess.STARTUPINFO()
            startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startup.wShowWindow = 4  # SW_SHOWNOACTIVATE
            self.process = subprocess.Popen([str(executable), '--yumi-root', str(self.paths.root),
                '-popupwindow', '-screen-fullscreen', '0', '-logFile', str(self.paths.logs / 'unity-player.log')],
                cwd=str(executable.parent), startupinfo=startup)

    def close(self):
        with self.lock:
            process, self.process = self.process, None
            if process is None or process.poll() is not None:
                return
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=2)
