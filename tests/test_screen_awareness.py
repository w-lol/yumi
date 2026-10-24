import base64
import threading
import unittest
from unittest.mock import patch
import numpy as np
from yumi.perception import Perception, screen_request


class ScreenAwarenessTests(unittest.TestCase):
    def perception(self):
        value=Perception.__new__(Perception)
        value.active=threading.Event();value.active.set()
        value.settings={'screen_enabled':True,'screen_exclusions':[],'monitor_index':0}
        return value

    def test_explicit_screen_questions(self):
        for text in ['What do you see?','Can you read my screen?','Describe the desktop','What is on this monitor?']:
            self.assertTrue(screen_request(text),text)
        for text in ['Go to the right monitor','I bought a new display','What do you see as the benefits?']:
            self.assertFalse(screen_request(text),text)

    def test_disabled_and_excluded_capture_never_grabs(self):
        value=self.perception();value.settings['screen_enabled']=False
        with patch('mss.mss',side_effect=AssertionError('must not capture')):
            self.assertIsNone(value.screen_snapshot())
        value.settings['screen_enabled']=True
        with patch.object(value,'screen_allowed',return_value=False),patch('mss.mss',side_effect=AssertionError('must not capture')):
            self.assertIsNone(value.screen_snapshot())

    def test_fresh_in_memory_frame_and_all_monitor_selection(self):
        value=self.perception()
        with patch('mss.mss') as factory:
            grabber=factory.return_value.__enter__.return_value
            grabber.monitors=[{'all':True},{'primary':True}]
            grabber.grab.return_value=np.zeros((20,30,4),dtype=np.uint8)
            snapshot=value.screen_snapshot()
            grabber.grab.assert_called_once_with({'all':True})
            self.assertEqual(snapshot['monitor'],0)
            self.assertTrue(base64.b64decode(snapshot['image']).startswith(b'\xff\xd8'))
