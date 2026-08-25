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
V04_NATIVE_LIBRARY_ENVIRONMENTS = ("SCGS_NATIVE_LIBRARY", "SCGS_V04_NATIVE_PATH")
V05_NATIVE_LIBRARY_ENVIRONMENT = "SCGS_NATIVE_V05_LIBRARY"
# Backwards-compatible import for existing test helpers which only care about
# the frozen v04 pair.
NATIVE_LIBRARY_ENVIRONMENTS = V04_NATIVE_LIBRARY_ENVIRONMENTS
GODOT_BUILD_CONFIGURATIONS = ("Debug", "Release")


def _run(*arguments: str) -> None:
    subprocess.run(arguments, cwd=ROOT, check=True)


def main() -> int:
    native_paths = {
        name: os.environ.get(name, "").strip()
        for name in (*V04_NATIVE_LIBRARY_ENVIRONMENTS, V05_NATIVE_LIBRARY_ENVIRONMENT)
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
    v04_paths = {
        resolved_native_paths[name]
        for name in V04_NATIVE_LIBRARY_ENVIRONMENTS
    }
    if len(v04_paths) != 1:
        rendered = ", ".join(
            f"{name}={resolved_native_paths[name]}"
            for name in V04_NATIVE_LIBRARY_ENVIRONMENTS
        )
        print(f"v04 native library environment variables disagree: {rendered}", file=sys.stderr)
        return 1
    for name, native_library in resolved_native_paths.items():
        if not native_library.is_file():
            print(
                f"native integration library does not exist: {name}={native_library}",
                file=sys.stderr,
            )
            return 1
    if resolved_native_paths[V05_NATIVE_LIBRARY_ENVIRONMENT] in v04_paths:
        print("v04 and v05 integration libraries must be distinct files", file=sys.stderr)
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

    for configuration in GODOT_BUILD_CONFIGURATIONS:
        _run(
            "dotnet",
            "build",
            str(GODOT_PROJECT),
            "--configuration",
            configuration,
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
