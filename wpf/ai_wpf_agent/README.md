# AI WPF Agent

AI WPF Agent is the WPF adapter for the shared AI Platform Agent Framework.

It focuses on the official solution, build, and XAML surfaces that are stable across environments:

- `.sln` and `.csproj` inspection
- XAML file enumeration, search, readback, and controlled attribute mutation
- `dotnet` / `msbuild` builds and tests

Current implementation status:

- Implemented as a local Python HTTP service
- Uses stable filesystem, solution, XAML, `dotnet`, and `msbuild` surfaces
- Does not depend on brittle GUI automation
- Does not yet support Unity-style dynamic generated tool registration
- Best suited for Windows machines that already have the target WPF project plus `dotnet` and `msbuild`

## Start

```bash
python -m ai_wpf_agent.cli --root-dir /path/to/workspace
```

Useful flags:

- `--port 19788`
- `--no-token`
- `--full-access`
- `--dotnet-executable dotnet`
- `--msbuild-executable msbuild`
- `--solution-path /path/to/App.sln`
- `--project-dir /path/to/project`

You can also install the package entrypoint first:

```bash
python -m pip install .
ai-wpf-agent --root-dir C:\path\to\workspace --solution-path C:\path\to\App.sln --full-access
```

Default token file:

```text
<root-dir>/.ai_platform_agent/wpf/token.txt
```

## What a human user should do

1. Install Python 3.11+ on the Windows machine.
2. Ensure `dotnet` and, if needed, `msbuild` are available in `PATH`.
3. Unzip this package and run `python -m pip install .` inside `wpf/ai_wpf_agent/`.
4. Start the adapter with `--root-dir` and either `--solution-path` or `--project-dir`.
5. Keep the service running locally.
6. Give the AI client the local base URL and token file path.

## What the AI client should do

The AI side should not guess WPF capabilities. It should use the adapter protocol:

1. `GET /health`
2. Cache `manifestHash`
3. `POST /manifest/search`
4. `POST /tool/describe_many`
5. `POST /call/{toolId}`
6. `GET /result/{handleId}` when paging is needed

The built-in AI operating manual is exposed both as:

- local file: `AGENT.md`
- HTTP endpoint: `GET /agent`

That means a capable AI client can learn the adapter workflow directly from the package after connection.

## What this adapter currently lets AI do

- Inspect the configured WPF installation and workspace
- Summarize a solution or project
- List solution projects
- List, search, and read XAML files
- List XAML resource keys
- Run `dotnet build`
- Run `dotnet test`
- Run `msbuild`
- Perform controlled XAML attribute updates when started with `--full-access`

## Minimal integration

For a standalone WPF product, ship only this directory:

- `wpf/ai_wpf_agent/`

Keep these files in the package you send to others:

- `ai_wpf_agent/`
- `bin/`
- `README.md`
- `AGENT.md`
- `pyproject.toml`

Do not bundle Unity, Flutter, Photoshop, or any other platform adapter.
