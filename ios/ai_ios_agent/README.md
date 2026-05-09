# AI iOS Agent

AI iOS Agent is the iOS adapter for the shared AI Platform Agent Framework.

It uses the official Apple CLI tooling surface:

- `xcodebuild` for project/workspace inspection, build, and test
- `xcrun simctl` for simulator discovery, boot, shutdown, app install, and app launch
- simulator log extraction through `simctl spawn ... log show`

## Start

```bash
python -m ai_ios_agent.cli --root-dir /path/to/workspace
```

Useful flags:

- `--port 19789`
- `--no-token`
- `--full-access`
- `--xcodebuild-executable xcodebuild`
- `--xcrun-executable xcrun`
- `--project-path /path/to/App.xcodeproj`
- `--workspace-path /path/to/App.xcworkspace`
- `--scheme App`
