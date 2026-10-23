from __future__ import annotations
import html
import json
import os
from pathlib import Path
import subprocess
import threading
import time
from PySide6.QtCore import Qt, QTimer, QObject, Signal, QByteArray
from PySide6.QtGui import QImage, QPixmap, QFont
from PySide6.QtWidgets import (QApplication, QMainWindow, QWidget, QHBoxLayout, QVBoxLayout,
    QLabel, QPushButton, QTabWidget, QTextBrowser, QTextEdit, QLineEdit, QFormLayout,
    QCheckBox, QDoubleSpinBox, QSpinBox, QComboBox, QListWidget, QListWidgetItem,
    QGroupBox, QMessageBox, QInputDialog, QFileDialog, QSplitter, QScrollArea)
from .controller import Controller
from .avatar import Avatar
from .settings import PERSONA
from .paths import atomic_json

STYLE = '''
QWidget { background: #0e1420; color: #dce5f3; font-family: 'Segoe UI'; font-size: 13px; }
QMainWindow { background: #0e1420; }
QTabWidget::pane { border: 1px solid #29354a; border-radius: 8px; }
QTabBar::tab { background: #172032; padding: 10px 14px; margin: 2px; border-radius: 5px; }
QTabBar::tab:selected { background: #23475b; color: #91e8ff; }
QPushButton { background: #20334a; border: 1px solid #35516b; border-radius: 6px; padding: 8px 12px; }
QPushButton:hover { background: #2c4b65; }
QPushButton:disabled { color: #687386; background: #172030; }
QLineEdit, QTextEdit, QTextBrowser, QListWidget, QComboBox, QSpinBox, QDoubleSpinBox {
 background: #111b2a; border: 1px solid #304058; border-radius: 5px; padding: 6px; selection-background-color: #365b76; }
QGroupBox { border: 1px solid #293b53; margin-top: 14px; padding-top: 14px; border-radius: 6px; }
QGroupBox::title { subcontrol-origin: margin; left: 12px; color: #82d8ed; }
QLabel#title { font-size: 26px; font-weight: 650; color: #ecf7ff; }
QPushButton#stop { background: #7b2943; border-color: #bd4267; font-weight: bold; }
'''

class Bridge(QObject):
    event = Signal(object)


def button(text, action, layout):
    value = QPushButton(text)
    value.clicked.connect(action)
    layout.addWidget(value)
    return value


