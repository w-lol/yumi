import tempfile
import unittest
import threading
import time
import numpy as np
from yumi.audio import Audio, Utterances
from yumi.paths import Paths
from yumi.settings import Settings


class AudioTests(unittest.TestCase):
    def test_speech_backpressure_remains_interruptible(self):
        with tempfile.TemporaryDirectory() as tmp:
            paths=Paths(tmp);a=Audio(paths,Settings(paths),lambda *_:None,lambda _:None)
            a.tts=object();a.active.set();a.silenced.clear()
            for _ in range(a.speech.maxsize):a.speech.put((a.epoch,'old','neutral'))
            cancel=threading.Event()
            thread=threading.Thread(target=lambda:a.say('A complete detailed answer.',wait=True,cancel=cancel))
            thread.start();time.sleep(.03)
            self.assertTrue(thread.is_alive())
            cancel.set();a.interrupt();thread.join(timeout=.5)
            self.assertFalse(thread.is_alive())
            self.assertTrue(a.silenced.is_set())
            while not a.speech.empty():self.assertNotEqual(a.speech.get()[0],a.epoch)
    def test_vad_bounded_and_silence_not_utterance(self):
        vad=Utterances()
        self.assertIsNone(vad.feed(np.zeros(480,dtype=np.float32))[1])
        output=None
        for _ in range(30):vad.feed(np.ones(480,dtype=np.float32)*0.05)
        for _ in range(25):
            _,candidate,_=vad.feed(np.zeros(480,dtype=np.float32))
            if candidate is not None:output=candidate
        self.assertIsNotNone(output)
        self.assertLessEqual(len(output),16000*12)
        self.assertLessEqual(len(vad.pre),5)

    def test_interrupt_silences_and_drops_stale_audio_without_losing_capture(self):
        with tempfile.TemporaryDirectory() as tmp:
            a=Audio(Paths(tmp),Settings(Paths(tmp)),lambda *_:None,lambda _:None)
            a.active.set()
            a.playback.put((a.epoch,np.ones(1024,dtype=np.float32)))
            output=np.zeros((512,1),dtype=np.float32)
            a._output(output,512,None,None)
            self.assertGreater(float(output.max()),0)
            a.blocks.put((a.capture_epoch,np.ones(480,dtype=np.float32)))
            a.interrupt()
            a._output(output,512,None,None)
            self.assertTrue((output==0).all())
            self.assertEqual(a.playback.qsize(),0)
            self.assertEqual(a.blocks.qsize(),1)
            a.pause()
            self.assertEqual(a.blocks.qsize(),0)

    def test_speaker_safe_capture_gates_echo_and_headphones_does_not(self):
        with tempfile.TemporaryDirectory() as tmp:
            settings=Settings(Paths(tmp));a=Audio(Paths(tmp),settings,lambda *_:None,lambda _:None)
            a.active.set();a.speaking=True
            frame=np.ones((480,1),dtype=np.float32)
            a._input(frame,480,None,None)
            self.assertEqual(a.blocks.qsize(),0)
            settings.set('audio_mode','headphones')
            a._input(frame,480,None,None)
            self.assertEqual(a.blocks.qsize(),1)

    def test_sensor_arrays_not_serialized_in_journal(self):
        from yumi.journal import Journal
        with tempfile.TemporaryDirectory() as tmp:
            p=Paths(tmp);j=Journal(p)
            j.write('test',audio=np.ones(16000),frame=np.ones((3,3)),prompt='PRIVATE',count=2)
            j.close()
            text=(p.logs/'events.jsonl').read_text()
            self.assertNotIn('PRIVATE',text)
            self.assertNotIn('audio',text)
            self.assertNotIn('frame',text)
            self.assertIn('"count": 2',text)
