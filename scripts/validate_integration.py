"""Real local inference/voice/semantic checks; no audio or frame files are written."""
from __future__ import annotations
import base64
import json
from pathlib import Path
import sys
import tempfile
import threading
import time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from yumi.paths import Paths,atomic_json
from yumi.settings import Settings
from yumi.inference import LocalModel
from yumi.audio import Audio
from yumi.memory import Memory
from yumi.semantic import Encoder


def run(root):
    import numpy as np
    import cv2
    import httpx
    paths=Paths(root);paths.activate();settings=Settings(paths)
    result={'schema':1,'platform':sys.platform,'checks':{},'metrics':{}}
    model=LocalModel(paths,settings,lambda *_:None)
    started=time.perf_counter()
    try:
        model.start(threading.Event())
        result['metrics']['model_cold_start_seconds']=time.perf_counter()-started
        response=httpx.post(model.url+'/v1/chat/completions',json={'messages':[]},trust_env=False,timeout=5)
        assert response.status_code==401,(response.status_code,response.text)
        result['checks']['unauthenticated_control_rejected']='passed'
        answers=[];times=[]
        for index in range(3):
            begin=time.perf_counter();first=None
            output=''
            for chunk in model.stream([{'role':'system','content':'The user prefers mango. Answer in one short sentence. No reasoning.'},
                                      {'role':'user','content':'Which fruit do I prefer?'}],threading.Event(),max_tokens=40):
                if first is None:first=time.perf_counter()-begin
                output+=chunk
            assert 'mango' in output.lower(),output
            answers.append(output);times.append({'first_token_seconds':first,'total_seconds':time.perf_counter()-begin})
        result['checks']['grounded_text']='passed';result['metrics']['text_samples']=times
        cancel=threading.Event()
        generator=model.stream([{'role':'user','content':'Explain the history of computing in great detail.'}],cancel,max_tokens=300)
        first=next(generator)
        begin=time.perf_counter();cancel.set();model.cancel();generator.close()
        result['metrics']['cancel_dispatch_seconds']=time.perf_counter()-begin
        recovery=''.join(model.stream([{'role':'user','content':'Say ready.'}],threading.Event(),max_tokens=12))
        assert 'ready' in recovery.lower(),recovery
        result['checks']['generation_cancel_and_recovery']='passed'
        image=np.zeros((224,224,3),dtype=np.uint8);image[:]=(0,0,255)
        ok,jpeg=cv2.imencode('.jpg',image);assert ok
        content=[{'type':'text','text':'What single color fills this image? Answer with the color only.'},
                 {'type':'image_url','image_url':{'url':'data:image/jpeg;base64,'+base64.b64encode(jpeg).decode()}}]
        begin=time.perf_counter()
        answer=''.join(model.stream([{'role':'user','content':content}],threading.Event(),max_tokens=16))
        result['metrics']['vision_seconds']=time.perf_counter()-begin
        assert 'red' in answer.lower(),answer
        result['checks']['real_vision']='passed'
    finally:
        process=model.process
        model.stop()
        assert process is None or process.poll() is not None
        assert not model.key_file.exists()
        result['checks']['server_cleanup']='passed'
    audio=Audio(paths,settings,lambda *_:None,lambda _:None)
    audio.load()
    first_chunks=[];samples=[]
    for _ in range(3):
        begin=time.perf_counter();chunks=[]
        def callback(chunk,progress):
            if not chunks:first_chunks.append(time.perf_counter()-begin)
            chunks.append(len(chunk));return 1
        generated=audio.tts.generate('Yumi is ready for a local conversation.',sid=audio.speaker_id,speed=1.08,callback=callback)
        waveform=np.asarray(generated.samples,dtype=np.float32)
        assert len(waveform)>1000 and np.isfinite(waveform).all()
        resampled=np.interp(np.arange(0,len(waveform),generated.sample_rate/16000),np.arange(len(waveform)),waveform).astype(np.float32)
        segments,_=audio.stt.transcribe(resampled,language='en',beam_size=1)
        text=' '.join(s.text for s in segments)
        assert 'ready' in text.lower() and 'conversation' in text.lower(),text
        samples.append({'transcript':text,'synthesis_plus_recognition_seconds':time.perf_counter()-begin})
        waveform.fill(0);resampled.fill(0);del generated
    result['checks']['in_memory_tts_stt']='passed'
    result['voice']={'backend':audio.tts_backend,'name':settings['kokoro_voice'],'sample_rate':audio.rate}
    result['metrics']['tts_first_chunk_seconds']=first_chunks
    result['metrics']['speech_samples']=samples
    encoder=Encoder(paths.models/'embedding')
    with tempfile.TemporaryDirectory(prefix='semantic acceptance ',dir=paths.tmp) as tmp:
        memory=Memory(Path(tmp)/'memory.sqlite3');memory.encoder=encoder
        identifier=memory.add('Jason enjoys riding his bicycle through the forest','preference')
        memory.index_batch()
        assert any(r['id']==identifier for r in memory.retrieve('cycling outdoors'))
        backup=Path(tmp)/'backup.sqlite3';memory.backup(backup)
        memory.forget(identifier);memory.restore(backup)
        assert not memory.retrieve('cycling outdoors')
        assert memory.db.execute('SELECT COUNT(*) FROM vectors').fetchone()[0]==0
        memory.close()
    result['checks']['semantic_recall_delete_restore']='passed'
    result['status']='passed'
    atomic_json(paths.logs/'integration.json',result)
    print(json.dumps(result,indent=2))
    return result


if __name__=='__main__':run(Path(__file__).resolve().parents[1])
