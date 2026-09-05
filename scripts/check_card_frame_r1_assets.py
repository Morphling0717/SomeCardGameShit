#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Read-only source/GLB contract for Card Frame R1; no Blender/Godot required.

The manifest is art/card_frame_r1/frame-manifest.json. Sources stay outside
Godot; only self-contained GLBs and runtime textures belong in its asset tree.
This verifies bytes, hierarchy, material slots, actual transformed positions
and triangle budgets, not artistic quality or runtime text occlusion.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import struct
import sys
from pathlib import Path, PurePosixPath
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = "art/card_frame_r1/frame-manifest.json"
RUNTIME = "client/godot/assets/visual/anime_v1/card_frame_r1"
SOURCE_PATHS = {
    "blend": "art/card_frame_r1/card_frame_r1.blend",
    "script": "scripts/art/build_card_frame_r1.py",
    "concept": "art/card_frame_r1/concept-master-r1.png",
}
MODELS = {"high": (f"{RUNTIME}/frame-master.glb", 35_000),
          "low": (f"{RUNTIME}/frame-master-low.glb", 14_000)}
MATERIALS = {"Platinum", "Gold", "Enamel", "Emerald", "Sapphire", "Ruby", "DarkRecess"}
GROUPS = ("CommonFrame", "AttackFoot", "HealthFoot")
BOUNDS = ((-0.79, 0.79), (0.0, 0.13), (-1.0534, 1.0534))
EPSILON = 0.0001
MAX_GLB_BYTES = 64 * 1024 * 1024
IDENTITY = ((1.0, 0.0, 0.0, 0.0), (0.0, 1.0, 0.0, 0.0),
            (0.0, 0.0, 1.0, 0.0), (0.0, 0.0, 0.0, 1.0))


class CardFrameAuditError(ValueError):
    pass


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise CardFrameAuditError(message)


def _hash(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def _path(root: Path, relative: object) -> Path:
    _require(isinstance(relative, str) and bool(relative), "asset path must be a nonempty string")
    parts = PurePosixPath(relative)
    _require("\\" not in relative and ":" not in relative and not parts.is_absolute()
             and ".." not in parts.parts and str(parts) == relative, "asset path must be normalized and repository-relative")
    candidate = (root / relative).resolve(strict=True)
    _require(candidate.is_relative_to(root.resolve()), "asset resolves outside the repository")
    _require(candidate.is_file(), "asset must be a regular file")
    return candidate


def _record(root: Path, record: object, expected: str | None = None) -> Path:
    _require(isinstance(record, dict) and set(record) == {"path", "sha256"}, "asset record must have exactly path/sha256")
    if expected is not None:
        _require(record["path"] == expected, f"asset must use locked path {expected}")
    _require(isinstance(record["sha256"], str) and re.fullmatch(r"[0-9a-f]{64}", record["sha256"]) is not None,
             "asset SHA-256 must be lowercase 64-hex")
    path = _path(root, record["path"])
    _require(_hash(path) == record["sha256"], f"asset SHA-256 mismatch: {record['path']}")
    return path


def _numbers(value: object, count: int, label: str) -> list[float]:
    _require(isinstance(value, list) and len(value) == count, f"{label} must have {count} numbers")
    _require(all(isinstance(item, (float, int)) and not isinstance(item, bool)
                 and math.isfinite(item) for item in value), f"{label} must contain finite numbers")
    return [float(item) for item in value]


def _index(value: object, length: int, label: str) -> int:
    _require(isinstance(value, int) and not isinstance(value, bool) and 0 <= value < length, f"invalid {label} index")
    return value


def _multiply(left: Any, right: Any) -> tuple[tuple[float, ...], ...]:
    return tuple(tuple(sum(left[row][item] * right[item][column] for item in range(4))
                       for column in range(4)) for row in range(4))


def _transform(node: dict[str, Any]) -> tuple[tuple[float, ...], ...]:
    if "matrix" in node:
        _require(not any(key in node for key in ("translation", "rotation", "scale")), "node cannot mix matrix and TRS")
        values = _numbers(node["matrix"], 16, "matrix")
        matrix = tuple(tuple(values[column * 4 + row] for column in range(4)) for row in range(4))
        _require(all(abs(matrix[3][i] - IDENTITY[3][i]) <= 1e-6 for i in range(4)), "node matrix must be affine")
        return matrix
    t = _numbers(node.get("translation", [0, 0, 0]), 3, "translation")
    s = _numbers(node.get("scale", [1, 1, 1]), 3, "scale")
    x, y, z, w = _numbers(node.get("rotation", [0, 0, 0, 1]), 4, "rotation")
    _require(abs(x*x + y*y + z*z + w*w - 1) <= 1e-4, "rotation quaternion must be normalized")
    rotation = ((1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)),
                (2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)),
                (2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)))
    return tuple(tuple(rotation[row][column]*s[column] for column in range(3)) + (t[row],)
                 for row in range(3)) + ((0, 0, 0, 1),)


