import unittest
from yumi.delivery import classify, speech_text, Phrases, STYLES


class DeliveryTests(unittest.TestCase):
    def test_depth_and_context_change_actual_delivery(self):
        self.assertLess(classify('Hi Yumi').max_tokens, 150)
        self.assertGreater(classify('Explain networking in detail').max_tokens, 2500)
        self.assertEqual(classify('I am overwhelmed').style, 'comforting')
        self.assertEqual(classify('This is broken again').situation, 'user_frustration')
        self.assertLess(STYLES['comforting'], STYLES['excited'])

    def test_streamed_code_and_stage_directions_never_spoken(self):
        source = 'Here is the answer.\n```python\nsecret_metadata = 42\n```\n**Hello** [Jason](https://example.com). *laughs* 😄'
        phrases = Phrases()
        output = []
        for c in source: output.extend(phrases.feed(c))
        output.extend(phrases.feed('', final=True))
        combined = ' '.join(output)
        self.assertIn('Hello', combined)
        self.assertNotIn('metadata', combined)
        self.assertNotIn('laughs', combined)
        self.assertNotIn('https', combined)
        self.assertFalse(speech_text('{"action":"jump"}'))
