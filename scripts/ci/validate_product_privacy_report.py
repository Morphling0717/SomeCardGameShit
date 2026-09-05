#!/usr/bin/env python3
"""Validate actual product-face poison/scrub evidence; headless never proves GPU privacy."""
# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys

try:
    from .validate_product_visual_report import ProductVisualError, RESOLUTIONS, _keys, _integer, _load, _validate_image
except ImportError:
    from validate_product_visual_report import ProductVisualError, RESOLUTIONS, _keys, _integer, _load, _validate_image

ProductPrivacyError = ProductVisualError
TOP = {"schema_version", "suite", "api", "evidence_kind", "injection_source", "injection_verified",
       "detector_self_test_passed", "gpu_injection_verified", "injection_magenta_pixels", "samples", "success"}
SAMPLE = {"state", "frame_ordinal", "viewer", "revision", "frame_clock", "viewer_reads_delta", "private_queries_delta",
          "forbidden_tokens", "identity_resource_leaks", "collisions", "drag_tokens", "private_callbacks",
          "hidden_identity_leaks", "input_enabled", "opaque_cover", "gpu_checked", "magenta_pixels",
          "width", "height", "sha256"}
ZERO_FIELDS = {"viewer_reads_delta", "private_queries_delta", "forbidden_tokens", "identity_resource_leaks",
               "collisions", "drag_tokens", "private_callbacks", "hidden_identity_leaks", "magenta_pixels"}


def validate_directory(directory: Path | str, require_gpu: bool = False) -> None:
    directory = Path(directory).resolve()
    data = _keys(_load(directory / "product-privacy.json"), TOP, "product privacy report")
    _integer(data["schema_version"], 1, 1, "schema_version")
    if data["suite"] != "product-v05-privacy" or data["api"] != "scgs_v05" or \
       data["injection_source"] != "real-revealed-product-hand":
        raise ProductPrivacyError("Not real schema-2 product-hand privacy evidence")
    for field in ("success", "injection_verified", "detector_self_test_passed"):
        if data[field] is not True: raise ProductPrivacyError(f"Missing actual privacy proof: {field}")
    display = data["evidence_kind"] == "display-gpu"
    if data["evidence_kind"] not in {"display-gpu", "structural-only"} or (require_gpu and not display):
        raise ProductPrivacyError("Headless/structural evidence cannot prove GPU privacy")
    if data["gpu_injection_verified"] is not display:
        raise ProductPrivacyError("GPU positive-control flag disagrees with execution surface")
    _integer(data["injection_magenta_pixels"], 64 if display else 0, 50_000_000 if display else 0, "injection_magenta_pixels")
    if type(data["samples"]) is not list or len(data["samples"]) != 4:
        raise ProductPrivacyError("Both resolving and covered require two real frames")
    expected = [(state, ordinal) for state in ("resolving", "covered") for ordinal in (1, 2)]
    files = set()
    resolution = None
    revisions = {}
    for sample, pair in zip(data["samples"], expected, strict=True):
        sample = _keys(sample, SAMPLE, "privacy sample")
        _integer(sample["frame_ordinal"], 1, 2, "frame_ordinal")
        if (sample["state"], sample["frame_ordinal"]) != pair:
            raise ProductPrivacyError("Privacy frames are missing, duplicate or out of order")
        if sample["viewer"] is not None or sample["input_enabled"] is not False:
            raise ProductPrivacyError("A protected frame exposed a viewer or active input")
        if type(sample["opaque_cover"]) is not bool or (pair[0] == "covered" and not sample["opaque_cover"]):
            raise ProductPrivacyError("Handoff requires a real opaque cover")
        _integer(sample["revision"], 0, 2**64 - 1, "revision")
        if pair[0] in revisions and revisions[pair[0]] != sample["revision"]:
            raise ProductPrivacyError("Privacy state revision drifted between its two frames")
        revisions[pair[0]] = sample["revision"]
        for field in ZERO_FIELDS: _integer(sample[field], 0, 0, field)
        if sample["frame_clock"] != ("frame-post-draw" if display else "process-frame") or sample["gpu_checked"] is not display:
            raise ProductPrivacyError("Frame clock cannot upgrade a headless check to GPU evidence")
        for field in ("width", "height"): _integer(sample[field], 1, 16384, field)
        size = (sample["width"], sample["height"])
        if size not in RESOLUTIONS or (resolution is not None and size != resolution):
            raise ProductPrivacyError("Privacy viewport is unsupported or changed")
        resolution = size
        digest = sample["sha256"]
        if not display:
            if digest is not None: raise ProductPrivacyError("Headless frames cannot contain GPU screenshot hashes")
            continue
        if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ProductPrivacyError("Missing exact GPU image hash")
        filename = f"privacy-{pair[0]}-{pair[1]}.png"
        path = directory / filename
        files.add(filename)
        rgba = _validate_image(path, *size, digest)
        if any(rgba[index] >= 245 and rgba[index + 1] <= 12 and rgba[index + 2] >= 245 and rgba[index + 3] >= 250
               for index in range(0, len(rgba), 4)):
            raise ProductPrivacyError("Private magenta pixels survived in the final GPU image")
    if {path.name for path in directory.glob("privacy-*.png")} != files:
        raise ProductPrivacyError("Stale or unreported product privacy screenshots")
    if revisions["covered"] <= revisions["resolving"]:
        raise ProductPrivacyError("No actual prepared command advanced the revision before handoff")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--require-gpu", action="store_true")
    args = parser.parse_args()
    try:
        validate_directory(args.directory, args.require_gpu)
    except (ProductPrivacyError, OSError) as error:
        print(f"product privacy validation failed: {error}", file=sys.stderr)
        return 1
    print("product privacy evidence verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
