from __future__ import annotations

import argparse

from .models import AiWpfAgentConfig
from .server import AiWpfAgentServer


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AI WPF Agent")
    parser.add_argument("--root-dir", default=".", help="Workspace root for token and state files.")
    parser.add_argument("--host", default="127.0.0.1", help="HTTP bind host.")
    parser.add_argument("--port", type=int, default=19788, help="HTTP bind port.")
    parser.add_argument("--no-token", action="store_true", help="Disable token authentication.")
    parser.add_argument("--full-access", action="store_true", help="Enable high-risk mutation tools.")
    parser.add_argument("--dotnet-executable", default="dotnet", help="dotnet executable path.")
    parser.add_argument("--msbuild-executable", default="msbuild", help="msbuild executable path.")
    parser.add_argument("--solution-path", default="", help="Default .sln file or solution directory.")
    parser.add_argument("--project-dir", default="", help="Default project directory.")
    parser.add_argument("--tool-timeout-seconds", type=int, default=300, help="Per-tool timeout in seconds.")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    config = AiWpfAgentConfig(root_dir=args.root_dir, host=args.host, port=args.port, require_token=not args.no_token, full_access_enabled=args.full_access, dotnet_executable=args.dotnet_executable, msbuild_executable=args.msbuild_executable, solution_path=args.solution_path, project_dir=args.project_dir, tool_timeout_seconds=args.tool_timeout_seconds)
    server = AiWpfAgentServer(config)
    print(f"AI WPF Agent listening on {config.server_url}")
    if config.require_token:
        print(f"Token file: {config.token_file_path}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.stop()


if __name__ == "__main__":
    main()
