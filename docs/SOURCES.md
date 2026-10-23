# Pinned components, sources and licenses

The exact immutable model revisions, filenames, byte sizes and SHA-256 hashes are in
`assets/model-lock.json`. Runtime/bootstrap artifact URLs and hashes are in
`assets/bootstrap-artifacts.json`. Every Windows Python package and transitive dependency
is pinned with a wheel hash in `requirements-windows.lock`; the Windows-specific
conditional dependency on colorama is included. This is a **CPython 3.13, Windows x64**
lock. `requirements-linux-tested.txt` records the separate cloud test environment.

| Component | Selected version / source | License / notes |
|---|---|---|
| CPython | 3.13.17, PSF package at `https://api.nuget.org/v3-flatcontainer/python/3.13.17/python.3.13.17.nupkg` | PSF; official NuGet distribution described in Python's Windows documentation. Installer checks pinned hash and Python publisher Authenticode signature on Windows. |
| Qt for Python | PySide6 6.8.3 from PyPI | LGPLv3/GPLv3/commercial terms; this app uses the free LGPL distribution as separate, replaceable DLLs. No paid license is required. Preserve installed third-party notices. |
| llama.cpp | ggml-org release **b11669**, Windows CUDA 12.4 and CPU archives | MIT; official GitHub release assets, pinned SHA-256. CUDA runtime redistributed by upstream under NVIDIA's applicable runtime terms. |
| Conversation and vision | **Qwen3-VL-4B-Instruct**, official Qwen GGUF, Q4_K_M + F16 projector | Apache 2.0, confirmed against the original Qwen model metadata, not inferred from an unrelated engine license. Original model revision `ebb281ec70b05090aa6165b016eac8ec08e71b17`; GGUF revision in manifest. |
| Recognition | faster-whisper 1.2.1, Systran `faster-whisper-base.en` | MIT; local CTranslate2 CPU int8 inference. Manifest includes `vocabulary.txt`, tokenizer, config and weights. |
| Speech engine | sherpa-onnx 1.13.8 | Apache 2.0; native Windows wheel and bundled phonemizer dependencies retain their own notices. |
| Starter voice | Piper `en_US-ljspeech-medium`, distributed by sherpa-onnx | Piper voice repository declares MIT; individual model card identifies the LJ Speech dataset as **public domain**. Female American English. Model card copied into this project. eSpeak NG phonemization data/code have their own GPL terms; keep them as replaceable third-party components and preserve upstream source references/notices. |
| Semantic encoder | Xenova `all-MiniLM-L6-v2`, quantized ONNX | Apache 2.0; local CPU encoder, 384-dimensional vectors. |
| Audio I/O | sounddevice 0.5.2 | MIT; Windows wheel includes PortAudio. No external audio app needed. |
| Screen capture | mss 10.0.0 | MIT; screenshots remain in RAM. |
| Camera | opencv-python-headless 4.11.0.86 | Apache 2.0 plus bundled third-party notices; no extra OpenCV GUI. |
| Storage | Python SQLite/FTS5 | SQLite public domain. |

Primary references:

- https://docs.python.org/3.13/using/windows.html#the-nuget-org-packages
- https://doc.qt.io/qt-6.8/supported-platforms.html
- https://github.com/ggml-org/llama.cpp/releases/tag/b11669
- https://github.com/ggml-org/llama.cpp/blob/b11669/tools/server/README.md
- https://github.com/ggml-org/llama.cpp/blob/b11669/docs/multimodal.md
- https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct
- https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct-GGUF
- https://huggingface.co/Systran/faster-whisper-base.en
- https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/python-api-examples/offline-tts-play.py
- https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models
- https://huggingface.co/rhasspy/piper-voices
- https://huggingface.co/rhasspy/piper-voices/blob/main/en/en_US/ljspeech/medium/MODEL_CARD
- https://keithito.com/LJ-Speech-Dataset/
- https://huggingface.co/Xenova/all-MiniLM-L6-v2
- https://github.com/microsoft/onnxruntime/blob/v1.31.0/onnxruntime/core/platform/telemetry_environment.h

Normal operation never fetches `latest`, resolves a model alias, or downloads missing
weights. Missing assets cause a clear repair error. The installer uses the included
manifest. The catalog-resolution helper exists for maintainers preparing a new manifest;
it is not normal runtime behavior. Review license metadata when changing a model.

The source ZIP distributes Yumi's source and metadata, not a repackaged bundle of all
third-party binaries/weights. Setup obtains exact artifacts from their upstream sources.
Upstream distributions retain their license texts. When redistributing an installed
binary bundle, fulfill each included component's license obligations; in particular,
keep LGPL libraries replaceable and include applicable notices/source offers.

Cloud-only validation used a privately compiled PortAudio 19.7.0 OSS backend because
this Linux container has no physical audio devices. Its official tag archive SHA-256:
`5af29ba58bbdbb7bbcefaaecc77ec8fc413f0db6f4c4e286c40c3e1b83174fa0`.
This Linux library is not part of the Windows installer or a claim about your devices.
