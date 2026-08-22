#!/usr/bin/env python3
"""Run a subprocess with bounded time and optional output contracts."""

from __future__ import annotations

import argparse
import os
import signal
import subprocess
import sys
from pathlib import Path


def _terminate_tree(process: subprocess.Popen[bytes]) -> None:
    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass


def _write_process_output(output_bytes: bytes) -> None:
    """Forward child output without re-encoding UTF-8 through a legacy console."""
    binary_stdout = getattr(sys.stdout, "buffer", None)
    if binary_stdout is not None:
        binary_stdout.write(output_bytes)
        binary_stdout.flush()
        return

    print(output_bytes.decode("utf-8", errors="replace"), end="", flush=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--timeout", type=float, required=True)
    parser.add_argument("--cwd", type=Path)
    parser.add_argument("--expect-output")
    parser.add_argument("--forbid-output", action="append", default=[])
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()

    command = args.command
    if command and command[0] == "--":
        command = command[1:]
    if not command:
        parser.error("a command is required after --")
    if args.timeout <= 0:
        parser.error("--timeout must be positive")

    cwd = args.cwd.resolve(strict=True) if args.cwd else None
    creation_flags = (
        subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0
    )
    process = subprocess.Popen(
        command,
        cwd=cwd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        creationflags=creation_flags,
        start_new_session=os.name != "nt",
    )
    try:
        output_bytes, _ = process.communicate(timeout=args.timeout)
    except subprocess.TimeoutExpired:
        _terminate_tree(process)
        output_bytes, _ = process.communicate()
        output = output_bytes.decode("utf-8", errors="replace")
        _write_process_output(output_bytes)
        print(
            f"command timed out after {args.timeout:g}s: {command[0]}",
            file=sys.stderr,
        )
        return 124

    output = output_bytes.decode("utf-8", errors="replace")
    _write_process_output(output_bytes)
    if process.returncode != 0:
        print(f"command exited with status {process.returncode}", file=sys.stderr)
        return process.returncode if process.returncode > 0 else 1

    forbidden = [pattern for pattern in args.forbid_output if pattern in output]
    if forbidden:
        print(f"forbidden output found: {forbidden}", file=sys.stderr)
        return 1
    if args.expect_output and args.expect_output not in output:
        print(
            f"required output marker not found: {args.expect_output}",
            file=sys.stderr,
        )
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