class Board(QMainWindow):
    def __init__(self, paths, settings):
        super().__init__()
        self.paths, self.settings = paths, settings
        self.setWindowTitle('Yumi Control Board')
        self.resize(1160, 820)
        self.bridge = Bridge()
        self.bridge.event.connect(self.on_event)
        self.core = Controller(paths, settings, self.bridge.event.emit)
        self.avatar = Avatar(self.core, settings, self.show_board)
        self.chat_records = []
        self.stream_text = ''
        self.stream_turn = ''
        self.errors = []
        self.closing = False
        container = QWidget()
        layout = QVBoxLayout(container)
        layout.setContentsMargins(22, 18, 22, 18)
        header = QHBoxLayout()
        title = QLabel('YUMI  /  CONTROL BOARD')
        title.setObjectName('title')
        header.addWidget(title)
        header.addStretch()
        self.status = QLabel('INACTIVE · sensors off')
        header.addWidget(self.status)
        layout.addLayout(header)
        controls = QHBoxLayout()
        button('Start / Resume', self.core.start, controls)
        button('Pause', self.core.pause, controls)
        button('Restart workers', self.core.restart, controls)
        button('Stop speaking', lambda: self.core.cancel(silence_only=True), controls)
        button('Cancel turn', self.core.cancel, controls)
        stop = button('■  EMERGENCY STOP', self.core.stop, controls)
        stop.setObjectName('stop')
        layout.addLayout(controls)
        self.banner = QLabel('Local processing · No machine actions · Start enables the configured sensors')
        self.banner.setWordWrap(True)
        layout.addWidget(self.banner)
        self.tabs = QTabWidget()
        self.tabs.setUsesScrollButtons(True)
        layout.addWidget(self.tabs, 1)
        self.overview_tab()
        self.conversation_tab()
        self.personality_tab()
        self.avatar_tab()
        self.voice_tab()
        self.awareness_tab()
        self.memory_tab()
        self.models_tab()
        self.diagnostics_tab()
        self.maintenance_tab()
        self.setCentralWidget(container)
        self.setStyleSheet(STYLE)
        if settings['board_geometry']:
            self.restoreGeometry(QByteArray.fromHex(settings['board_geometry'].encode()))
        self.timer = QTimer(self)
        self.timer.timeout.connect(self.refresh)
        self.timer.start(500)
        self.refresh_memory()
        self.refresh_devices()
        self.refresh()

    def page(self, name):
        widget = QWidget()
        layout = QVBoxLayout(widget)
        layout.setContentsMargins(16, 16, 16, 16)
        area = QScrollArea()
        area.setWidgetResizable(True)
        area.setWidget(widget)
        self.tabs.addTab(area, name)
        return layout

    def check(self, form, label, key, after=None):
        value = QCheckBox(label)
        value.setChecked(self.settings[key])
        def change(checked):
            self.settings.set(key, checked)
            if after: after()
        value.toggled.connect(change)
        form.addRow(value)
        return value

    def number(self, form, label, key, low, high, step=1, after=None):
        value = QSpinBox() if isinstance(self.settings[key], int) else QDoubleSpinBox()
        value.setRange(low, high)
        value.setSingleStep(step)
        value.setValue(self.settings[key])
        def change(number):
            self.settings.set(key, number)
            if after: after()
        value.valueChanged.connect(change)
        form.addRow(label, value)
        return value

    def overview_tab(self):
        layout = self.page('Overview')
        self.health = QTextBrowser()
        layout.addWidget(self.health)
        self.state_view = QLabel()
        self.state_view.setWordWrap(True)
        layout.addWidget(self.state_view)
        group = QGroupBox('Future machine control — unavailable')
        form = QVBoxLayout(group)
        label = QLabel('No OS, browser, mouse, keyboard, file-action or shell adapters are installed.\nThe versioned registry and policy boundary exist; unsupported requests return an explicit result.')
        label.setWordWrap(True)
        form.addWidget(label)
        self.registry_view = QLabel('Registered action adapters: 0')
        form.addWidget(self.registry_view)
        layout.addWidget(group)

    def conversation_tab(self):
        layout = self.page('Conversation')
        self.chat = QTextBrowser()
        self.chat.setMinimumHeight(350)
        layout.addWidget(self.chat, 1)
        self.memory_used = QLabel('Memory references: none')
        self.memory_used.setWordWrap(True)
        layout.addWidget(self.memory_used)
        row = QHBoxLayout()
        self.input = QLineEdit()
        self.input.setPlaceholderText('Talk to Yumi, or use remember: <fact> / forget: <memory ID>')
        self.input.returnPressed.connect(self.send)
        row.addWidget(self.input, 1)
        button('Send', self.send, row)
        layout.addLayout(row)
        row = QHBoxLayout()
        button('Regenerate speech', self.replay, row)
        button('Correct last transcript', self.correct_transcript, row)
        button('Clear working context', self.clear_context, row)
        button('Retry last input', lambda: self.core.submit(self.core.last_input[0]), row)
        layout.addLayout(row)
        self.history_query = QLineEdit()
        self.history_query.setPlaceholderText('Search engaged conversation archive')
        self.history_query.returnPressed.connect(self.search_history)
        layout.addWidget(self.history_query)
        button('Search archive', self.search_history, layout)
        self.history_results = QTextBrowser()
        self.history_results.setMaximumHeight(160)
        layout.addWidget(self.history_results)

    def personality_tab(self):
        layout = self.page('Personality')
        self.persona = QTextEdit(self.settings['persona'])
        layout.addWidget(self.persona, 1)
        row = QHBoxLayout()
        button('Save personality version', lambda: self.guard(lambda: self.settings.set_persona(self.persona.toPlainText())), row)
        button('Restore default', self.reset_persona, row)
        button('Revert previous version', self.revert_persona, row)
        layout.addLayout(row)
        form = QFormLayout()
        self.number(form, 'Talkativeness', 'talkativeness', 0.0, 1.0, 0.1)
        layout.addLayout(form)
        self.directive = QLineEdit()
        self.directive.setPlaceholderText('Direct steering instruction')
        layout.addWidget(self.directive)
        row = QHBoxLayout()
        button('Next turn', lambda: self.steer('turn'), row)
        button('This session', lambda: self.steer('session'), row)
        button('Remember preference', lambda: self.steer('persistent'), row)
        layout.addLayout(row)
        self.affect_label = QLabel()
        self.affect_label.setWordWrap(True)
        layout.addWidget(self.affect_label)
        button('Reset simulated affect', self.reset_affect, layout)
        layout.addWidget(QLabel('Affect is simulated presentation state; it is not evidence of consciousness.'))

    def avatar_tab(self):
        layout = self.page('Avatar')
        form = QFormLayout()
        for label, key in [('Show avatar', 'avatar_visible'), ('Always on top', 'avatar_topmost'),
                           ('Click-through (use this board to disable)', 'avatar_clickthrough'), ('Pin position', 'avatar_pinned')]:
            self.check(form, label, key, self.avatar.apply_settings)
        self.number(form, 'Size', 'avatar_scale', 0.5, 2.0, 0.1, self.avatar.apply_settings)
        self.number(form, 'Opacity', 'avatar_opacity', 0.2, 1.0, 0.05, self.avatar.apply_settings)
        self.number(form, 'Animation intensity', 'avatar_animation', 0.0, 1.0, 0.1)
        self.number(form, 'Movement intensity', 'avatar_motion', 0.0, 1.0, 0.1)
        self.check(form, 'Roam inside configured bounds', 'avatar_roaming')
        layout.addLayout(form)
        row = QHBoxLayout()
        button('Use current monitor as roaming area', self.roaming_monitor, row)
        button('Edit roaming bounds', lambda: self.edit_json_setting('roaming_rect'), row)
        button('Edit excluded rectangles', lambda: self.edit_json_setting('excluded_rects'), row)
        layout.addLayout(row)
        self.expression = QComboBox()
        self.expression.addItems(['auto', 'curious', 'calm'])
        self.expression.currentTextChanged.connect(lambda value: setattr(self.avatar, 'expression', value))
        layout.addWidget(QLabel('Expression preview'))
        layout.addWidget(self.expression)
        layout.addWidget(QLabel('Original animated procedural placeholder. Mouth motion follows actual playback amplitude.'))
        layout.addStretch()

    def voice_tab(self):
        from .delivery import STYLES
        from .voice import KOKORO_VOICES
        layout = self.page('Voice & listening')
        form = QFormLayout()
        delivery = QComboBox()
        delivery.addItems(['auto', *STYLES])
        delivery.setCurrentText(self.settings['speech_style'])
        delivery.currentTextChanged.connect(lambda value: self.settings.set('speech_style', value))
        form.addRow('Speech delivery (native speed; no pitch API)', delivery)
        voice = QComboBox()
        for name in KOKORO_VOICES:
            gender = 'female' if name.startswith('af_') else 'male'
            voice.addItem(f'{name[3:].title()} — US English ({gender})', name)
        voice.setCurrentIndex(max(0, voice.findData(self.settings['kokoro_voice'])))
        voice.currentIndexChanged.connect(lambda _: self.settings.set('kokoro_voice', voice.currentData()))
        form.addRow('Kokoro voice (next spoken sentence)', voice)
        self.input_device = QComboBox()
        self.output_device = QComboBox()
        form.addRow('Microphone', self.input_device)
        form.addRow('Speakers / headphones', self.output_device)
        self.input_device.currentIndexChanged.connect(lambda _: self.set_device('input_device', self.input_device))
        self.output_device.currentIndexChanged.connect(lambda _: self.set_device('output_device', self.output_device))
        self.check(form, 'Speak responses', 'voice')
        self.check(form, 'Microphone enabled (restart to apply)', 'mic_enabled')
        self.number(form, 'Speaking rate', 'voice_rate', 0.65, 1.5, 0.05)
        self.number(form, 'Volume', 'volume', 0.0, 1.0, 0.05)
        self.number(form, 'Speech energy threshold', 'vad_threshold', 0.001, 0.2, 0.001)
        mode = QComboBox()
        mode.addItem('Speaker-safe — HALF DUPLEX, no voice barge-in', 'speaker_safe')
        mode.addItem('Headphones — open listening with interruption', 'headphones')
        mode.setCurrentIndex(0 if self.settings['audio_mode'] == 'speaker_safe' else 1)
        mode.currentIndexChanged.connect(lambda _: self.settings.set('audio_mode', mode.currentData()))
        form.addRow('Listening mode', mode)
        self.number(form, 'Addressability threshold', 'attention_threshold', 0.1, 0.95, 0.05)
        self.number(form, 'Engagement timeout (seconds)', 'engagement_seconds', 5, 120)
        layout.addLayout(form)
        self.meters = QLabel()
        self.attention_view = QLabel('Addressability: no utterance assessed yet')
        self.attention_view.setWordWrap(True)
        layout.addWidget(self.meters)
        layout.addWidget(self.attention_view)
        row = QHBoxLayout()
        button('Refresh devices', self.refresh_devices, row)
        button('Speech test', self.speech_test, row)
        button('Choose local voice asset folder', self.select_voice, row)
        layout.addLayout(row)
        row = QHBoxLayout()
        button('I was talking to you', lambda: self.core.control('addressed'), row)
        button("I wasn't talking to you", lambda: self.core.control('unaddressed'), row)
        button('Undo feedback', self.core.attention.undo, row)
        button('Reset calibration', self.core.attention.reset, row)
        layout.addLayout(row)
        row = QHBoxLayout()
        button('Quiet for a minute', lambda: self.core.control('quiet'), row)
        button('You can talk now', lambda: self.core.control('talk'), row)
        button('Pronunciation dictionary', lambda: self.edit_json_setting('pronunciation'), row)
        layout.addLayout(row)
        layout.addWidget(QLabel('Default: local Kokoro v1.0, Heart voice, 24 kHz.\nKokoro selection applies to Kokoro assets; custom Piper folders keep their own voice.\nNo pitch control or verified speaker echo cancellation. Use headphones for voice interruption.'))
        layout.addStretch()

    def awareness_tab(self):
        layout = self.page('Awareness')
        form = QFormLayout()
        self.check(form, 'Camera capture', 'camera_enabled', self.clear_observations)
        self.check(form, 'Screen sampling', 'screen_enabled', self.clear_observations)
        self.check(form, 'Camera preview', 'camera_preview')
        self.number(form, 'Camera index', 'camera_index', 0, 10)
        self.number(form, 'Monitor index (primary = 1)', 'monitor_index', 1, 12)
        self.number(form, 'Inference sampling interval (seconds)', 'vision_interval', 10, 300)
        self.check(form, 'Grounded spontaneous remarks', 'proactive')
        self.number(form, 'Spontaneous remark cooldown (seconds)', 'proactive_cooldown', 60, 1800)
        layout.addLayout(form)
        button('Exclude foreground applications from screen capture', lambda: self.edit_json_setting('screen_exclusions'), layout)
        self.camera_preview = QLabel('Camera preview appears only while active')
        self.camera_preview.setMinimumHeight(220)
        self.camera_preview.setAlignment(Qt.AlignCenter)
        layout.addWidget(self.camera_preview)
        self.observations = QTextBrowser()
        layout.addWidget(self.observations)
        layout.addWidget(QLabel('Frames stay in RAM. Descriptions are uncertain model observations. Camera selection uses device indices.'))

    def memory_tab(self):
        layout = self.page('Memory')
        self.memory_query = QLineEdit()
        self.memory_query.setPlaceholderText('Search facts, preferences, tasks and episodes')
        self.memory_query.returnPressed.connect(self.refresh_memory)
        layout.addWidget(self.memory_query)
        button('Search / refresh', self.refresh_memory, layout)
        self.memories = QListWidget()
        self.memories.currentItemChanged.connect(self.show_memory)
        layout.addWidget(self.memories, 1)
        self.memory_detail = QTextBrowser()
        self.memory_detail.setMaximumHeight(170)
        layout.addWidget(self.memory_detail)
        row = QHBoxLayout()
        button('Add fact', self.add_memory, row)
        button('Edit / correct', self.edit_memory, row)
        button('Pin / unpin', self.pin_memory, row)
        button('Forget selected', self.forget_memory, row)
        layout.addLayout(row)
        row = QHBoxLayout()
        button('Export', self.export_memory, row)
        button('Import', self.import_memory, row)
        button('Backup', self.backup_memory, row)
        button('Restore backup', self.restore_memory, row)
        button('Purge backups', self.purge_backups, row)
        layout.addLayout(row)
        form = QFormLayout()
        self.number(form, 'Conversation retention (days)', 'retention_days', 1, 3650)
        self.number(form, 'Memory storage cap (MiB)', 'storage_limit_mb', 16, 16384, 128, lambda: setattr(self.core.memory, 'max_bytes', self.settings['storage_limit_mb']*1024*1024))
        layout.addLayout(form)
        button('Delete conversation archive', self.clear_archive, layout)
        layout.addWidget(QLabel('Forgetting clears active context and derived memories. Export files and backups remain until you delete them.'))

    def models_tab(self):
        layout = self.page('Models & performance')
        self.performance = QTextBrowser()
        layout.addWidget(self.performance)
        form = QFormLayout()
        self.number(form, 'GPU layers (0 = CPU; restart required)', 'gpu_layers', 0, 99)
        self.number(form, 'Context tokens (restart required)', 'context', 2048, 8192, 1024)
        self.number(form, 'CPU threads (restart required)', 'threads', 1, 16)
        self.number(form, 'Maximum answer tokens', 'max_tokens', 32, 600, 16)
        self.number(form, 'Temperature', 'temperature', 0.0, 1.2, 0.1)
        layout.addLayout(form)
        layout.addWidget(QLabel('Dedicated profile: one inference slot; camera inference yields to conversation.\nMetrics below are measurements, not performance guarantees. Stop unloads the language model.'))

    def diagnostics_tab(self):
        layout = self.page('Diagnostics')
        self.log_filter = QLineEdit()
        self.log_filter.setPlaceholderText('Filter event metadata by component, kind, turn ID or severity')
        layout.addWidget(self.log_filter)
        self.logs = QTextBrowser()
        layout.addWidget(self.logs, 1)
        form = QFormLayout()
        self.check(form, 'Enable bounded prompt inspector (RAM only)', 'capture_inspector', lambda: setattr(self.core,'inspector',None))
        layout.addLayout(form)
        row = QHBoxLayout()
        button('Show assembled prompt', self.inspect_prompt, row)
        button('Export redacted event metadata', self.export_diagnostics, row)
        button('Show installation log', self.show_install_log, row)
        layout.addLayout(row)
        layout.addWidget(QLabel('This journal records operational events, not hidden reasoning. Sensor buffers and prompts are excluded from diagnostic exports.'))

    def maintenance_tab(self):
        layout = self.page('Settings & maintenance')
        location = QLabel('Installation root: ' + str(self.paths.root))
        location.setWordWrap(True)
        layout.addWidget(location)
        layout.addWidget(QLabel('Runtime, models, caches, logs and memory use this root. No automatic startup entry is installed.'))
        close = QComboBox()
        close.addItem('Closing the board stops both windows and workers', 'stop')
        close.addItem('Closing the board minimizes it; explicit Quit stops everything', 'minimize')
        close.setCurrentIndex(0 if self.settings['close_behavior'] == 'stop' else 1)
        close.currentIndexChanged.connect(lambda _: self.settings.set('close_behavior', close.currentData()))
        layout.addWidget(close)
        row = QHBoxLayout()
        button('Read installation / repair guide', lambda: self.show_doc('INSTALL.md'), row)
        button('Read verification status', lambda: self.show_doc('VERIFICATION.md'), row)
        button('Read privacy / architecture', lambda: self.show_doc('ARCHITECTURE.md'), row)
        layout.addLayout(row)
        button('Run local health report', self.health_report, layout)
        button('Stop and quit both applications', lambda: self.quit_app(force=True), layout)
        layout.addWidget(QLabel('Repair / update: rerun scripts/setup.ps1 from this release.\nRelocation: move the whole folder, then rerun setup with the new root.\nUninstall: use scripts/uninstall.ps1; it offers preservation of memory and settings.\nNo self-updater or credential account is required.'))
        layout.addStretch()

    def show_board(self):
        self.showNormal()
        self.raise_()
        self.activateWindow()

    def guard(self, action):
        try:
            return action()
        except Exception as exc:
            QMessageBox.warning(self, 'Yumi', str(exc))

    def notice(self, text):
        self.banner.setText(text)

    def on_event(self, event):
        kind, payload = event.kind, event.payload
        if kind == 'accepted':
            self.stream_turn, self.stream_text = event.turn_id, ''
            self.chat_records.append(('Context' if payload.get('source')=='proactive' else 'You',payload['text']))
            self.render_chat()
        elif kind == 'delta' and event.turn_id == self.stream_turn and event.turn_id == self.core.turn:
            self.stream_text += payload['text']
            self.render_chat()
        elif kind == 'answer':
            if event.turn_id == self.core.turn:
                self.chat_records.append(('Yumi', payload['text']))
                self.stream_text = ''
                self.render_chat()
        elif kind in ('error', 'device_error'):
            message = payload.get('component', 'Yumi') + ': ' + payload.get('message', '')
            self.errors = (self.errors + [message])[-20:]
            self.notice(message)
        elif kind == 'notice': self.notice(payload.get('message', ''))
        elif kind == 'attention':
            self.attention_view.setText(f"Addressability: {payload['confidence']:.0%} · {'accepted' if payload['accepted'] else 'ignored'}\n{json.dumps(payload['signals'])}")
        elif kind == 'retrieval': self.memory_used.setText('Memory references: ' + (', '.join(payload['memory_ids']) or 'none'))
        elif kind == 'memory_changed': self.refresh_memory()

    def render_chat(self):
        rows = self.chat_records[-80:]
        if self.stream_text: rows = rows + [('Yumi · responding', self.stream_text)]
        self.chat.setHtml(''.join('<p style="color:#8bdef1"><b>' + html.escape(role) + '</b></p><p>' + html.escape(text).replace('\n', '<br>') + '</p>' for role, text in rows))
        self.chat.verticalScrollBar().setValue(self.chat.verticalScrollBar().maximum())

    def send(self):
        text = self.input.text()
        self.input.clear()
        self.core.submit(text)

    def replay(self):
        if not self.core.audio.active.is_set():
            self.notice('Start Yumi and select a working output device first.')
            return
        self.core.audio.new_turn()
        self.core.audio.say(self.core.last_answer)

    def speech_test(self):
        if not self.core.audio.active.is_set():
            self.notice('Start Yumi first; voice assets and an output device must be ready.')
            return
        self.core.audio.new_turn()
        self.core.audio.say("Hey Jason. I'm Yumi. Let's make this thing actually useful.")

    def correct_transcript(self):
        text, ok = QInputDialog.getText(self, 'Correct transcript', 'Corrected text:', text=self.core.last_input[0])
        if ok and text: self.core.submit(text)

    def clear_context(self):
        self.core.cancel()
        self.core.history.clear()
        self.core.inspector = None
        self.notice('Working context cleared; durable memories are unchanged.')

    def search_history(self):
        rows = self.core.memory.history(self.history_query.text())
        self.history_results.setPlainText('\n\n'.join(f"{r['session'][:8]} · {r['role']}: {r['content']}" for r in reversed(rows)))

    def reset_persona(self):
        self.settings.set_persona(PERSONA)
        self.persona.setPlainText(PERSONA)

    def revert_persona(self):
        history = self.settings.history()
        if history:
            self.settings.set_persona(history[-1]['text'])
            self.persona.setPlainText(self.settings['persona'])

    def steer(self, scope):
        text = self.directive.text().strip()
        if not text: return
        if scope == 'turn': self.core.one_turn = text
        elif scope == 'session': self.settings.data['session_directive'] = text
        else: self.core.memory.add(text, 'preference', 'explicit user steering', pinned=True)
        self.notice('Steering saved for ' + scope + '. Applies to subsequent turns.')

    def reset_affect(self):
        for key in self.core.affect: self.core.affect[key] = 0.55

    def roaming_monitor(self):
        rect = self.avatar.screen().availableGeometry()
        self.settings.set('roaming_rect', [rect.x(), rect.y(), rect.width(), rect.height()])
        self.notice('Roaming constrained to this monitor’s available desktop area.')

    def edit_json_setting(self, key):
        text, ok = QInputDialog.getMultiLineText(self, 'Edit ' + key, 'JSON value (rectangles: [x,y,width,height]):', json.dumps(self.settings[key], indent=2))
        if not ok: return
        def save():
            value = json.loads(text)
            if key == 'pronunciation' and (not isinstance(value, dict) or not all(isinstance(k, str) and isinstance(v, str) for k,v in value.items())):
                raise ValueError('Use a JSON object mapping words to pronunciations')
            if key == 'screen_exclusions' and (not isinstance(value, list) or not all(isinstance(v,str) for v in value)):
                raise ValueError('Use a list of process names, for example ["chrome.exe"]')
            if key in ('roaming_rect', 'excluded_rects'):
                rects = [value] if key == 'roaming_rect' else value
                if not isinstance(rects, list) or not all(isinstance(r,list) and len(r)==4 and all(isinstance(n,(int,float)) for n in r) and r[2]>0 and r[3]>0 for r in rects):
                    raise ValueError('Use valid [x,y,width,height] rectangles')
            self.settings.set(key, value)
        self.guard(save)

    def set_device(self, key, combo):
        if combo.currentIndex() >= 0:
            self.settings.set(key, combo.currentData())
            self.notice('Device selection saved. Restart workers to apply it.')

    def refresh_devices(self):
        try:
            devices = self.core.audio.devices()
            for combo, key, channel in [(self.input_device,'input_device','max_input_channels'), (self.output_device,'output_device','max_output_channels')]:
                combo.blockSignals(True)
                combo.clear()
                combo.addItem('System default', None)
                for device in devices:
                    if device[channel] > 0: combo.addItem(device['name'], device['index'])
                index = combo.findData(self.settings[key])
                combo.setCurrentIndex(max(0,index))
                combo.blockSignals(False)
        except Exception as exc:
            self.notice('Audio devices unavailable: ' + str(exc))

    def select_voice(self):
        directory = QFileDialog.getExistingDirectory(self, 'Choose voice folder inside installation root', str(self.paths.models))
        if directory:
            def save():
                relative = Path(directory).resolve().relative_to(self.paths.root)
                self.settings.set('voice_folder', str(relative))
                self.notice('Voice selection saved. Restart workers to load it.')
            self.guard(save)

    def clear_observations(self):
        self.core.latest_observations.clear()
        self.core.perception.latest.clear()
        if not self.settings['camera_enabled']: self.core.perception.preview = None

    def refresh_memory(self):
        self.memories.clear()
        for row in self.core.memory.retrieve(self.memory_query.text(), limit=100, chars=100000):
            item = QListWidgetItem(('★ ' if row['pinned'] else '') + row['kind'] + ' · ' + row['content'][:140])
            item.setData(Qt.UserRole, row['id'])
            self.memories.addItem(item)

    def selected_memory(self):
        item = self.memories.currentItem()
        return self.core.memory.get(item.data(Qt.UserRole)) if item else None

    def show_memory(self, *_):
        self.memory_detail.setPlainText(json.dumps(self.selected_memory(), indent=2, ensure_ascii=False))

    def add_memory(self):
        text, ok = QInputDialog.getMultiLineText(self, 'Explicit fact', 'What should Yumi remember?')
        if ok and text:
            self.guard(lambda: self.core.memory.add(text))
            self.refresh_memory()

    def edit_memory(self):
        row = self.selected_memory()
        if not row: return
        text, ok = QInputDialog.getMultiLineText(self, 'Correct fact', 'Replacement:', row['content'])
        if ok and text:
            self.core.cancel()
            self.core.history.clear()
            self.guard(lambda: self.core.memory.edit(row['id'], text))
            self.refresh_memory()

    def pin_memory(self):
        row = self.selected_memory()
        if row:
            self.core.memory.pin(row['id'], not row['pinned'])
            self.refresh_memory()

    def forget_memory(self):
        row = self.selected_memory()
        if row:
            self.core.forget(row['id'])
            self.refresh_memory()

    def export_memory(self):
        path = self.paths.data / ('memory-export-' + time.strftime('%Y%m%d-%H%M%S') + '.json')
        self.guard(lambda: self.core.memory.export(path))
        self.notice('Private memory export: ' + str(path))

    def import_memory(self):
        path, _ = QFileDialog.getOpenFileName(self, 'Import memory', str(self.paths.data), 'JSON (*.json)')
        if path:
            self.guard(lambda: self.core.memory.import_file(path))
            self.refresh_memory()

    def backup_memory(self):
        path = self.paths.backups / ('memory-' + time.strftime('%Y%m%d-%H%M%S') + '.sqlite3')
        self.guard(lambda: self.core.memory.backup(path))
        self.notice('Backup created: ' + str(path))

    def restore_memory(self):
        path, _ = QFileDialog.getOpenFileName(self, 'Restore backup', str(self.paths.backups), 'SQLite (*.sqlite3)')
        if path:
            self.core.pause()
            self.core.history.clear()
            self.guard(lambda: self.core.memory.restore(path))
            self.refresh_memory()

    def purge_backups(self):
        if QMessageBox.question(self, 'Purge backups', 'Delete all Yumi memory-*.sqlite3 backups in this installation?') == QMessageBox.Yes:
            for path in self.paths.backups.glob('memory-*.sqlite3'): path.unlink()
            self.notice('Memory backups purged. Exported JSON files are separate; delete them if no longer needed.')

    def clear_archive(self):
        if QMessageBox.question(self, 'Delete archive', 'Delete the engaged conversation archive? Curated facts remain.') == QMessageBox.Yes:
            self.core.memory.clear_history()
            self.clear_context()
            self.search_history()

    def inspect_prompt(self):
        self.logs.setPlainText(json.dumps(self.core.inspector, indent=2, ensure_ascii=False))
        self.log_filter.setText('__inspector__')

    def export_diagnostics(self):
        path = self.paths.logs / 'diagnostics-export.json'
        atomic_json(path, {'version': 1, 'events': list(self.core.journal.recent), 'metrics': self.core.metrics})
        self.notice('Metadata-only diagnostic export: ' + str(path))

    def show_install_log(self):
        path = self.paths.logs / 'install.log'
        self.log_filter.setText('__inspector__')
        self.logs.setPlainText(path.read_text(encoding='utf-8', errors='replace')[-60000:] if path.exists() else 'No installation log available.')

    def show_doc(self, name):
        text = (self.paths.root / 'docs' / name).read_text(encoding='utf-8')
        dialog = QMessageBox(self)
        dialog.setWindowTitle(name)
        dialog.setText(text[:3000])
        dialog.setDetailedText(text)
        dialog.exec()

    def health_report(self):
        from .smoke import report
        result = report(self.paths.root)
        path = self.paths.logs / 'health-report.json'
        atomic_json(path, result)
        self.notice('Health report: ' + str(path) + ' · ' + result['status'])

    def refresh(self):
        core = self.core
        self.status.setText(core.state.upper() + (' · ACTIVE' if core.active else ' · sensors off'))
        self.health.setPlainText('\n'.join([
            'MODEL: ' + ('ready' if core.model.ready else 'not loaded / unavailable'),
            'VOICE MODEL: ' + ('loaded' if core.audio.tts else 'not loaded'),
            'RECOGNITION: ' + ('loaded' if core.audio.stt else 'not loaded'),
            'MICROPHONE: ' + ('capturing' if core.audio.active.is_set() and core.audio.input_stream else 'off'),
            'CAMERA: ' + ('open' if core.perception.camera else 'off / unavailable'),
            'CONVERSATION WORKER: ' + ('busy' if core.busy else 'idle'),
            'QUEUED TURNS: ' + str(core.queue.qsize()),
            'FUTURE ACTION ADAPTERS: ' + str(len(core.registry.entries)),
            '\nRecent problems:', *self.errors[-6:]]))
        self.state_view.setText('Current session: ' + core.session[:12] + '\nAttention calibration bias: ' + f'{core.attention.bias:+.3f}' + '\nCurrent task: ' + core.active_task['status'] + ' · ' + core.active_task.get('request','')[:180])
        self.affect_label.setText('Simulated affect · ' + ' · '.join(f'{k}: {v:.2f}' for k,v in core.affect.items()))
        self.meters.setText(f'Microphone RMS: {core.audio.input_level:.3f} · Playback RMS: {core.audio.output_level:.3f} · Speech queue: {core.audio.speech.qsize()}')
        frame = core.perception.preview
        if frame is not None and core.active:
            image = QImage(frame.data, frame.shape[1], frame.shape[0], frame.strides[0], QImage.Format_RGB888).copy()
            self.camera_preview.setPixmap(QPixmap.fromImage(image).scaled(400, 240, Qt.KeepAspectRatio, Qt.SmoothTransformation))
        else:
            self.camera_preview.setText('No active camera preview')
        self.observations.setPlainText('\n\n'.join(f"{key} · {time.time()-value['timestamp']:.0f}s old {'(STALE)' if time.time()-value['timestamp']>60 else ''}\n{value['text']}\n{value['confidence']}" for key,value in core.latest_observations.copy().items()))
        metrics = dict(core.metrics, **core.audio.stats, **core.resources.current)
        metrics.update(avatar_frame_interval_ms=round(self.avatar.frame_ms,1),
                       memory_disk_mb=round(core.memory.storage_bytes()/1048576,3),
                       semantic_encoder=core.memory.encoder.version if core.memory.encoder else 'not loaded',
                       context_tokens=self.settings['context'], gpu_layers=self.settings['gpu_layers'],
                       queued_turns=core.queue.qsize(), queued_speech=core.audio.speech.qsize())
        self.performance.setPlainText(json.dumps(metrics, indent=2))
        query = self.log_filter.text()
        if query != '__inspector__':
            self.logs.setPlainText('\n'.join(json.dumps(row) for row in list(core.journal.recent)[-100:] if query.lower() in json.dumps(row).lower()))

    def quit_app(self, force=False):
        if self.closing: return
        if not force and self.settings['close_behavior'] == 'minimize':
            self.showMinimized()
            return
        self.closing = True
        self.timer.stop()
        self.settings.set('board_geometry', bytes(self.saveGeometry().toHex()).decode())
        self.avatar.timer.stop()
        self.avatar.hide()
        self.core.close()
        QApplication.instance().quit()

    def closeEvent(self, event):
        event.ignore()
        self.quit_app()
