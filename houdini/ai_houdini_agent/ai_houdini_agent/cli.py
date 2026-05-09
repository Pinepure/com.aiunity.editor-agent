from __future__ import annotations

import argparse

from .models import AiHoudiniAgentConfig
from .server import AiHoudiniAgentServer


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AI Houdini Agent")
    parser.add_argument("--root-dir", default=".", help="Workspace root for token and state files.")
    parser.add_argument("--host", default="127.0.0.1", help="HTTP bind host.")
    parser.add_argument("--port", type=int, default=19779, help="HTTP bind port.")
    parser.add_argument("--no-token", action="store_true", help="Disable token authentication.")
    parser.add_argument("--full-access", action="store_true", help="Enable high-risk mutation tools.")
    parser.add_argument("--hython-executable", default="hython", help="Houdini hython executable path.")
    parser.add_argument("--default-hip-file", default="", help="Default .hip file for calls that omit hipFile.")
    parser.add_argument("--tool-timeout-seconds", type=int, default=180, help="Per-tool timeout in seconds.")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    config = AiHoudiniAgentConfig(
        root_dir=args.root_dir,
        host=args.host,
        port=args.port,
        require_token=not args.no_token,
        full_access_enabled=args.full_access,
        hython_executable=args.hython_executable,
        default_hip_file=args.default_hip_file,
        tool_timeout_seconds=args.tool_timeout_seconds,
    )
    server = AiHoudiniAgentServer(config)
    print(f"AI Houdini Agent listening on {config.server_url}")
    if config.require_token:
        print(f"Token file: {config.token_file_path}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.stop()


if __name__ == "__main__":
    main()
