# Install and run Yumi

This is a functional development build. Windows hardware acceptance is still required;
read `VERIFICATION.md` before treating it as a finished production release.

## Apply a downloaded update

Download **Yumi-Windows-installer** from a successful Actions run and extract the
artifact once. Keep `Update-Yumi.ps1`, `Yumi.zip` and `Yumi.zip.sha256` together.
Close Yumi, open PowerShell in that folder, and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Update-Yumi.ps1
```

For a different installation folder, add `-Root 'D:\Yumi'`. The updater verifies the
adjacent checksum, stages the bundle and runs the existing setup against your root.
Memory, settings and original avatar assets are retained. The Unity add-on source
requires a local build; see [Unity instructions](UNITY_INTEGRATION.md). This update
contains no verified Unity player.

## One PowerShell setup invocation

Download `Yumi.zip` into your Windows Downloads folder. Paste this **one invocation**
into Windows PowerShell 5.1:

```powershell
& { $r = 'C:\Users\jason\3D Objects\yumi'; Expand-Archive -LiteralPath "$env:USERPROFILE\Downloads\Yumi.zip" -DestinationPath "$r\downloads\release-0.1.0" -Force; powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$r\downloads\release-0.1.0\scripts\setup.ps1" -Root $r }
```

To obtain the ZIP through GitHub, open **Actions → Windows installer**, select a
successful run, and download **Yumi-Windows-installer** under **Artifacts** (GitHub
sign-in is required for Actions artifacts). Extract that downloaded artifact ZIP once;
it contains `Yumi.zip`, `Yumi.zip.sha256` and `Update-Yumi.ps1`. Put the inner `Yumi.zip` in Downloads.
The artifact expires after 30 days; rerun the workflow to create another copy.

The workflow runs for pull requests and pushes to `main`, and can be started manually
with **Run workflow** after it is merged into `main`. Optional release publication is
manual: run it on `main`, select `publish_release`, and supply a new tag such as
`yumi-v0.1.0-dev.1`. This publishes a GitHub prerelease with the bundle, checksum and updater attached.
Existing tags/releases are not overwritten. A failed publication still leaves the
successful build's Actions artifact available.

The build packages the existing bootstrap installer, not an MSI or signed executable.
It runs Windows dependency/native-import checks, unit tests and PowerShell 5.1 parsing;
it validates real Kokoro synthesis in RAM but does not exercise GPU inference, physical devices or the full setup in CI.
For a local equivalent packaging command, run
`python scripts/package_installer.py --output dist` from the checkout.

The script is included in the ZIP. No invented release URL, global Python, Node, Docker,
WSL, paid account, inference API key, service, scheduled task, PATH change, permanent
execution-policy change, or administrator access is required. The invocation's execution
policy applies only to its child PowerShell process. Internet is needed for initial
verified downloads. The downloaded ZIP itself is initially in the location you choose;
all installer-controlled downloads and installed content go beneath the specified root.

Setup checks Windows build, CPU, RAM, free disk, NVIDIA driver and free VRAM before
model downloads. Windows 10 1809 or later and x64 are required by the chosen Qt build.
It reserves 10 GiB free disk for downloads, extraction and working headroom, and requires
at least 8 GiB physical RAM (16+ recommended). CUDA mode checks driver >=551.61 and
6 GiB available VRAM. The 4070 Super is the intended GPU; no benchmark on that machine
has been performed. If CUDA is unavailable, `-Cpu` is a free explicit fallback; performance
will differ. Setup never installs a GPU driver or an OS component.

The private CPython distribution is the Python Software Foundation's official NuGet
package. Its DLLs and packages remain in `runtime/`. Native import checks and a real
language-model plus in-memory TTS/STT smoke test run before the success marker and
launcher pointer are written. Missing/denied sensor devices are reported by the app;
a model smoke test does not prove that physical audio/camera devices work.

## Opening the two applications

Double-click `C:\Users\jason\3D Objects\yumi\Yumi.vbs` to open **Yumi Control Board**
and **Yumi Avatar**. They share one owner process and an automatically managed private
model server. No browser window or additional console is part of normal operation.

Both windows initially open inactive. **Start / Resume** warms models and activates the
configured microphone/camera/screen. **Pause** stops capture and speech; the language
model remains warm. **Emergency Stop** also unloads the language model. Closing the
control board stops both windows and workers by default; the optional close-to-minimize
setting is explicit. Closing the avatar hides it. The board remains available.

Use Voice & listening to select devices; restart workers to apply a device change.
The default voice is **Kokoro v1.0 Heart** (`af_heart`), running locally at 24 kHz.
Choose another US English voice under **Voice & listening → Kokoro voice**; it applies
to the next synthesized sentence. Speaking rate and volume remain adjustable. Setup
downloads the checksum-pinned 350 MB Kokoro archive and replaces the previous default
voice transactionally. Updating preserves memory and settings; existing selected custom
Piper voice folders continue to use Piper. Choose `models/voice` as the local voice asset
folder to return to the installed Kokoro model, then restart workers.
Speaker-safe mode is explicitly **half duplex**. Use headphones for microphone barge-in.
This release does not supply a validated full-duplex speaker echo canceller. A transcript
similarity gate rejects likely playback transcripts, but it is not acoustic echo cancellation.

Typed chat remains available if voice or a sensor fails. `remember: <fact>` stores an
explicit fact. Direct first-person preferences can also be learned from engaged dialogue.
Use the Memory page for exact correction, deletion, backup and export. The board shows
which memory IDs influenced a response. General computer actions are unavailable.

## Repair and updates

Rerun the same `setup.ps1 -Root 'C:\Users\jason\3D Objects\yumi'` invocation after
closing Yumi. It verifies existing artifacts, resumes model partial downloads, skips a
healthy dependency environment and runtime, and preserves settings and memory. Python
runtime directories are keyed by the dependency-lock hash, so changed dependencies do
not overwrite the previously active interpreter. Source files have a rollback copy;
previous native/voice runtimes are retained until a new candidate is prepared. On a
failed post-install smoke test, source and replaced native/voice runtime are restored.
The launcher pointer is committed only after a successful smoke test.

Downloads and previous runtime backups consume disk. Inspect them before deleting old
versions. No automatic self-update or source-code rewriting runs during conversation.
Updates require a supplied, reviewed release package. The app's maintenance page explains
repair; it does not pretend to implement a signed online update service.

The bootstrap Python artifact uses an atomic `.part` download with bounded retries;
a failed bootstrap request restarts that small artifact. Large model downloads support
HTTP range resumption and verify the complete SHA-256 before activation. No checksum,
TLS or signature check is disabled to make installation pass.

If startup fails, inspect `logs/install.log`, `logs/preflight.json`, and
`logs/target-smoke.json`. Native DLL/GPU errors are installation failures, not readiness.
If a system prerequisite is missing, setup reports it; it does not change drivers or
install a global Visual C++ redistributable silently. The portable Python distribution
already includes its required Python runtime DLLs.

## Relocation

Stop both windows, move the entire root, and rerun the included setup script with
`-Root '<new full path>'`. Launchers resolve paths relative to their own location; no
virtual environment is claimed to be inherently relocatable. The runtime pointer is
relative. Models, database and settings remain under the moved root. No remote account
credentials exist in this build; local server authentication keys are regenerated each
session. Test target-specific audio device indices again after moving machines.

## Uninstall

Run `scripts/uninstall.ps1 -Root '<root>' -KeepMemory` to preserve memory, settings,
exports and backups, or omit `-KeepMemory` to remove those known application files too.
The script asks for deletion confirmation, removes inventoried files, and leaves unknown
files/nonempty cache directories for inspection. It never recursively deletes the root
or an arbitrary parent folder. No external startup entry or service needs removal.

## Target acceptance

Setup automatically runs `scripts/run.py --root <root> --smoke` using private Python.
For the fuller non-recording model/vision/voice checks, run `scripts/validate_integration.py`
with the same private Python after closing the app. See `VERIFICATION.md` for the
remaining physical-device and Windows checklist. No validation tool writes input/output
audio recordings, screenshots of the user's desktop, or video.
