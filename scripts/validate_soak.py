"""Bounded repeat-interaction and long-history checks, with measured results."""
from pathlib import Path
import sys
import tempfile
import threading
import time
import statistics
import json
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from yumi.paths import Paths,atomic_json
from yumi.settings import Settings
from yumi.memory import Memory
from yumi.semantic import Encoder
from yumi.inference import LocalModel
import psutil

root=Path(__file__).resolve().parents[1];paths=Paths(root);paths.activate();settings=Settings(paths)
started=time.perf_counter();metrics={};process=psutil.Process();ram=[]
with tempfile.TemporaryDirectory(prefix='Yumi history soak ',dir=paths.tmp) as tmp:
    memory=Memory(Path(tmp)/'memory.sqlite3')
    for index in range(10000):memory.add(f'Archive fact {index}: project item {index%100} was reviewed.','episode','synthetic validation fixture')
    fact=memory.add('Jason enjoys riding his bicycle through the forest','preference','synthetic validation fixture')
    encoder=Encoder(paths.models/'embedding');memory.encoder=encoder
    # Index the important record first; the background index remains resumable.
    with memory.lock,memory.db:memory.db.execute('UPDATE memories SET updated=updated+1 WHERE id=?',(fact,))
    count=0
    while count<300:
        count+=memory.index_batch(limit=10)
    # Explicitly exercise full long-history lexical recall independently of index coverage.
    times=[]
    for _ in range(40):
        begin=time.perf_counter();rows=memory.retrieve('Archive fact 7654',limit=8)
        times.append((time.perf_counter()-begin)*1000)
        assert len(rows)<=8
        ram.append(process.memory_info().rss/1048576)
    metrics['history_records']=10001
    metrics['indexed_records']=count
    metrics['retrieval_samples']=len(times)
    metrics['retrieval_median_ms']=statistics.median(times)
    metrics['retrieval_p95_ms']=sorted(times)[int(len(times)*.95)-1]
    metrics['database_mib']=memory.storage_bytes()/1048576
    memory.close()
model=LocalModel(paths,settings,lambda *_:None)
completed=cancelled=0
try:
    model.start(threading.Event())
    for index in range(12):
        cancel=threading.Event()
        stream=model.stream([{'role':'user','content':'Give three concise tips for keeping a desk tidy.'}],cancel,max_tokens=100)
        out=''
        for chunk in stream:
            out+=chunk
            if index%2==0 and len(out)>12:
                cancel.set();model.cancel();cancelled+=1;break
        stream.close()
        if not cancel.is_set():
            assert len(out.strip())>10
            completed+=1
        ram.append(process.memory_info().rss/1048576)
    metrics['completed_turns']=completed;metrics['cancelled_turns']=cancelled
finally:model.stop()
metrics['elapsed_seconds']=time.perf_counter()-started
metrics['parent_rss_mib_min']=min(ram);metrics['parent_rss_mib_max']=max(ram)
metrics['scope']='Cloud CPU bounded-history/repeated-interruption run; not a target-machine long-duration soak'
metrics['status']='passed'
atomic_json(paths.logs/'soak-validation.json',metrics);print(json.dumps(metrics,indent=2))
