from __future__ import annotations

import argparse

from .models import AiGodotAgentConfig
from .server import AiGodotAgentServer


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AI Godot Agent")
    parser.add_argument("--root-dir", default=".", help="Workspace root for token and state files.")
    parser.add_argument("--host", default="127.0.0.1", help="HTTP bind host.")
    parser.add_argument("--port", type=int, default=19781, help="HTTP bind port.")
    parser.add_argument("--no-token", action="store_true", help="Disable token authentication.")
    parser.add_argument("--full-access", action="store_true", help="Enable high-risk mutation tools.")
    parser.add_argument("--godot-executable", default="godot", help="Godot executable path.")
    parser.add_argument("--project-dir", default="", help="Default Godot project directory for scene-backed calls.")
    parser.add_argument("--tool-timeout-seconds", type=int, default=180, help="Per-tool timeout in seconds.")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    config = AiGodotAgentConfig(
        root_dir=args.root_dir,
        host=args.host,
        port=args.port,
        require_token=not args.no_token,
        full_access_enabled=args.full_access,
        godot_executable=args.godot_executable,
        project_dir=args.project_dir,
        tool_timeout_seconds=args.tool_timeout_seconds,
    )
    server = AiGodotAgentServer(config)
    print(f"AI Godot Agent listening on {config.server_url}")
    if config.require_token:
        print(f"Token file: {config.token_file_path}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.stop()


if __name__ == "__main__":
    main()
