#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Audit and ZIP an existing Windows review export; never run Godot.

This does not rebuild the export or prove that its bytes came from a commit.
It preserves the original export and normal product launch. Actual launch and
GPU/user acceptance remain separate, mandatory evidence.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

from audit_godot_export import (  # noqa: E402
    _audit_font_source, _audit_product_card_export_policy, _audit_windows,
)
from audit_visual_assets import audit as audit_visual_assets  # noqa: E402
from check_card_frame_r1_assets import audit as audit_card_frame  # noqa: E402
from dev.check_godot_mcp_export import check_export, check_pck  # noqa: E402

LAUNCHER = "PLAY_BATTLE_PRESENTATION_REVIEW.cmd"
PACKAGE_MANIFEST = "REVIEW_PACKAGE.json"
PACKAGE_README = "REVIEW_README.txt"
ENTRIES = {
    "battle-presentation": {
        "argument": "--battle-presentation-review", "launcher": LAUNCHER,
        "kind": "battle-presentation-v2-stage1-windows-review",
        "archive": "SomeCardGameShit-battle-presentation-v2-stage1-windows-x86_64-review.zip",
        "title": "战斗表现 V2 第一阶段：独立实机验收候选",
        "scope": "本阶段不代表所有卡牌演出完成，素材与演出等待用户视觉批准。",
    },
    "card-frame-r1": {
        "argument": "--card-frame-review", "launcher": "PLAY_CARD_FRAME_R1_REVIEW.cmd",
        "kind": "card-frame-r1-windows-review",
        "archive": "SomeCardGameShit-card-frame-r1-windows-x86_64-review.zip",
        "title": "卡框精修 R1：独立实机验收候选",
        "scope": "仅验收三张代表卡的新实体卡框、宝石、完整卡名和数字排版；"
                 "不代表所有卡牌换装，也不将沿用的基础动作计为新增演出。",
    },
}
FRAME_ROOT = "assets/visual/anime_v1/card_frame_r1/"
FRAME_MODELS = ("frame-master.glb", "frame-master-low.glb")
FRAME_TEXTURES = ("platinum-albedo-source.png", "relief-normal.png", "relief-ao.png", "relief-roughness.png")
FRAME_MANIFEST = "art/card_frame_r1/frame-manifest.json"


class ReviewPackageError(ValueError):
    pass


def _entry(name: str) -> dict[str, str]:
    if name not in ENTRIES:
        raise ReviewPackageError(f"unknown review entry: {name}")
    return ENTRIES[name]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _source_identity(repo: Path, allow_worktree: bool) -> dict[str, object]:
    def git(*arguments: str) -> str:
        return subprocess.run(["git", *arguments], cwd=repo, check=True,
                              capture_output=True, text=True).stdout.strip()
    head = git("rev-parse", "HEAD")
    if not re.fullmatch(r"[0-9a-f]{40}", head):
        raise ReviewPackageError("cannot establish source base commit")
    dirty = bool(git("status", "--porcelain", "--untracked-files=normal"))
    if dirty and not allow_worktree:
        raise ReviewPackageError("dirty workspace: pass --allow-worktree to label an uncommitted review candidate")
    return {"base_commit": head, "worktree_dirty": dirty,
            "export_provenance": "operator_supplied_export_not_rebuilt_by_packager"}


def _launcher(executable_name: str, identity: dict[str, object], *, entry: str = "battle-presentation") -> str:
    selected = _entry(entry)
    if not re.fullmatch(r"[A-Za-z0-9_.-]+\.exe", executable_name, re.IGNORECASE):
        raise ReviewPackageError("review executable filename must be shell-safe ASCII")
    # Do not pass a clean-source claim for a dirty build. The review screen then
    # retains its explicit unverified-workspace label; the JSON records its base.
    source_argument = ("" if identity["worktree_dirty"] else
                       f" --review-source-sha={identity['base_commit']}")
    return ("@echo off\r\nsetlocal\r\n"
            f'start "" "%~dp0{executable_name}" -- {selected["argument"]}{source_argument}\r\n'
            "endlocal\r\n")


def _audit_finalized(export: Path) -> None:
    _audit_font_source()
    audit_visual_assets(ROOT)
    _audit_product_card_export_policy()
    _audit_windows(export)
    check_export(export.parent)


