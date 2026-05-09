# AI Android Agent

AI Android Agent is the Android adapter for the shared AI Platform Agent Framework.

It exposes the same discovery-first protocol as the other adapters and focuses on the official Android tooling surface:

- ADB devices, packages, app launch, and log capture
- Gradle project inspection, task listing, builds, and tests
- Android Emulator / AVD discovery and controlled emulator startup

## Start

```bash
python -m ai_android_agent.cli --root-dir /path/to/workspace
```

Useful flags:

- `--port 19787`
- `--no-token`
- `--full-access`
- `--adb-executable adb`
- `--gradle-executable gradle`
- `--emulator-executable emulator`
- `--project-dir /path/to/android/project`
- `--android-sdk-root /path/to/Android/sdk`

By default the service token is stored at:

```text
<root-dir>/.ai_platform_agent/android/token.txt
```
