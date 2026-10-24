import json
import tempfile
import time
import unittest
from pathlib import Path
from websockets.sync.client import connect
from websockets.exceptions import ConnectionClosed, InvalidStatus
from yumi.paths import Paths
from yumi.unity_bridge import UnityBridge, action_intent, validate_action


class UnityBridgeTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='Unity bridge spaces ')
        self.paths=Paths(self.temp.name)
        self.bridge=UnityBridge(self.paths,lambda:{'state':'engaged','mouth':{'aa':.3}},lambda *_:None)
        self.bridge.start()
        self.assertTrue(self.bridge.ready.wait(5),self.bridge.error)
        self.url=f'ws://127.0.0.1:{self.bridge.port}/avatar/v1'

    def tearDown(self):
        self.bridge.close()
        self.assertFalse(self.bridge.thread.is_alive())
        self.assertFalse(self.bridge.descriptor.exists())
        self.temp.cleanup()

    def test_unauthenticated_client_receives_no_state(self):
        with connect(self.url) as socket:
            with self.assertRaises(ConnectionClosed):socket.recv(timeout=2)
        self.assertFalse(self.bridge.connected)

    def test_browser_origin_rejected_even_with_valid_token(self):
        with self.assertRaises(InvalidStatus):
            with connect(self.url,origin='https://untrusted.example',additional_headers={'Authorization':'Bearer '+self.bridge.token}):pass

    def test_authenticated_state_actions_ack_and_rejection(self):
        with connect(self.url,additional_headers={'Authorization':'Bearer '+self.bridge.token}) as socket:
            state=json.loads(socket.recv(timeout=2))
            self.assertEqual(state['protocol'],1)
            self.assertEqual(state['payload']['mouth']['aa'],.3)
            with self.assertRaisesRegex(RuntimeError,'ready'):self.bridge.action({'type':'wave'})
            socket.send(json.dumps({'protocol':1,'type':'hello','payload':{'ready':True,'renderer':'Unity'}}))
            deadline=time.monotonic()+2
            while not self.bridge.status.get('ready') and time.monotonic()<deadline:time.sleep(.01)
            identifier=self.bridge.action({'type':'move_to_window','target':'Discord'})
            while True:
                action=json.loads(socket.recv(timeout=2))
                if action['type']=='action':break
            self.assertEqual(action['id'],identifier)
            self.assertEqual(action['payload']['target'],'Discord')
            socket.send(json.dumps({'protocol':1,'type':'action_result','payload':{'action_id':identifier,'success':True,'message':'finished'}}))
            deadline=time.monotonic()+2
            while self.bridge.status.get('action_id')!=identifier and time.monotonic()<deadline:time.sleep(.01)
            self.assertTrue(self.bridge.status.get('ready'))
            socket.send(json.dumps({'protocol':1,'type':'shell','payload':{'command':'forbidden'}}))
            with self.assertRaises(ConnectionClosed):
                while True:socket.recv(timeout=2)

    def test_unity_scene_reports_keep_connection_ready_and_commands_work(self):
        headers={'Authorization':'Bearer '+self.bridge.token}
        with connect(self.url,additional_headers=headers) as socket:
            socket.recv(timeout=2)
            socket.send(json.dumps({'protocol':1,'type':'status','payload':{'ready':True,'renderer':'Unity','message':'Motion=Idle; monitors=3; windows=8','windows':None,'monitors':None}}))
            for i in range(3):
                socket.send(json.dumps({'protocol':1,'type':'scene','payload':{'ready':False,'success':False,'windows':[{'id':'ps1','title':'PowerShell','app':'powershell'}], 'monitors':[{'id':'display1','label':'Display 1'}],'current_window':None,'pointer_window':''}}))
                deadline=time.monotonic()+2
                while not self.bridge.scene.get('windows') and time.monotonic()<deadline:time.sleep(.01)
                self.assertEqual(self.bridge.scene['windows'][0]['id'],'ps1')
                self.assertTrue(self.bridge.connected)
                self.assertIs(self.bridge.status['ready'],True)
                self.assertEqual(json.loads(socket.recv(timeout=2))['type'],'state')
            identifier=self.bridge.action({'type':'wave'})
            while True:
                frame=json.loads(socket.recv(timeout=2))
                if frame['type']=='action':break
            self.assertEqual(frame['id'],identifier)

    def test_snapshot_failure_is_visible_and_invalidates_readiness(self):
        with connect(self.url,additional_headers={'Authorization':'Bearer '+self.bridge.token}) as socket:
            socket.recv(timeout=2)
            def broken():raise RuntimeError('fixture snapshot failure')
            self.bridge.snapshot=broken
            with self.assertRaises(ConnectionClosed):
                while True:socket.recv(timeout=2)
        deadline=time.monotonic()+2
        while self.bridge.connected and time.monotonic()<deadline:time.sleep(.01)
        self.assertIn('RuntimeError',self.bridge.error)
        self.assertIs(self.bridge.status.get('ready'),False)

    def test_high_level_intents_cannot_supply_os_commands_or_coordinates(self):
        self.assertEqual(action_intent('Yumi, go over to Discord.')['target'],'Discord')
        self.assertEqual(action_intent('Go sit on Chrome')['type'],'sit')
        self.assertEqual(action_intent('stop moving')['type'],'stop_movement')
        self.assertEqual(action_intent('stop following my mouse')['type'],'stop_movement')
        self.assertEqual(action_intent('walk across the monitors'),{'type':'other_monitor','target':'next'})
        self.assertEqual(action_intent('go to the right monitor'),{'type':'other_monitor','target':'right'})
        self.assertEqual(action_intent('sit on this window'),{'type':'sit','target':'cursor_window'})
        self.assertEqual(action_intent('jump to that window'),{'type':'jump_to_platform','target':'cursor'})
        self.assertIsNone(action_intent('Open Discord'))
        with self.assertRaises(ValueError):validate_action({'type':'move_to_window','x':100})
        with self.assertRaises(ValueError):validate_action({'type':'shell','target':'cmd.exe'})
        with self.assertRaises(RuntimeError):self.bridge.action({'type':'wave'})

    def test_reconnection_requires_fresh_readiness_and_drops_old_actions(self):
        headers={'Authorization':'Bearer '+self.bridge.token}
        with connect(self.url,additional_headers=headers) as socket:
            socket.recv(timeout=2)
            socket.send(json.dumps({'protocol':1,'type':'hello','payload':{'ready':True}}))
            deadline=time.monotonic()+2
            while not self.bridge.status.get('ready') and time.monotonic()<deadline:time.sleep(.01)
            self.assertTrue(self.bridge.status.get('ready'))
        deadline=time.monotonic()+2
        while self.bridge.connected and time.monotonic()<deadline:time.sleep(.01)
        with connect(self.url,additional_headers=headers) as socket:
            state=json.loads(socket.recv(timeout=2))
            self.assertEqual(state['type'],'state')
            with self.assertRaisesRegex(RuntimeError,'ready'):self.bridge.action({'type':'wave'})
            self.assertEqual(len(self.bridge.actions),0)
