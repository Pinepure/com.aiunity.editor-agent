from __future__ import annotations

import argparse

from .models import AiFigmaAgentConfig
from .server import AiFigmaAgentServer


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AI Figma Agent")
    parser.add_argument("--root-dir", default=".", help="Workspace root for token and state files.")
    parser.add_argument("--host", default="127.0.0.1", help="HTTP bind host.")
    parser.add_argument("--port", type=int, default=19783, help="HTTP bind port.")
    parser.add_argument("--no-token", action="store_true", help="Disable token authentication.")
    parser.add_argument("--full-access", action="store_true", help="Enable high-risk mutation tools.")
    parser.add_argument("--figma-base-url", default="https://api.figma.com", help="Figma REST API base URL.")
    parser.add_argument("--figma-token", default="", help="Figma REST API token. Defaults to FIGMA_TOKEN or the persisted adapter token file.")
    parser.add_argument("--tool-timeout-seconds", type=int, default=120, help="Per-tool timeout in seconds.")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    config = AiFigmaAgentConfig(
        root_dir=args.root_dir,
        host=args.host,
        port=args.port,
        require_token=not args.no_token,
        full_access_enabled=args.full_access,
        figma_base_url=args.figma_base_url,
        figma_token=args.figma_token,
        tool_timeout_seconds=args.tool_timeout_seconds,
    )
    server = AiFigmaAgentServer(config)
    print(f"AI Figma Agent listening on {config.server_url}")
    if config.require_token:
        print(f"Service token file: {config.token_file_path}")
    print(f"Figma API token file: {config.figma_token_file_path}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.stop()


if __name__ == "__main__":
    main()
