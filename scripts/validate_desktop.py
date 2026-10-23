"""Headless Qt integration with the real local language model and missing-device fallback."""
import os
os.environ.setdefault('QT_QPA_PLATFORM','offscreen')
from pathlib import Path
import sys
import tempfile
import time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PySide6.QtWidgets import QApplication
from yumi.paths import Paths,atomic_json
from yumi.settings import Settings
from yumi.inference import LocalModel
from yumi.ui import Board

root=Path(__file__).resolve().parents[1]
app=QApplication([])
results={}

def until(predicate,seconds=40):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        app.processEvents()
        if predicate():return
        time.sleep(0.02)
    raise AssertionError('Timed out waiting for desktop state')

with tempfile.TemporaryDirectory(prefix='Yumi desktop spaces ü ',dir=root/'tmp') as tmp:
    paths=Paths(tmp);paths.activate();settings=Settings(paths)
    settings.data.update(gpu_layers=0,mic_enabled=False,camera_enabled=False,screen_enabled=False,voice=False)
    board=Board(paths,settings)
    board.core.model=LocalModel(Paths(root),settings,board.core.event)
    board.show()
    try:
        until(lambda:board.isVisible() and board.avatar.isVisible())
        visible=[w.windowTitle() for w in app.topLevelWidgets() if w.isVisible()]
        assert sorted(visible)==['Yumi Avatar','Yumi Control Board'],visible
        results['two_visible_windows']='passed'
        board.core.start()
        until(lambda:board.core.active and not board.core.loading)
        assert board.core.model.ready
        assert board.core.audio.tts is None  # This fixture deliberately lacks audio assets.
        results['text_survives_voice_failure']='passed'
        board.core.submit('remember: My preferred snack is mango.')
        board.core.submit('What is my preferred snack? Reply in one short sentence.')
        until(lambda:bool(board.core.last_answer))
        assert 'mango' in board.core.last_answer.lower(),board.core.last_answer
        assert 'mango' in board.chat.toPlainText().lower()
        results['typed_chat_and_memory_in_ui']='passed'
        previous=board.core.model.process.pid
        board.core.model.process.terminate()
        until(lambda:board.core.model.ready and board.core.model.process and board.core.model.process.pid!=previous and not board.core.loading,60)
        results['worker_crash_recovery']='passed'
        board.core.submit('Write a detailed story about a journey through space.')
        until(lambda:board.core.busy)
        before=time.perf_counter();board.core.stop()
        elapsed=time.perf_counter()-before
        assert elapsed<0.5,elapsed
        assert not board.core.active and not board.core.perception.active.is_set()
        until(lambda:board.core.model.process is None or board.core.model.process.poll() is not None)
        results['emergency_stop_during_generation']='passed'
        results['stop_dispatch_ms']=elapsed*1000
        board.grab().save(str(root/'docs/control-board.png'))
        board.avatar.grab().save(str(root/'docs/avatar.png'))
    finally:
        board.quit_app(force=True)
    assert not board.core.worker.is_alive()
    results['shutdown_without_model_or_conversation_worker']='passed'
atomic_json(root/'logs/desktop-validation.json',results)
print(results)
