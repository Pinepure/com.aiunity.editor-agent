# AI WPF Agent

AI WPF Agent is the WPF adapter for the shared AI Platform Agent Framework.

It focuses on the official solution, build, and XAML surfaces that are stable across environments:

- `.sln` and `.csproj` inspection
- XAML file enumeration, search, readback, and controlled attribute mutation
- `dotnet` / `msbuild` builds and tests

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
