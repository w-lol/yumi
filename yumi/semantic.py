"""Small optional local semantic encoder; embeddings stay in indexed SQLite storage."""
from __future__ import annotations
from collections import OrderedDict
import hashlib
import threading
import numpy as np


class Encoder:
    def __init__(self, directory):
        import os
        os.environ['ORT_DISABLE_TELEMETRY'] = '1'
        import onnxruntime as ort
        ort.disable_telemetry_events()
        from tokenizers import Tokenizer
        directory = directory.resolve()
        self.version = 'minilm-' + hashlib.sha256((directory/'model_quantized.onnx').read_bytes()).hexdigest()[:16]
        self.tokenizer = Tokenizer.from_file(str(directory/'tokenizer.json'))
        self.tokenizer.enable_truncation(max_length=256)
        self.tokenizer.enable_padding()
        options = ort.SessionOptions()
        options.intra_op_num_threads = 1
        options.inter_op_num_threads = 1
        self.session = ort.InferenceSession(str(directory/'model_quantized.onnx'), sess_options=options,
                                           providers=['CPUExecutionProvider'])
        self.lock = threading.Lock()
        self.cache = OrderedDict()
        # Deterministic random-hyperplane LSH: eight indexed 8-bit buckets.
        self.projections = np.random.default_rng(2026).standard_normal((64,384)).astype(np.float32)

    def encode(self, text):
        key = hashlib.sha256(text.encode()).digest()
        with self.lock:
            if key in self.cache:
                self.cache.move_to_end(key)
                return self.cache[key].copy()
            encoded = self.tokenizer.encode(text[:8000])
            ids = np.asarray([encoded.ids], dtype=np.int64)
            mask = np.asarray([encoded.attention_mask], dtype=np.int64)
            types = np.asarray([encoded.type_ids], dtype=np.int64)
            values = {'input_ids':ids, 'attention_mask':mask, 'token_type_ids':types}
            outputs = self.session.run(None, {i.name:values[i.name] for i in self.session.get_inputs()})
            hidden = outputs[0]
            vector = (hidden * mask[:,:,None]).sum(axis=1)[0] / max(1, mask.sum())
            vector = np.asarray(vector / max(1e-9,np.linalg.norm(vector)),dtype=np.float32)
            self.cache[key]=vector
            if len(self.cache)>64:self.cache.popitem(last=False)
            return vector.copy()

    def buckets(self, vector):
        bits=(self.projections @ vector >= 0).astype(np.uint8)
        return [int(np.packbits(bits[i:i+8])[0]) for i in range(0,64,8)]

    def clear(self):
        with self.lock:self.cache.clear()
