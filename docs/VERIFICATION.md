# Verification and release status

This package is a **functional development build**, not a claim that every requirement
in the master brief has passed Windows acceptance. Source and installer are delivered;
Windows 10, NVIDIA driver/CUDA execution, physical devices and long-duration use on the
4070 Super remain unverified. No access to Jason's computer was available.

## Executed in the cloud

- 20 unit/contract tests passed: memory correction/deletion/restart, derived-record
  invalidation, deletion after backup restore/import, bounded retrieval, transaction
  rollback, attention calibration/undo/quiet mode, no mandatory wake word, capability
  refusal, schema validation, split reasoning-tag filtering, secret redaction,
  RAM-only audio queue cancellation and download resumption/archive traversal checks.
- Real Qwen3-VL-4B Q4_K_M inference with the pinned llama.cpp binary: authenticated
  streaming responses, three grounded text samples, cancellation and recovery.
  Unauthenticated inference requests returned HTTP 401. Managed server cleanup passed.
- Real vision inference identified a synthetic red image correctly. This establishes
  image-path functionality, not reliable open-world scene understanding.
- Real local voice synthesis and Whisper recognition ran entirely in memory. Three
  samples recognized the intended phrase; the name "Yumi" was sometimes rendered as "UME" or "You Me".
  No recording file was created. This does not validate physical playback or microphones.
- Real MiniLM semantic retrieval found a cycling preference using the query "cycling
  outdoors". Deletion removed vectors; restoring an older backup did not resurrect it.
- Headless Qt: exactly two visible windows; chat and memory through the UI; text survived
  missing voice assets; a terminated model worker recovered; Emergency Stop during
  generation returned promptly; shutdown left no conversation/model worker.
- A bounded-history/repeated-interruption run used 10,001 memory records, 300 indexed
  records, 40 retrievals, six completed turns and six cancelled turns. It lasted about
  65 seconds. This is **not** the requested long-duration target-machine soak.
- All PowerShell scripts passed parsing with PowerShell 7.6.8 on Linux. Windows
  PowerShell 5.1 execution, Authenticode validation and installer rollback execution on
  Windows remain unverified. Windows wheel hashes and environment markers were audited.

Machine-readable evidence is in `docs/validation/`. Small sample sizes and repeated
prompts mean these measurements must not be treated as user-facing performance SLAs.
The cloud had five logical CPUs, approximately 33 GiB RAM and no exposed NVIDIA GPU.
No numbers below are measurements on an RTX 4070 Super.

Measured cloud examples:

- Cold model startup: approximately 3.10 seconds.
- Text time to first token: 0.656 / 0.070 / 0.065 seconds for the three short, repeated
  prompts; later samples benefit from prompt caching.
- First synthesized audio chunk: 0.212 / 0.181 / 0.156 seconds. This is **not** time to
  physical speaker output or end-of-speech-to-first-response.
- Synthesis plus recognition loopback: 0.95 / 0.85 / 0.76 seconds for the short phrase.
- Vision request: approximately 2.75 seconds for the synthetic image.
- Desktop emergency-stop dispatch: approximately 8.46 ms in the earlier captured run;
  this does **not** establish physical interruption-to-silence latency.
- Long-history retrieval p95: approximately 67.5 ms in the recorded test, with concurrent
  desktop validation workload. The memory database was approximately 8.14 MiB.

## Material limitations

Kokoro update: 24 regression tests passed locally. Real Kokoro v1.0 Heart and Bella
synthesis produced finite, non-silent 24 kHz audio in RAM; Heart synthesis fed into
Whisper recognized the intended phrase in three repetitions. Default voice loading,
voice selection, callback delivery, and custom Piper folder compatibility were checked.
The desktop regression check also passed. See `validation/kokoro-linux.json` and
`validation/kokoro-integration.json`. The Windows Actions workflow now downloads and
verifies the same archive and synthesizes both voices without devices; its result is
included as `validation/kokoro-windows.json` in successful installer artifacts.
These checks do not prove physical playback quality or full-duplex speaker support.