def _audit_card_frame_export(export: Path) -> dict[str, object]:
    source_audit = audit_card_frame(ROOT)
    entries: set[str] = set()

    def inspect(name: str) -> None:
        lower = name.lower()
        if re.search(r"\.blend\d*$", lower) or lower.startswith(("art/", "scripts/art/")):
            raise ReviewPackageError(f"Blender/development source leaked into player PCK: {name}")
        entries.add(name)

    check_pck(export.with_suffix(".pck"), check_entry=inspect)
    for name in (*FRAME_MODELS, *FRAME_TEXTURES):
        resource = FRAME_ROOT + name
        if resource in entries:
            continue
        if not any(resource + suffix in entries for suffix in (".import", ".remap")):
            raise ReviewPackageError(f"R1 player pack is missing resource entry: {resource}")
        extension = "scn" if name.endswith(".glb") else "ctex"
        pattern = rf"\.godot/imported/{re.escape(name)}-[0-9a-f]{{32}}(?:\.[a-z0-9_]+)?\.{extension}"
        if not any(re.fullmatch(pattern, path) for path in entries):
            raise ReviewPackageError(f"R1 resource entry has no imported payload: {resource}")
    for path in export.parent.rglob("*"):
        if path.is_file() and re.search(r"\.blend\d*$", path.name, re.IGNORECASE):
            raise ReviewPackageError("Blender source must not be included in the player directory")
    return {"source_model_manifest_sha256": source_audit["manifest_sha256"],
            "source_models": source_audit["models"], "packed_model_entries": list(FRAME_MODELS),
            "packed_texture_entries": list(FRAME_TEXTURES),
            "packed_importer_bytes_match_source_claimed": False,
            "gpu_or_user_visual_approval_claimed": False}


def _source_files(export: Path) -> list[Path]:
    directory = export.parent
    data = [item for item in directory.iterdir() if item.is_dir() and item.name.startswith("data_")]
    if len(data) != 1:
        raise ReviewPackageError("expected exactly one managed data directory")
    allowed = {export.name, export.with_suffix(".pck").name,
               export.with_suffix(".console.exe").name, "scgs_v05.dll", "licenses", data[0].name}
    unexpected = [item.name for item in directory.iterdir() if item.name not in allowed]
    if unexpected:
        raise ReviewPackageError(f"unexpected export-root files (do not package diagnostics/private data): {unexpected}")
    files = []
    for item in directory.rglob("*"):
        if item.is_symlink() or (hasattr(item, "is_junction") and item.is_junction()):
            raise ReviewPackageError("export links/junctions are not allowed")
        if item.is_file():
            relative = item.relative_to(directory)
            if any(part.lower() in {".mcp.json", "__mcp_probe", "godot_mcp_toolkit", "review-evidence"}
                   for part in relative.parts):
                raise ReviewPackageError("development/private review data is not distributable")
            files.append(item)
    return sorted(files)


