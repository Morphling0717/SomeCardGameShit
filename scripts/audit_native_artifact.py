#!/usr/bin/env python3
"""Audit the Gate 2 native library architecture and exported C surface."""

from __future__ import annotations

import argparse
import struct
import subprocess
import sys
from pathlib import Path


EXPECTED_EXPORTS = {
    "scgs_v04_abi_version",
    "scgs_v04_create",
    "scgs_v04_destroy",
    "scgs_v04_get_last_error",
    "scgs_v04_get_reaction_context_json",
    "scgs_v04_get_view_json",
    "scgs_v04_list_legal_actions_json",
    "scgs_v04_list_valid_donors_json",
    "scgs_v04_list_valid_slots_json",
    "scgs_v04_list_valid_targets_json",
    "scgs_v04_preview_payment_json",
    "scgs_v04_read_events_json",
    "scgs_v04_start",
    "scgs_v04_submit_command_json",
}


class AuditError(RuntimeError):
    pass


def _u16(data: bytes, offset: int, endian: str = "<") -> int:
    return struct.unpack_from(f"{endian}H", data, offset)[0]


def _u32(data: bytes, offset: int, endian: str = "<") -> int:
    return struct.unpack_from(f"{endian}I", data, offset)[0]


def _read_c_string(data: bytes, offset: int) -> str:
    end = data.find(b"\0", offset)
    if end < 0:
        raise AuditError("unterminated PE export name")
    return data[offset:end].decode("ascii")


def _audit_pe(data: bytes) -> tuple[set[str], set[str]]:
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise AuditError("invalid PE header")
    pe_offset = _u32(data, 0x3C)
    if data[pe_offset : pe_offset + 4] != b"PE\0\0":
        raise AuditError("invalid PE signature")

    coff = pe_offset + 4
    machine = _u16(data, coff)
    architectures = {0x8664: "x86_64", 0xAA64: "arm64"}
    if machine not in architectures:
        raise AuditError(f"unsupported PE machine 0x{machine:04x}")

    section_count = _u16(data, coff + 2)
    optional_size = _u16(data, coff + 16)
    optional = coff + 20
    magic = _u16(data, optional)
    if magic == 0x20B:
        data_directories = optional + 112
    elif magic == 0x10B:
        data_directories = optional + 96
    else:
        raise AuditError(f"unsupported PE optional-header magic 0x{magic:04x}")

    export_rva = _u32(data, data_directories)
    if export_rva == 0:
        raise AuditError("PE library has no export directory")

    sections = optional + optional_size

    def rva_to_offset(rva: int) -> int:
        for index in range(section_count):
            section = sections + index * 40
            virtual_size = _u32(data, section + 8)
            virtual_address = _u32(data, section + 12)
            raw_size = _u32(data, section + 16)
            raw_offset = _u32(data, section + 20)
            if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
                return raw_offset + rva - virtual_address
        raise AuditError(f"PE RVA 0x{rva:x} is outside all sections")

    export_offset = rva_to_offset(export_rva)
    name_count = _u32(data, export_offset + 24)
    names_offset = rva_to_offset(_u32(data, export_offset + 32))
    exports = {
        _read_c_string(data, rva_to_offset(_u32(data, names_offset + index * 4)))
        for index in range(name_count)
    }
    return {architectures[machine]}, exports


def _audit_elf(data: bytes, library: Path) -> tuple[set[str], set[str]]:
    if len(data) < 20 or data[:4] != b"\x7fELF":
        raise AuditError("invalid ELF header")
    endian = "<" if data[5] == 1 else ">"
    machine = _u16(data, 18, endian)
    architectures = {62: "x86_64", 183: "arm64"}
    if machine not in architectures:
        raise AuditError(f"unsupported ELF machine {machine}")
    result = subprocess.run(
        ["nm", "-D", "--defined-only", str(library)],
        check=True,
        capture_output=True,
        text=True,
    )
    exports = {line.split()[-1] for line in result.stdout.splitlines() if line.split()}
    return {architectures[machine]}, exports


def _mach_architectures(data: bytes) -> set[str]:
    cpu_names = {0x01000007: "x86_64", 0x0100000C: "arm64"}
    magic = data[:4]
    if magic in (b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf"):
        endian = "<" if magic == b"\xcf\xfa\xed\xfe" else ">"
        cpu = _u32(data, 4, endian)
        if cpu not in cpu_names:
            raise AuditError(f"unsupported Mach-O CPU 0x{cpu:08x}")
        return {cpu_names[cpu]}

    if magic not in (b"\xca\xfe\xba\xbe", b"\xca\xfe\xba\xbf"):
        raise AuditError("invalid Mach-O header")
    fat64 = magic == b"\xca\xfe\xba\xbf"
    count = _u32(data, 4, ">")
    stride = 32 if fat64 else 20
    result = set()
    for index in range(count):
        cpu = _u32(data, 8 + index * stride, ">")
        if cpu not in cpu_names:
            raise AuditError(f"unsupported Mach-O fat CPU 0x{cpu:08x}")
        result.add(cpu_names[cpu])
    return result


def _audit_mach_o(data: bytes, library: Path) -> tuple[set[str], set[str]]:
    result = subprocess.run(
        ["/usr/bin/nm", "-gjU", str(library)],
        check=True,
        capture_output=True,
        text=True,
    )
    # Mach-O prefixes C symbols with one underscore.
    exports = {
        line.strip()[1:] if line.strip().startswith("_") else line.strip()
        for line in result.stdout.splitlines()
        if line.strip()
    }
    return _mach_architectures(data), exports


def audit(library: Path, expected_architecture: str) -> None:
    if not library.is_file():
        raise AuditError(f"native library does not exist: {library}")
    data = library.read_bytes()
    if data.startswith(b"MZ"):
        architectures, exports = _audit_pe(data)
    elif data.startswith(b"\x7fELF"):
        architectures, exports = _audit_elf(data, library)
    else:
        architectures, exports = _audit_mach_o(data, library)

    if architectures != {expected_architecture}:
        raise AuditError(
            f"expected exactly {expected_architecture}, found {sorted(architectures)}"
        )

    gate_exports = {name for name in exports if name.startswith("scgs_v04_")}
    missing = EXPECTED_EXPORTS - exports
    unexpected = exports - EXPECTED_EXPORTS
    if missing or unexpected:
        raise AuditError(
            f"export mismatch; missing={sorted(missing)}, unexpected={sorted(unexpected)}"
        )

    mangled = sorted(name for name in exports if name.startswith("?") or name.startswith("_Z"))
    if mangled:
        raise AuditError(f"C++ symbols escaped the library: {mangled[:10]}")

    print(
        f"audited {library}: architecture={expected_architecture}, "
        f"scgs_exports={len(gate_exports)}, no exported C++ symbols"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--library", required=True, type=Path)
    parser.add_argument("--architecture", required=True, choices=("x86_64", "arm64"))
    args = parser.parse_args()
    try:
        audit(args.library.resolve(), args.architecture)
    except (AuditError, OSError, struct.error, subprocess.CalledProcessError) as error:
        print(f"native artifact audit failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