def _glb(path: Path) -> tuple[dict[str, Any], bytes]:
    _require(path.stat().st_size <= MAX_GLB_BYTES, "GLB exceeds 64 MiB audit ceiling")
    data = path.read_bytes()
    _require(len(data) >= 20, "truncated GLB")
    magic, version, length = struct.unpack_from("<III", data)
    _require(magic == 0x46546C67 and version == 2 and length == len(data), "invalid GLB v2 header/length")
    chunks: list[tuple[int, bytes]] = []
    offset = 12
    while offset < len(data):
        _require(offset + 8 <= len(data), "truncated GLB chunk header")
        size, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        _require(size % 4 == 0 and offset + size <= len(data), "invalid GLB chunk length")
        chunks.append((kind, data[offset:offset+size]))
        offset += size
    _require(len(chunks) == 2 and chunks[0][0] == 0x4E4F534A and chunks[1][0] == 0x004E4942,
             "GLB must contain exactly JSON then embedded BIN")
    document = json.loads(chunks[0][1].decode("utf-8"))
    _require(isinstance(document, dict) and document.get("asset", {}).get("version") == "2.0", "expected glTF 2.0")
    _require(not document.get("animations") and not document.get("skins"), "static frame must not contain animation/skin")
    buffers = document.get("buffers", [])
    _require(len(buffers) == 1 and isinstance(buffers[0], dict) and "uri" not in buffers[0], "GLB must use one embedded buffer")
    size = buffers[0].get("byteLength")
    _require(isinstance(size, int) and size > 0 and size <= len(chunks[1][1]) <= size + 3, "BIN byteLength mismatch")
    for image in document.get("images", []):
        _require(isinstance(image, dict) and "uri" not in image and "bufferView" in image,
                 "GLB images must be embedded, never external/data URI dependencies")
    return document, chunks[1][1][:size]


def _accessor(document: dict[str, Any], binary: bytes, index: int, *, positions: bool) -> list[Any]:
    accessors, views = document.get("accessors", []), document.get("bufferViews", [])
    accessor = accessors[_index(index, len(accessors), "accessor")]
    _require(isinstance(accessor, dict) and "sparse" not in accessor and not accessor.get("normalized"),
             "frame accessors must be explicit non-normalized data")
    view = views[_index(accessor.get("bufferView"), len(views), "bufferView")]
    _require(view.get("buffer", 0) == 0 and not view.get("extensions"), "bufferView must use uncompressed embedded buffer")
    count, component = accessor.get("count"), accessor.get("componentType")
    _require(isinstance(count, int) and 0 < count <= 1_000_000, "invalid accessor count")
    if positions:
        _require(accessor.get("type") == "VEC3" and component == 5126, "POSITION must be FLOAT VEC3")
        fmt, element_size = "<fff", 12
    else:
        _require(accessor.get("type") == "SCALAR" and component in (5121, 5123, 5125), "indices must be unsigned SCALAR")
        fmt, element_size = {5121: ("<B", 1), 5123: ("<H", 2), 5125: ("<I", 4)}[component]
    start, view_size = view.get("byteOffset", 0), view.get("byteLength")
    offset, stride = accessor.get("byteOffset", 0), view.get("byteStride", element_size)
    _require(all(isinstance(value, int) and value >= 0 for value in (start, view_size, offset, stride))
             and stride >= element_size and start + view_size <= len(binary)
             and offset + (count-1)*stride + element_size <= view_size, "accessor exceeds its bufferView")
    values = [struct.unpack_from(fmt, binary, start + offset + item*stride) for item in range(count)]
    if positions:
        _require(all(all(math.isfinite(number) for number in value) for value in values), "POSITION contains non-finite coordinates")
        return values
    return [value[0] for value in values]


