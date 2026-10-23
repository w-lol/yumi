# Yumi

Local Windows desktop companion. See [installation](docs/INSTALL.md),
[architecture](docs/ARCHITECTURE.md), and [verification](docs/VERIFICATION.md).

Original Unity avatar integration: [status, inspection tools and localhost protocol](docs/UNITY_INTEGRATION.md).
The original Unity character is the target; its project must be available for inspection
before a compatible overlay/player can be built. The existing avatar remains usable.

Run: `python -m yumi --root <installation-directory>`.
Tests: `python -m unittest discover -s tests -v`.

Download a Windows installer from **Actions → Windows installer → a successful run →
Artifacts → Yumi-Windows-installer**. Extract the artifact ZIP to obtain `Yumi.zip`,
then follow [installation](docs/INSTALL.md). Pull requests and pushes to `main` build
artifacts automatically. After merging, **Run workflow** on `main` can optionally
publish a prerelease by selecting `publish_release` and supplying a new release tag.

The workflow packages the existing PowerShell setup bundle; runtime and model downloads
happen during setup. CI checks Windows native imports, unit tests, PowerShell syntax,
and archive integrity. It does not claim GPU or physical-device acceptance.
