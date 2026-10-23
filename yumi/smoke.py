"""Non-recording target preflight and automatic functional smoke test."""
from __future__ import annotations
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from .paths import Paths, atomic_json
from .settings import Settings
from .memory import Memory


def report(root):
    import psutil
    root = Path(root).resolve()
    result = {'schema':1, 'platform':platform.platform(), 'python':platform.python_version(),
              'root':str(root), 'cpu':platform.processor(), 'cpu_threads':psutil.cpu_count(),
              'ram_total_gib':round(psutil.virtual_memory().total/1024**3,2),
              'ram_available_gib':round(psutil.virtual_memory().available/1024**3,2),
              'disk_free_gib':round(shutil.disk_usage(root).free/1024**3,2), 'checks':{}}
    for name in ('PySide6','sounddevice','sherpa_onnx','faster_whisper','cv2','mss','httpx'):
        try:
            module=__import__(name)
            result['checks'][name]={'status':'passed','version':getattr(module,'__version__','unknown')}
        except Exception as exc:
            result['checks'][name]={'status':'failed','error':str(exc)}
    try:
        from .audio import Audio
        devices=Audio.devices()
        result['devices']=[{'name':d['name'],'input_channels':d['max_input_channels'],'output_channels':d['max_output_channels']} for d in devices]
    except Exception as exc: result['devices_error']=str(exc)
    try:
        flags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0
        p=subprocess.run(['nvidia-smi','--query-gpu=name,driver_version,memory.total,memory.free','--format=csv,noheader'],
                         capture_output=True,text=True,timeout=10,creationflags=flags)
        result['gpu']=p.stdout.strip() if p.returncode==0 else 'nvidia-smi failed'
    except (OSError,subprocess.TimeoutExpired): result['gpu']='not available'
    for name,pattern in [('chat','models/chat/*.gguf'),('voice','models/voice/**/*.onnx'),('whisper','models/whisper/model.bin')]:
        result['checks'][name]={'status':'passed' if list(root.glob(pattern)) else 'missing'}
    result['status']='passed' if all(c['status']=='passed' for c in result['checks'].values()) else 'incomplete'
    result['windows_hardware_validation']='required on target' if os.name!='nt' else 'preflight only; run functional smoke and interactive acceptance'
    return result


def functional(root):
    paths=Paths(root)
    settings=Settings(paths)
    result=report(root)
    from .inference import LocalModel
    model=LocalModel(paths,settings,lambda *_:None)
    try:
        model.start(threading.Event())
        start=time.perf_counter()
        answer=''.join(model.stream([{'role':'user','content':'Reply with the word ready.'}],threading.Event(),max_tokens=16))
        result['model_smoke']={'status':'passed' if 'ready' in answer.lower() else 'failed','output':answer,'seconds':time.perf_counter()-start}
    except Exception as exc:result['model_smoke']={'status':'failed','error':str(exc)}
    finally:model.stop()
    try:
        from .audio import Audio
        audio=Audio(paths,settings,lambda *_:None,lambda _:None)
        audio.load()
        start=time.perf_counter()
        generated=audio.tts.generate('Yumi is ready for a local test.',sid=audio.speaker_id,speed=1.0)
        import numpy as np
        assert len(generated.samples)>1000 and np.isfinite(generated.samples).all()
        # RAM-only loopback through recognition; this never creates a waveform file.
        samples=np.asarray(generated.samples,dtype=np.float32)
        resampled=np.interp(np.arange(0,len(samples),generated.sample_rate/16000),np.arange(len(samples)),samples).astype(np.float32)
        segments,_=audio.stt.transcribe(resampled,language='en',beam_size=1)
        transcript=' '.join(s.text for s in segments)
        result['voice_smoke']={'status':'passed' if 'ready' in transcript.lower() else 'failed',
                               'transcript':transcript,'seconds':time.perf_counter()-start}
        samples.fill(0);resampled.fill(0)
        del generated
    except Exception as exc:result['voice_smoke']={'status':'failed','error':str(exc)}
    result['status']='passed' if result['status']=='passed' and result['model_smoke']['status']=='passed' and result['voice_smoke']['status']=='passed' else 'incomplete'
    return result


def main(root):
    paths=Paths(root)
    result=functional(root)
    atomic_json(paths.logs/'target-smoke.json',result)
    print(json.dumps(result,indent=2))
    return 0 if result['status']=='passed' else 1