def audit_model(path: Path, triangle_limit: int) -> dict[str, Any]:
    document, binary = _glb(path)
    nodes, meshes, materials = document.get("nodes", []), document.get("meshes", []), document.get("materials", [])
    names = [material.get("name") for material in materials]
    _require(len(names) == len(MATERIALS) and set(names) == MATERIALS, "GLB material slots must be exactly the locked seven names")
    scenes = document.get("scenes", [])
    scene = scenes[_index(document.get("scene", 0), len(scenes), "scene")]
    roots = scene.get("nodes", [])
    _require(len(roots) == 1, "GLB must have one CardFrameMaster scene root")
    root = _index(roots[0], len(nodes), "root")
    _require(nodes[root].get("name") == "CardFrameMaster", "root name must be CardFrameMaster")
    visited: set[int] = set()
    grouped = dict.fromkeys(GROUPS, 0)
    used_materials: set[str] = set()
    minimum, maximum = [math.inf]*3, [-math.inf]*3
    triangles = 0

    def visit(index: int, parent_matrix: Any, group: str | None) -> None:
        nonlocal triangles
        index = _index(index, len(nodes), "node")
        _require(index not in visited, "node hierarchy is cyclic or shared between parents")
        visited.add(index)
        node = nodes[index]
        _require(isinstance(node, dict), "invalid node")
        name = node.get("name", "")
        _require(isinstance(name, str), "node name must be text")
        group = next((prefix for prefix in GROUPS if name.startswith(prefix)), group)
        matrix = _multiply(parent_matrix, _transform(node))
        if "mesh" in node:
            _require(group is not None, "every frame mesh must belong to a named CommonFrame/AttackFoot/HealthFoot group")
            mesh = meshes[_index(node["mesh"], len(meshes), "mesh")]
            _require(not mesh.get("weights"), "static card frame cannot contain morph weights")
            for primitive in mesh.get("primitives", []):
                _require(primitive.get("mode", 4) == 4 and not primitive.get("targets"), "frame primitives must be static TRIANGLES")
                material = names[_index(primitive.get("material"), len(names), "material")]
                used_materials.add(material)
                vertices = _accessor(document, binary, primitive.get("attributes", {}).get("POSITION"), positions=True)
                indices = (_accessor(document, binary, primitive["indices"], positions=False)
                           if "indices" in primitive else list(range(len(vertices))))
                _require(len(indices) % 3 == 0 and all(value < len(vertices) for value in indices), "invalid triangle indices")
                triangles += len(indices)//3
                grouped[group] += len(indices)//3
                for position in vertices:
                    for axis in range(3):
                        value = sum(matrix[axis][part]*position[part] for part in range(3)) + matrix[axis][3]
                        _require(math.isfinite(value), "transformed position must be finite")
                        minimum[axis], maximum[axis] = min(minimum[axis], value), max(maximum[axis], value)
        for child in node.get("children", []):
            visit(child, matrix, group)

    visit(root, IDENTITY, None)
    _require(len(visited) == len(nodes), "GLB contains unreachable nodes outside CardFrameMaster")
    _require(used_materials == MATERIALS and all(value > 0 for value in grouped.values()), "all seven materials and three geometry groups must be used")
    _require(0 < triangles < triangle_limit, f"triangle count {triangles} must be strictly below {triangle_limit}")
    for axis, (low, high) in enumerate(BOUNDS):
        _require(minimum[axis] >= low-EPSILON and maximum[axis] <= high+EPSILON,
                 f"transformed axis {axis} bounds {minimum[axis]}..{maximum[axis]} exceed {low}..{high}")
    return {"triangles": triangles, "triangle_limit_exclusive": triangle_limit,
            "group_triangles": grouped, "materials": sorted(used_materials),
            "minimum": minimum, "maximum": maximum, "bounds_epsilon": EPSILON}


def _png_dimensions(path: Path) -> tuple[int, int]:
    with path.open("rb") as stream:
        header = stream.read(24)
    _require(len(header) == 24 and header[:8] == b"\x89PNG\r\n\x1a\n" and header[12:16] == b"IHDR", "expected PNG image")
    dimensions = struct.unpack(">II", header[16:24])
    _require(all(0 < size <= 8192 for size in dimensions), "PNG dimensions exceed 8192 or are empty")
    return dimensions


