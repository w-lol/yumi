"""Bounded background measurements. No UI-thread subprocesses or busy polling."""
import os
import subprocess
import threading
import time
import psutil


class Resources:
    def __init__(self):
        self.stop_event=threading.Event()
        self.current={}
        self.thread=threading.Thread(target=self._run,daemon=True)
        self.thread.start()

    def _run(self):
        process=psutil.Process()
        process.cpu_percent()
        tick=0
        gpu={}
        while not self.stop_event.is_set():
            try:
                rss=process.memory_info().rss
                children=process.children(recursive=True)
                for child in children:
                    try:rss+=child.memory_info().rss
                    except psutil.Error:pass
                if tick%5==0:
                    try:
                        flags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0
                        result=subprocess.run(['nvidia-smi','--query-gpu=name,memory.used,memory.total,utilization.gpu',
                                               '--format=csv,noheader,nounits'],capture_output=True,text=True,timeout=2,creationflags=flags)
                        if result.returncode==0:
                            fields=[part.strip() for part in result.stdout.splitlines()[0].split(',')]
                            gpu={'gpu_name':fields[0],'vram_used_mb':float(fields[1]),'vram_total_mb':float(fields[2]),'gpu_percent':float(fields[3])}
                        else:gpu={'gpu_status':'nvidia-smi unavailable'}
                    except (OSError,ValueError,IndexError,subprocess.TimeoutExpired):gpu={'gpu_status':'not available'}
                self.current={'process_tree_ram_mb':round(rss/1048576,1),'process_cpu_percent':process.cpu_percent(),
                              'system_cpu_percent':psutil.cpu_percent(),'available_ram_mb':round(psutil.virtual_memory().available/1048576),
                              'worker_processes':len(children),'measured_at':time.time(),**gpu}
            except psutil.Error:
                pass
            tick+=1
            self.stop_event.wait(1)

    def close(self):
        self.stop_event.set()
        self.thread.join(timeout=3)
