#!/usr/bin/env python3
"""Classify a Git change set for the fast CI workflow.

The classifier deliberately has no GitHub-specific dependency.  It accepts the
event's base/head commits, asks Git for the authoritative changed-path list and
writes small scalar outputs that a workflow can consume.  An empty or
unreadable change set always falls back to the full matrix.
"""

from __future__ import annotations

import argparse
import subprocess
from pathlib import Path, PurePosixPath
from typing import Iterable, Sequence


ZERO_SHA = "0" * 40
EMPTY_TREE_SHA = "4b825dc642cb6eb9a060e54bf8d69288fbee4904"
ROOT_DOCUMENTS = {
    "LICENSE",
    "README.md",
    "TEST_REPORT.md",
    "THIRD_PARTY_NOTICES.md",
}
DOCUMENT_SUFFIXES = {".md", ".mdx", ".rst"}
PACKAGED_DOCUMENTS = {
    "LICENSE",
    "THIRD_PARTY_NOTICES.md",
    "client/godot/ASSET_NOTICES.md",
    "client/godot/assets/fonts/NOTICE.md",
    "client/godot/assets/visual/anime_v1/slice/PROVENANCE.md",
    "client/godot/assets/visual/anime_v1/card_body/PROVENANCE.md",
    "docs/anime-v1-visual-slice.md",
    "docs/anime-v1-card-body-r1.md",
    "docs/native-api-v04.md",
    "docs/native-api-v05.md",
}


class ChangeClassificationError(RuntimeError):
    """Raised when Git cannot describe the requested change set."""


def normalize_path(raw_path: str) -> str:
    """Return a safe repository-relative POSIX path or an empty string."""

    path = raw_path.strip().replace("\\", "/")
    candidate = PurePosixPath(path)
    if not path or candidate.is_absolute() or ".." in candidate.parts:
        return ""
    return candidate.as_posix()


def is_documentation_path(raw_path: str) -> bool:
    """Whether a path can skip compilation and runtime/export validation."""

    path = normalize_path(raw_path)
    if not path:
        return False
    # These documents are copied byte-for-byte into desktop exports and are
    # therefore packaging inputs, not documentation-only changes.
    if path in PACKAGED_DOCUMENTS:
        return False
    if path in ROOT_DOCUMENTS:
        return True
    return PurePosixPath(path).suffix.lower() in DOCUMENT_SUFFIXES


def classify_paths(paths: Iterable[str]) -> tuple[bool, tuple[str, ...]]:
    """Return ``(docs_only, normalized_paths)`` with a fail-closed default."""

    normalized = tuple(path for raw in paths if (path := normalize_path(raw)))
    docs_only = bool(normalized) and all(is_documentation_path(path) for path in normalized)
    return docs_only, normalized


def _git_command(
    *, base: str, head: str, name_only: bool = False, check: bool = False
) -> list[str]:
    if not head:
        raise ChangeClassificationError("head revision is required")
    if name_only == check:
        raise ChangeClassificationError("select exactly one Git operation")

    if not base or base == ZERO_SHA:
        if name_only:
            # A newly-created ref has no meaningful event base.  Listing the
            # whole tree intentionally forces a full build instead of trusting
            # only the tip commit of a pre-existing local branch.
            return ["git", "ls-tree", "-r", "--name-only", head]
        # A newly-created ref may point at a multi-commit branch. Checking the
        # final tree against Git's canonical empty tree covers whitespace left
        # by every commit, rather than only the tip commit's parent diff.
        return ["git", "diff", "--check", EMPTY_TREE_SHA, head]
    if name_only:
        # Rename detection reports only the destination path. Disabling it keeps
        # deleted packaged inputs visible, so a rename cannot bypass full CI.
        return ["git", "diff", "--no-renames", "--name-only", base, head]
    return ["git", "diff", "--check", base, head]


def _run_git(arguments: Sequence[str], repository: Path) -> str:
    completed = subprocess.run(
        arguments,
        cwd=repository,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
    )
    if completed.returncode != 0:
        detail = completed.stderr.strip() or completed.stdout.strip()
        raise ChangeClassificationError(
            f"Git command failed ({completed.returncode}): {' '.join(arguments)}: {detail}"
        )
    return completed.stdout


def changed_paths(base: str, head: str, repository: Path) -> tuple[str, ...]:
    output = _run_git(
        _git_command(base=base, head=head, name_only=True), repository
    )
    return tuple(line for line in output.splitlines() if line.strip())


def check_whitespace(base: str, head: str, repository: Path) -> None:
    output = _run_git(_git_command(base=base, head=head, check=True), repository)
    if output.strip():
        raise ChangeClassificationError(f"whitespace errors detected:\n{output.rstrip()}")


def write_github_outputs(path: Path, docs_only: bool, paths: Sequence[str]) -> None:
    with path.open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(f"docs_only={'true' if docs_only else 'false'}\n")
        stream.write(f"full_ci={'false' if docs_only else 'true'}\n")
        stream.write(f"changed_count={len(paths)}\n")


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="")
    parser.add_argument("--head", required=True)
    parser.add_argument("--repository", type=Path, default=Path.cwd())
    parser.add_argument("--github-output", type=Path)
    parser.add_argument("--check-whitespace", action="store_true")
    args = parser.parse_args(argv)

    repository = args.repository.resolve()
    try:
        paths = changed_paths(args.base, args.head, repository)
        docs_only, normalized = classify_paths(paths)
        if args.check_whitespace:
            check_whitespace(args.base, args.head, repository)
    except ChangeClassificationError as error:
        parser.error(str(error))

    print(f"docs_only={'true' if docs_only else 'false'}")
    print(f"full_ci={'false' if docs_only else 'true'}")
    print(f"changed_count={len(normalized)}")
    for path in normalized:
        print(f"changed_path={path}")
    if args.github_output is not None:
        write_github_outputs(args.github_output, docs_only, normalized)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