def audit(root: Path, manifest_relative: str = MANIFEST) -> dict[str, Any]:
    root = root.resolve(strict=True)
    manifest_path = _path(root, manifest_relative)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    required = {"schema_version", "kind", "blender_version", "sources", "models", "textures"}
    optional = {"extra_sources", "coordinates", "text_clearance", "approval"}
    _require(isinstance(manifest, dict) and required <= set(manifest)
             and set(manifest) <= required | optional, "unexpected frame manifest fields")
    _require(manifest["schema_version"] == 1 and manifest["kind"] == "card-frame-r1"
             and manifest["blender_version"] == "4.5.13", "expected locked card-frame-r1 / Blender 4.5.13 manifest")
    sources = manifest["sources"]
    _require(isinstance(sources, dict) and set(sources) == set(SOURCE_PATHS), "manifest must contain blend/script/concept sources")
    for role, relative in SOURCE_PATHS.items():
        source = _record(root, sources[role], relative)
        if role == "blend":
            with source.open("rb") as stream:
                _require(stream.read(7) == b"BLENDER", "source must be an uncompressed BLENDER file")
        elif role == "concept":
            _png_dimensions(source)
    if "coordinates" in manifest:
        _require(manifest["coordinates"] ==
                 "Godot metres: X width 1.58, Z depth 2.106667, Y face upward; maximum relief 0.13",
                 "unexpected coordinate declaration")
    if "text_clearance" in manifest:
        value = manifest["text_clearance"]
        _require(isinstance(value, (float, int)) and not isinstance(value, bool)
                 and math.isfinite(value) and value >= 0.012,
                 "declared glyph clearance must be at least 0.012; GPU occlusion remains separate")
    if "approval" in manifest:
        _require(manifest["approval"] == "candidate_not_user_approved",
                 "source audit cannot grant visual approval")
    extra_sources = manifest.get("extra_sources", [])
    _require(isinstance(extra_sources, list) and len(extra_sources) <= 1,
             "only the optional engraving bake source is permitted")
    for record in extra_sources:
        source = _record(root, record, "art/card_frame_r1/engraving-bake.blend")
        with source.open("rb") as stream:
            _require(stream.read(7) == b"BLENDER", "bake source must be an uncompressed BLENDER file")
    models = manifest["models"]
    _require(isinstance(models, list) and len(models) in (1, 2), "expected mandatory high and optional low model")
    results, lods = {}, set()
    for record in models:
        _require(isinstance(record, dict) and set(record) == {"path", "sha256", "lod"}, "model record must have path/sha256/lod")
        lod = record["lod"]
        _require(isinstance(lod, str) and lod in MODELS and lod not in lods, "invalid or duplicate model LOD")
        lods.add(lod)
        relative, budget = MODELS[lod]
        model = _record(root, {key: record[key] for key in ("path", "sha256")}, relative)
        results[lod] = audit_model(model, budget)
    _require("high" in lods, "high model is required")
    textures = manifest["textures"]
    _require(isinstance(textures, list) and len(textures) == 4, "expected three baked maps and engraving source")
    roles, paths = set(), set()
    for record in textures:
        _require(isinstance(record, dict) and set(record) == {"path", "sha256", "role"}, "texture record must have path/sha256/role")
        role = record["role"]
        _require(isinstance(role, str) and role in {"normal", "ao", "roughness", "engraving"} and role not in roles,
                 "invalid or duplicate texture role")
        roles.add(role)
        path = _record(root, {key: record[key] for key in ("path", "sha256")})
        _require(record["path"] not in paths, "texture roles must use distinct files")
        paths.add(record["path"])
        _require(path.suffix.lower() == ".png", "frame maps must be PNG")
        _require(record["path"].startswith(RUNTIME + "/") if role != "engraving" else
                 record["path"].startswith((RUNTIME + "/", "art/card_frame_r1/")), "texture is outside allowed frame roots")
        dimensions = _png_dimensions(path)
        if role != "engraving":
            _require(dimensions == (1024, 1024), "normal/ao/roughness must be exactly 1024x1024")
    return {"schema_version": 1, "suite": "card-frame-r1-source-and-model", "success": True,
            "manifest_sha256": _hash(manifest_path), "blender_version": "4.5.13", "models": results,
            "verified_textures": 4, "visual_or_text_occlusion_approved": False,
            "godot_export_or_gpu_executed": False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--manifest", default=MANIFEST, help="repository-relative manifest path")
    args = parser.parse_args()
    try:
        result = audit(args.repo_root, args.manifest)
    except (OSError, ValueError, KeyError, IndexError, TypeError, struct.error, RecursionError) as error:
        print(f"card frame R1 audit failed: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
