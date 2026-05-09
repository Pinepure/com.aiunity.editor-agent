from __future__ import annotations

import argparse

from .models import AiUnrealAgentConfig
from .server import AiUnrealAgentServer


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AI Unreal Agent")
    parser.add_argument("--root-dir", default=".", help="Workspace root for token and state files.")
    parser.add_argument("--host", default="127.0.0.1", help="HTTP bind host.")
    parser.add_argument("--port", type=int, default=19780, help="HTTP bind port.")
    parser.add_argument("--no-token", action="store_true", help="Disable token authentication.")
    parser.add_argument("--full-access", action="store_true", help="Enable high-risk mutation tools.")
    parser.add_argument("--unreal-executable", default="UnrealEditor", help="Unreal Editor executable path.")
    parser.add_argument("--project-file", default="", help="Default .uproject file for Python-backed calls.")
    parser.add_argument("--remote-control-url", default="http://127.0.0.1:30010", help="Remote Control API base URL.")
    parser.add_argument("--tool-timeout-seconds", type=int, default=300, help="Per-tool timeout in seconds.")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    config = AiUnrealAgentConfig(
        root_dir=args.root_dir,
        host=args.host,
        port=args.port,
        require_token=not args.no_token,
        full_access_enabled=args.full_access,
        unreal_executable=args.unreal_executable,
        project_file=args.project_file,
        remote_control_url=args.remote_control_url,
        tool_timeout_seconds=args.tool_timeout_seconds,
    )
    server = AiUnrealAgentServer(config)
    print(f"AI Unreal Agent listening on {config.server_url}")
    if config.require_token:
        print(f"Token file: {config.token_file_path}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.stop()


if __name__ == "__main__":
    main()
