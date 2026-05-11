from __future__ import annotations

import argparse

from .models import AiPhotoshopAgentConfig
from .server import AiPhotoshopAgentServer


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AI Photoshop Agent")
    parser.add_argument("--root-dir", default=".", help="Workspace root for token and state files.")
    parser.add_argument("--bridge-dir", required=True, help="Shared bridge directory used by the companion service and the Photoshop plugin.")
    parser.add_argument("--host", default="127.0.0.1", help="HTTP bind host.")
    parser.add_argument("--port", type=int, default=19794, help="HTTP bind port.")
    parser.add_argument("--no-token", action="store_true", help="Disable token authentication.")
    parser.add_argument("--full-access", action="store_true", help="Enable high-risk mutation tools.")
    parser.add_argument("--tool-timeout-seconds", type=int, default=120, help="Per-tool timeout in seconds.")
    parser.add_argument("--poll-interval-seconds", type=float, default=0.5, help="Bridge poll interval in seconds.")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    config = AiPhotoshopAgentConfig(
        root_dir=args.root_dir,
        bridge_dir=args.bridge_dir,
        host=args.host,
        port=args.port,
        require_token=not args.no_token,
        full_access_enabled=args.full_access,
        tool_timeout_seconds=args.tool_timeout_seconds,
        poll_interval_seconds=args.poll_interval_seconds,
    )
    server = AiPhotoshopAgentServer(config)
    print(f"AI Photoshop Agent listening on {config.server_url}")
    if config.require_token:
        print(f"Service token file: {config.token_file_path}")
    print(f"Bridge directory: {config.bridge_dir}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.stop()


if __name__ == "__main__":
    main()
