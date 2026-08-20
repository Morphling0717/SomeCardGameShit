#!/usr/bin/env python3
"""Apply the tested SCGS protocol overlay to a pinned YGOProUnity_V2 checkout."""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path

PINNED_YGOPRO2_REVISION = "b90f5bbdb0ae60df4060152b94e25a60783040b8"
ENUM_RELATIVE_PATH = Path("Assets/YGOSharp/Enums/GameMessage.cs")
OVERLAY_RELATIVE_PATH = Path("client/YGOPro2Overlay/Assets/SomeCardGame")
DESTINATION_RELATIVE_PATH = Path("Assets/SomeCardGame")

SCGS_ENUM_BLOCK = """        ScgsGameMode = 210,
        ScgsPlayerState = 211,
        ScgsUnitState = 212,
        ScgsEvolutionState = 213,
        ScgsAdvancedSummonState = 214,
        ScgsRequestEvolutionMode = 215,
        ScgsRequestMaterials = 216,
        ScgsRequestImprint = 217,
        ScgsTacticWindow = 218,
        ScgsMatchStatistics = 219,
"""


def inject_message_ids(source: str) -> str:
    """Return GameMessage.cs with the SCGS range inserted exactly once."""
    if "ScgsGameMode = 210" in source:
        for expected in range(210, 220):
            if f"= {expected}," not in source:
                raise ValueError("existing SCGS message block is incomplete")
        return source

    collisions = []
    for match in re.finditer(r"^\s*[A-Za-z_][A-Za-z0-9_]*\s*=\s*(\d+)\s*,", source, re.MULTILINE):
        value = int(match.group(1))
        if 210 <= value <= 219:
            collisions.append(value)
    if collisions:
        raise ValueError("YGOPro2 already uses SCGS message ids: " + ", ".join(map(str, collisions)))

    marker = "        sibyl_chat = 230,"
    if source.count(marker) != 1:
        raise ValueError("could not find the expected sibyl_chat enum marker exactly once")
    return source.replace(marker, SCGS_ENUM_BLOCK + marker)


def git_revision(checkout: Path) -> str | None:
    if not (checkout / ".git").exists():
        return None
    process = subprocess.run(
        ["git", "-C", str(checkout), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    )
    return process.stdout.strip()


def apply_overlay(project_root: Path, checkout: Path, *, check_only: bool, allow_revision_mismatch: bool) -> None:
    project_root = project_root.resolve()
    checkout = checkout.resolve()
    enum_path = checkout / ENUM_RELATIVE_PATH
    overlay_path = project_root / OVERLAY_RELATIVE_PATH
    destination = checkout / DESTINATION_RELATIVE_PATH

    if not enum_path.is_file():
        raise FileNotFoundError(f"missing pinned YGOPro2 enum file: {enum_path}")
    if not overlay_path.is_dir():
        raise FileNotFoundError(f"missing SCGS overlay source: {overlay_path}")

    revision = git_revision(checkout)
    if revision is not None and revision != PINNED_YGOPRO2_REVISION and not allow_revision_mismatch:
        raise RuntimeError(
            f"YGOPro2 checkout is {revision}, expected {PINNED_YGOPRO2_REVISION}; "
            "review upstream changes before using --allow-revision-mismatch"
        )

    original = enum_path.read_text(encoding="utf-8-sig")
    patched = inject_message_ids(original)

    if check_only:
        print(f"overlay is applicable to {checkout}")
        return

    enum_path.write_text(patched, encoding="utf-8-sig", newline="")
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(overlay_path, destination)
    print(f"patched message ids: {enum_path}")
    print(f"copied overlay: {destination}")
    print("next required step: route SCGS package payloads from Ocgcore.logicalizeMessage into ScgsStateStore")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("checkout", type=Path, help="path to the YGOProUnity_V2 checkout")
    parser.add_argument(
        "--project-root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="SomeCardGameShit repository root",
    )
    parser.add_argument("--check", action="store_true", help="validate without changing files")
    parser.add_argument(
        "--allow-revision-mismatch",
        action="store_true",
        help="apply to a different upstream revision after manually reviewing conflicts",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    arguments = parse_args(sys.argv[1:] if argv is None else argv)
    try:
        apply_overlay(
            arguments.project_root,
            arguments.checkout,
            check_only=arguments.check,
            allow_revision_mismatch=arguments.allow_revision_mismatch,
        )
    except (OSError, RuntimeError, ValueError, subprocess.CalledProcessError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