1. **Speaker-safe mode is half duplex.** No validated acoustic echo cancellation or
   full-duplex speaker interruption backend is implemented. Headphone barge-in exists,
   but actual devices, noise and double talk still require testing.
2. Attention is heuristic plus bounded feedback calibration. There is no reliable
   speaker identity, room/media separation or visual gaze-intent classifier. False
   activations and missed directed turns must be measured in the target room.
3. STT uses completed VAD segments, not live partial transcription. The Kokoro
   voice offers voice selection, rate/volume and pronunciation controls, not independent pitch or
   expressive prosody. Affect adjusts rate slightly; its broader presentation is basic.
4. The avatar is a working procedural placeholder. Multi-monitor/DPI, click-through,
   fullscreen detection, focus behavior and monitor hot-plug code are Windows-unverified.
   There is no ready-made Live2D/VRM asset importer; replace the renderer through its
   documented interface.
5. Memory has hybrid retrieval and conservative explicit-fact learning. It is not
   unrestricted autonomous autobiographical learning. Session episodes are extractive;
   sophisticated task planning, relationship reasoning and automatic conflict resolution
   beyond explicit corrections are limited.
6. Application exclusions are foreground-process based. They are not a compositor-level
   guarantee that an excluded window cannot be partially visible behind another window.
   Disable screen capture for sensitive work. No routine frames are persisted.
7. Native workers use bounded threads plus a separate model process. A native kernel
   already executing may finish after cancellation, but stale results are discarded.
   Full sleep/wake recovery and full-disk fault injection on Windows remain unverified.
8. No signed automatic updater or arbitrary plugin installation is shipped. Maintenance
   uses the provided setup/repair/uninstall scripts. No machine-control adapters exist.

These are real remaining differences from the complete master brief, not features
hidden behind fake enabled controls. They require further implementation and/or
hardware acceptance before calling the entire requested scope finished.

## Required target-machine acceptance

Use a non-sensitive desktop and room for sensor checks. The app does not create audio
recordings or routine image/video files.

1. Install into the exact path containing spaces. Interrupt a download, rerun setup,
   then rerun a healthy setup; confirm settings/memory survive and no duplicate process
   owns the devices. Verify preflight and smoke reports show the actual hardware.
2. Open both windows. Start, pause, resume, hide avatar, close/reopen the board, and
   restart workers. Verify device indicators follow real capture state.
3. Speak a directed turn, a follow-up without Yumi's name, unrelated room speech and
   media speech. Try feedback corrections and restart. Record false activations/misses.
4. With headphones, interrupt a spoken answer and resume. With speakers, confirm the
   explicitly half-duplex behavior and check that Yumi does not respond to herself.
5. Apply an explicit memory correction, delete the fact, restart, restore an older
   backup, and verify it cannot be recalled. Inspect derived records and backups.
6. Check camera and screen descriptions, age indicators, toggles and foreground-process
   exclusions. Physically unplug devices and confirm no continuing "live" claims.
7. Exercise Emergency Stop during generation, recognition, playback and perception.
   Measure input-to-audio and interruption-to-silence using the board's actual callback
   metrics; do not substitute CPU dispatch timings.
8. Disconnect the network after assets are installed; run conversation, vision, voice
   and memory without accounts or API keys. Inspect network activity for unexpected
   outbound connections and inspect application writes for forbidden media files.
9. Run at least a 30-minute mixed interaction soak with repeated interruptions and
   memory maintenance. Record CPU/RAM/VRAM, response tails, camera load and avatar timing.
10. Verify multi-monitor/DPI/fullscreen/focus behavior, sleep/wake, backup restoration,
    folder relocation and repair. Test uninstall on a disposable copy with an unrelated
    file added; unknown files must remain.

A successful target run should produce reports and observed results, not merely a
checked-off assertion. Keep failed, skipped and unrun cases separate.
