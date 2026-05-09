from __future__ import annotations

import argparse

from .models import AiAndroidAgentConfig
from .server import AiAndroidAgentServer


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AI Android Agent")
    parser.add_argument("--root-dir", default=".", help="Workspace root for token and state files.")
    parser.add_argument("--host", default="127.0.0.1", help="HTTP bind host.")
    parser.add_argument("--port", type=int, default=19787, help="HTTP bind port.")
    parser.add_argument("--no-token", action="store_true", help="Disable token authentication.")
    parser.add_argument("--full-access", action="store_true", help="Enable high-risk mutation tools.")
    parser.add_argument("--adb-executable", default="adb", help="ADB executable path.")
    parser.add_argument("--gradle-executable", default="gradle", help="Fallback Gradle executable when no wrapper exists.")
    parser.add_argument("--emulator-executable", default="emulator", help="Android emulator executable path.")
    parser.add_argument("--project-dir", default="", help="Default Android project directory for Gradle-backed calls.")
    parser.add_argument("--android-sdk-root", default="", help="Android SDK root override.")
    parser.add_argument("--tool-timeout-seconds", type=int, default=300, help="Per-tool timeout in seconds.")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    config = AiAndroidAgentConfig(root_dir=args.root_dir, host=args.host, port=args.port, require_token=not args.no_token, full_access_enabled=args.full_access, adb_executable=args.adb_executable, gradle_executable=args.gradle_executable, emulator_executable=args.emulator_executable, project_dir=args.project_dir, android_sdk_root=args.android_sdk_root, tool_timeout_seconds=args.tool_timeout_seconds)
    server = AiAndroidAgentServer(config)
    print(f"AI Android Agent listening on {config.server_url}")
    if config.require_token:
        print(f"Token file: {config.token_file_path}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.stop()


if __name__ == "__main__":
    main()
