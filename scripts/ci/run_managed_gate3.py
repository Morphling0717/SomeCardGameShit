#!/usr/bin/env python3
"""Run the locked Gate 3 managed restore, build, and test contract."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
GODOT_PROJECT = ROOT / "client/godot/SomeCardGameShit.csproj"
NATIVE_LIBRARY_ENVIRONMENTS = ("SCGS_NATIVE_LIBRARY", "SCGS_V04_NATIVE_PATH")


def _run(*arguments: str) -> None:
    subprocess.run(arguments, cwd=ROOT, check=True)


def main() -> int:
    native_paths = {
        name: os.environ.get(name, "").strip()
        for name in NATIVE_LIBRARY_ENVIRONMENTS
    }
    missing_environments = [name for name, value in native_paths.items() if not value]
    if missing_environments:
        print(
            "native integration tests are mandatory; missing environment variable(s): "
            + ", ".join(missing_environments),
            file=sys.stderr,
        )
        return 1

    resolved_native_paths = {
        name: Path(value).expanduser().resolve()
        for name, value in native_paths.items()
    }
    if len(set(resolved_native_paths.values())) != 1:
        rendered = ", ".join(
            f"{name}={path}" for name, path in resolved_native_paths.items()
        )
        print(f"native library environment variables disagree: {rendered}", file=sys.stderr)
        return 1
    native_library = next(iter(resolved_native_paths.values()))
    if not native_library.is_file():
        print(f"native integration library does not exist: {native_library}", file=sys.stderr)
        return 1

    global_config = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
    expected_sdk = global_config["sdk"]["version"]
    test_runner = global_config.get("test", {}).get("runner")
    if test_runner != "Microsoft.Testing.Platform":
        print(
            "global.json must select the Microsoft.Testing.Platform test runner",
            file=sys.stderr,
        )
        return 1
    actual_sdk = subprocess.run(
        ["dotnet", "--version"],
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    if actual_sdk != expected_sdk:
        print(
            f"global.json requires .NET SDK {expected_sdk}, found {actual_sdk}",
            file=sys.stderr,
        )
        return 1

    if not GODOT_PROJECT.is_file():
        print(f"missing Godot project: {GODOT_PROJECT}", file=sys.stderr)
        return 1
    test_projects = sorted(
        path
        for path in (ROOT / "client").rglob("*Tests.csproj")
        if "obj" not in path.parts and "bin" not in path.parts
    )
    if not test_projects:
        print("no managed *Tests.csproj project was found", file=sys.stderr)
        return 1

    restore_projects = [GODOT_PROJECT, *test_projects]
    for project in restore_projects:
        lock_file = project.with_name("packages.lock.json")
        if not lock_file.is_file():
            print(f"locked restore requires {lock_file}", file=sys.stderr)
            return 1
        _run("dotnet", "restore", str(project), "--locked-mode", "--nologo")

    _run(
        "dotnet",
        "build",
        str(GODOT_PROJECT),
        "--configuration",
        "Release",
        "--no-restore",
        "--nologo",
    )
    for project in test_projects:
        _run(
            "dotnet",
            "test",
            "--project",
            str(project),
            "--configuration",
            "Release",
            "--no-restore",
            "--minimum-expected-tests",
            "1",
        )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.returncode) from error
