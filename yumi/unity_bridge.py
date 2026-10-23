"""Authenticated localhost state transport; Unity owns rendering and movement."""
from __future__ import annotations
import asyncio
from collections import deque
import json
import os
import secrets
import threading
import time
import uuid
from .paths import atomic_json

ACTION_TYPES={'move_to_window','jump_to_platform','walk_to_position','run_to_position',
              'reset_avatar','sit','stand','wave','look_at','stop_movement','set_expression','stay','follow_cursor','other_monitor'}


def action_intent(text):
    import re
    text=re.sub(r'^\s*(?:hey\s+)?yumi[, !]*','',text.strip(),flags=re.I)
    text=re.sub(r'^please\s+','',text,flags=re.I).strip(' .!?')
    exact={'stop moving':'stop_movement','stay on this window':'stay','stand up':'stand',
           'wave':'wave','wave at me':'wave','follow my mouse':'follow_cursor',
           'follow the cursor':'follow_cursor','jump to the other monitor':'other_monitor',
           'come over here':'walk_to_position','jump over there':'jump_to_platform',
           'run across the screen':'run_to_position','sit down':'sit'}
    if text.casefold() in exact:return {'type':exact[text.casefold()],'target':'cursor'}
    match=re.fullmatch(r'(?:go (?:over )?to|move to|jump to|go sit on|sit on) (.{1,100})',text,re.I)
    if match:return {'type':'sit' if 'sit' in text.casefold().split()[:2] else 'move_to_window','target':match[1]}
    return None


def validate_action(action):
    if not isinstance(action,dict) or action.get('type') not in ACTION_TYPES:raise ValueError('Unsupported avatar action')
    if set(action)-{'type','target','expression'}:raise ValueError('Only high-level actions are accepted; coordinates and OS commands are excluded')
    if any(not isinstance(value,str) or len(value)>160 for value in action.values()):raise ValueError('Invalid action value')
    return dict(action)


class UnityBridge:
    def __init__(self, paths, snapshot, event):
        self.paths,self.snapshot,self.event=paths,snapshot,event
        self.session=uuid.uuid4().hex;self.token=secrets.token_urlsafe(32)
        self.descriptor=paths.tmp/'avatar-bridge.json'
        self.thread=None;self.closed=threading.Event();self.ready=threading.Event()
        self.connected=False;self.port=None;self.error='';self.status={}
        self.actions=deque(maxlen=32);self.lock=threading.Lock();self.sequence=0

    def start(self):
        if self.thread and self.thread.is_alive():return
        self.closed.clear();self.ready.clear();self.error=''
        self.token=secrets.token_urlsafe(32)
        self.thread=threading.Thread(target=self._run,daemon=True);self.thread.start()

    def _run(self):
        try:asyncio.run(self._serve())
        except Exception as exc:
            self.error=type(exc).__name__+': '+str(exc)
            self.event('error',{'component':'Unity bridge','message':self.error})
        finally:
            self.connected=False;self.ready.clear()
            try:
                if self.descriptor.exists() and json.loads(self.descriptor.read_text())['session']==self.session:self.descriptor.unlink()
            except (OSError,ValueError,KeyError):pass

    async def _serve(self):
        from websockets.asyncio.server import serve
        async with serve(self._client,'127.0.0.1',0,origins=[None],max_size=16384,
                         max_queue=4,close_timeout=.5,ping_interval=10,ping_timeout=10) as server:
            self.port=server.sockets[0].getsockname()[1]
            atomic_json(self.descriptor,{'protocol':1,'session':self.session,'url':f'ws://127.0.0.1:{self.port}/avatar/v1','token':self.token})
            if os.name!='nt':self.descriptor.chmod(0o600)
            self.ready.set()
            while not self.closed.is_set():await asyncio.sleep(.05)

    async def _client(self, socket):
        if (socket.request.path!='/avatar/v1' or
            not secrets.compare_digest(socket.request.headers.get('Authorization',''),'Bearer '+self.token)):
            await socket.close(1008,'Unauthorized');return
        if self.connected:await socket.close(1008,'An avatar is already connected');return
        self.connected=True;self.status={}
        receive=asyncio.create_task(self._receive(socket))
        try:
            while not self.closed.is_set() and not receive.done():
                self.sequence+=1
                frame={'protocol':1,'type':'state','session':self.session,'sequence':self.sequence,
                       'time_ms':int(time.time()*1000),'payload':self.snapshot()}
                await socket.send(json.dumps(frame))
                with self.lock:
                    queued=list(self.actions);self.actions.clear()
                for action in queued:await socket.send(json.dumps(action))
                await asyncio.sleep(.05)
        except Exception as exc:
            if type(exc).__name__ not in ('ConnectionClosedOK','ConnectionClosedError'):
                self.error=type(exc).__name__
        finally:
            receive.cancel()
            try:await receive
            except (asyncio.CancelledError,Exception):pass
            self.connected=False
            with self.lock:self.actions.clear()

    async def _receive(self,socket):
        async for raw in socket:
            try:data=json.loads(raw)
            except (ValueError,TypeError):await socket.close(1008,'Invalid JSON');return
            if not isinstance(data,dict) or data.get('protocol')!=1 or data.get('type') not in ('hello','status','action_result'):
                await socket.close(1008,'Unsupported protocol message');return
            payload=data.get('payload',{})
            if not isinstance(payload,dict):await socket.close(1008,'Invalid payload');return
            # Inbound state is diagnostic only; it cannot operate the backend.
            self.status.update({k:(v[:2000] if isinstance(v,str) else v) for k,v in payload.items()
                                if k in ('ready','renderer','message','action_id','success') and isinstance(v,(str,bool,int,float))})
            if data['type']=='action_result':self.event('unity_action_result',self.status)

    def action(self,value):
        action=validate_action(value)
        if not self.connected:raise RuntimeError('Unity avatar is not connected; no movement was performed')
        if self.status.get('ready') is not True:raise RuntimeError('Unity avatar has not reported ready; no movement was performed')
        record={'protocol':1,'type':'action','id':uuid.uuid4().hex,'payload':action}
        with self.lock:
            if len(self.actions)==self.actions.maxlen:raise RuntimeError('Avatar action queue is full')
            self.actions.append(record)
        return record['id']

    def close(self):
        self.closed.set()
        if self.thread:self.thread.join(timeout=2)