def package(export: Path, native_library: Path, output: Path, *, allow_worktree: bool = False,
            entry: str = "battle-presentation") -> dict[str, object]:
    selected = _entry(entry)
    export = export.resolve(strict=True)
    native_library = native_library.resolve(strict=True)
    output = output.resolve()
    if output.suffix.lower() != ".zip" or output.exists():
        raise ReviewPackageError("output must be a new .zip; existing packages are never overwritten")
    if output.is_relative_to(export.parent):
        raise ReviewPackageError("ZIP must be outside the source export directory")
    identity = _source_identity(ROOT, allow_worktree)
    _audit_finalized(export)
    frame_audit = _audit_card_frame_export(export) if entry == "card-frame-r1" else None
    if _sha256(export.parent / "scgs_v05.dll") != _sha256(native_library):
        raise ReviewPackageError("export v05 DLL differs from the explicit current build")
    sources = _source_files(export)
    launcher = _launcher(export.name, identity, entry=entry)
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="scgs-review-package-", dir=output.parent) as temporary:
        temporary_root = Path(temporary)
        staged = temporary_root / "package"
        staged.mkdir()
        for source in sources:
            destination = staged / source.relative_to(export.parent)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
        (staged / selected["launcher"]).write_bytes(launcher.encode("ascii"))
        if frame_audit is not None:
            # Provenance only: editable .blend/concept/generator stay outside the player.
            shutil.copy2(ROOT / FRAME_MANIFEST, staged / "licenses/CARD_FRAME_R1_MODEL_MANIFEST.json")
        readme = (
            selected["title"] + "\n"
            "implementation in progress / visual pending user approval\n\n"
            f"请先解压整个 ZIP，再双击 {selected['launcher']}。\n"
            "三入口为 LO-11、AP-11、NT-04；使用真实规则准备场面。\n"
            "请主动揭示后亲手出牌、选择格位/目标或进化；没有自动替您操作。\n"
            f"直接运行 {export.name} 仍打开正常产品菜单。\n"
            + selected["scope"] + "\n"
            "此包未正式签名；打包审计不等于已经实际启动或通过 GPU 验收。\n"
            "若验收入口不可见或有错误，请反馈；不要把旧主菜单当成新演出验收。\n"
            "个人验收轨迹保存在 Godot user://review-evidence，可能包含私密对局选择，\n"
            "不会打入此包；分享前请自行确认内容。\n\n"
            f"source_base_commit={identity['base_commit']}\n"
            f"worktree_dirty={str(identity['worktree_dirty']).lower()}\n"
            "来源声明只是打包时工作区状态，不证明既有导出等于某个干净提交；\n"
            "请保留独立构建/导出日志。所有包内文件哈希见 REVIEW_PACKAGE.json。\n"
        )
        (staged / PACKAGE_README).write_text(readme, encoding="utf-8-sig", newline="\n")
        manifest: dict[str, object] = {
            "schema_version": 1, "kind": selected["kind"],
            "status": "implementation_in_progress_pending_visual_user_approval",
            "source": identity, "executable": export.name, "launcher": selected["launcher"],
            "launch_arguments": ["--", selected["argument"]] + (
                [] if identity["worktree_dirty"] else [f"--review-source-sha={identity['base_commit']}"]
            ),
            "native_sha256": _sha256(native_library), "runtime_launched_by_packager": False,
            "files": [{"path": file.relative_to(staged).as_posix(), "sha256": _sha256(file)}
                      for file in sorted(staged.rglob("*")) if file.is_file()],
        }
        if frame_audit is not None:
            manifest["card_frame_r1_audit"] = frame_audit
            manifest["recommended_archive_name"] = selected["archive"]
        (staged / PACKAGE_MANIFEST).write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
                                              encoding="utf-8", newline="\n")
        _audit_finalized(staged / export.name)
        if frame_audit is not None:
            _audit_card_frame_export(staged / export.name)
        candidate = temporary_root / "candidate.zip"
        with zipfile.ZipFile(candidate, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for item in sorted(staged.rglob("*")):
                if item.is_file():
                    info = zipfile.ZipInfo(item.relative_to(staged).as_posix(), date_time=(2026, 9, 6, 0, 0, 0))
                    info.compress_type = zipfile.ZIP_DEFLATED
                    info.external_attr = 0o100644 << 16
                    archive.writestr(info, item.read_bytes())
        unpacked = temporary_root / "roundtrip"
        with zipfile.ZipFile(candidate) as archive:
            if archive.testzip() is not None:
                raise ReviewPackageError("ZIP CRC round-trip failed")
            archive.extractall(unpacked)  # Only our newly authored normalized paths.
        _audit_finalized(unpacked / export.name)
        if frame_audit is not None:
            _audit_card_frame_export(unpacked / export.name)
        for entry in manifest["files"]:
            if _sha256(unpacked / entry["path"]) != entry["sha256"]:
                raise ReviewPackageError("ZIP file-hash round-trip failed")
        # Exclusive creation also rejects a package created by another process
        # after the early existence check. Never replace the user's ZIP.
        with candidate.open("rb") as source, output.open("xb") as destination:
            shutil.copyfileobj(source, destination)
    return {"path": str(output), "sha256": _sha256(output), "source": identity,
            "runtime_launched": False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--export", required=True, type=Path)
    parser.add_argument("--native-library", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--allow-worktree", action="store_true")
    parser.add_argument("--entry", choices=tuple(ENTRIES), default="battle-presentation",
                        help="explicit review lane; the existing V2 default remains unchanged")
    args = parser.parse_args()
    try:
        result = package(args.export, args.native_library, args.output, allow_worktree=args.allow_worktree,
                         entry=args.entry)
    except (OSError, ValueError, RuntimeError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print(f"review packaging failed: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
