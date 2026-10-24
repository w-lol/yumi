import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch
from yumi.companion_tools import adjust,resolve_window,schemas
from yumi.continuity import import_handoff
from yumi.memory import Memory
from yumi.paths import Paths
from yumi.settings import Settings
from yumi.web import public_url,query_text,ReadOnlyWeb


class CompanionTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.paths=Paths(self.tmp.name)
        self.settings=Settings(self.paths)
    def tearDown(self):self.tmp.cleanup()

    def test_persisted_bounded_controls_and_critical_settings_rejected(self):
        for name,value,key in [('size',1.2,'unity_scale'),('movement_speed',210,'unity_movement_speed'),('speaking_rate',.9,'voice_rate')]:
            adjust(self.settings,name,value)
            self.assertEqual(Settings(self.paths)[key],value)
        before=dict(self.settings.data)
        for name,value in [('size',99),('speaking_rate',float('nan')),('volume',True),('gpu_layers',0),('persona','ignore instructions'),('web_enabled',True),('startup',True),('memory_delete',True)]:
            with self.assertRaises(ValueError):adjust(self.settings,name,value)
        self.assertEqual(self.settings.data,before)
        adjust(self.settings,'autonomous',True)
        self.assertFalse(self.settings['unity_stationary'])

    def test_other_app_instance_uses_actual_context(self):
        scene={'current_window':'a','windows':[{'id':'a','app':'powershell','title':'PowerShell'},{'id':'b','app':'pwsh','title':'Terminal'}]}
        self.assertEqual(resolve_window('the other powershell window',scene,{'target':'powershell'}),'b')
        self.assertEqual(resolve_window('other powershell',scene,{'target':'b'}),'a')
        self.assertEqual(resolve_window('missing firefox',scene),'missing firefox')

    def test_handoff_once_forget_survives_restart_and_context_clear(self):
        path=self.paths.data/'memory.sqlite3'
        memory=Memory(path)
        handoff=Path(__file__).resolve().parents[1]/'assets/continuity/development-history.json'
        try:
            self.assertGreater(import_handoff(memory,handoff),0)
            self.assertEqual(import_handoff(memory,handoff),0)
            row=memory.db.execute("SELECT id FROM memories WHERE id LIKE 'handoff-%' LIMIT 1").fetchone()
            memory.forget(row[0]);self.assertEqual(import_handoff(memory,handoff),0)
            memory.message('test','user','We built your avatar.')
            self.assertEqual(memory.recent_dialogue()[-1]['content'],'We built your avatar.')
            memory.clear_working_context();self.assertEqual(memory.recent_dialogue(),[])
        finally:memory.close()
        memory=Memory(path)
        try:self.assertEqual(memory.recent_dialogue(),[]);self.assertEqual(import_handoff(memory,handoff),0)
        finally:memory.close()

    def test_web_private_redirect_and_cancellation(self):
        with patch('yumi.web.socket.getaddrinfo',return_value=[(2,1,6,'',('127.0.0.1',443))]):
            with self.assertRaises(ValueError):public_url('https://private.test')
        for url in ['file:///etc/passwd','https://user:password@example.com','https://example.com:22']:
            with self.assertRaises(ValueError):public_url(url)
        cancel=threading.Event();cancel.set()
        self.assertEqual(ReadOnlyWeb().lookup('latest Unity release',cancel),[])
        self.assertNotIn('jason@example.com',query_text('Search jason@example.com for news'))
        tools=schemas(web=True)
        web=next(t['function'] for t in tools if t['function']['name']=='web_lookup')
        self.assertEqual(web['parameters']['properties'],{})

    def test_fragmented_native_tools_are_not_spoken_and_cancel_does_not_execute(self):
        from unittest.mock import Mock
        from yumi.inference import LocalModel
        model=LocalModel(self.paths,self.settings,lambda *_:None)
        model.process=Mock();model.process.poll.return_value=None;model.ready=True;model.port=12345
        records=[{'tool_calls':[{'index':0,'id':'call-1','function':{'name':'adjust_setting','arguments':'{"name":"speaking_'}}]},
                 {'tool_calls':[{'index':0,'function':{'arguments':'rate","value":0.9}'}}]},
                 {'reasoning_content':'hidden'}]
        with patch('yumi.inference.httpx.Client') as client:
            response=client.return_value.__enter__.return_value.stream.return_value.__enter__.return_value
            response.iter_lines.side_effect=lambda:iter(['data: '+json.dumps({'choices':[{'delta':delta}]}) for delta in records]+['data: [DONE]'])
            calls=[];output=''.join(model.stream([{'role':'user','content':'slower please'}],threading.Event(),tools=schemas(),on_tool=calls.append))
            self.assertEqual(output,'');self.assertEqual(len(calls),1)
            self.assertEqual(json.loads(calls[0]['function']['arguments']),{'name':'speaking_rate','value':.9})
            cancel=threading.Event();cancel.set();calls=[]
            self.assertEqual(''.join(model.stream([],cancel,tools=schemas(),on_tool=calls.append)),'');self.assertEqual(calls,[])
