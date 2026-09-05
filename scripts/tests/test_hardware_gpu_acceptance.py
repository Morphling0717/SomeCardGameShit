"""Hardware timing cannot be claimed from a software renderer or weaker report."""
# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import unittest

from scripts.dev.validate_hardware_gpu_acceptance import (
    HardwareGpuEvidenceError, hardware_adapter, validate_measurements,
)
from scripts.tests.test_validate_product_visual_report import performance


def log(adapter: str) -> str:
    return f"OpenGL API 3.3 - Compatibility - Using Device: {adapter}\n"


class HardwareGpuAcceptanceTests(unittest.TestCase):
    def test_actual_hardware_renderer_is_accepted_not_an_inventory_string(self) -> None:
        for adapter in ("NVIDIA - NVIDIA GeForce RTX 4080 Laptop GPU", "AMD - Radeon RX 7800 XT",
                        "Intel - Intel Arc A770", "Apple - Apple M3"):
            with self.subTest(adapter=adapter):
                self.assertEqual(adapter, hardware_adapter(log(adapter)))
        for missing in ("", "NVIDIA GeForce RTX 4080", "GPU inventory: NVIDIA RTX 4080",
                        "adapter_type=discrete; display-gpu=true"):
            with self.subTest(missing=missing), self.assertRaises(HardwareGpuEvidenceError):
                hardware_adapter(missing)

    def test_software_and_virtual_devices_cannot_claim_hardware_even_with_vendor_names(self) -> None:
        for adapter in ("Microsoft - Microsoft Basic Render Driver", "Microsoft WARP",
                        "Mesa - llvmpipe (LLVM 18.1.1, 256 bits)", "Mesa softpipe", "Mesa lavapipe",
                        "Google SwiftShader", "Intel - software rasterizer", "NVIDIA - WARP",
                        "Intel - GDI Generic", "VMware SVGA3D", "VirtualBox GPU"):
            with self.subTest(adapter=adapter), self.assertRaises(HardwareGpuEvidenceError):
                hardware_adapter(log(adapter))

    def test_multiple_devices_or_unknown_adapter_are_not_silently_selected(self) -> None:
        for value in (log("NVIDIA - RTX 4080") * 2, log("Unknown Vendor - Unknown Device"),
                      log("NVIDIA - RTX 4080") + log("Microsoft Basic Render Driver")):
            with self.subTest(value=value), self.assertRaises(HardwareGpuEvidenceError):
                hardware_adapter(value)

    def test_hardware_still_requires_real_heavy_board_and_unchanged_timing_budgets(self) -> None:
        runtime = log("NVIDIA - NVIDIA GeForce RTX 4080 Laptop GPU")
        self.assertIn("RTX 4080", validate_measurements(runtime, performance(), (1280, 720)))
        for field, value in (("p95_ms", 33.31), ("max_ms", 100), ("warmup_frames", 299),
                             ("measured_frames", 299), ("player0_main_board", 2), ("zero_growth", False)):
            data = performance()
            data[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                validate_measurements(runtime, data, (1280, 720))
        data = performance()
        data["after"]["textures"] += 1
        with self.assertRaises(ValueError):
            validate_measurements(runtime, data, (1280, 720))

    def test_software_even_fast_valid_measurements_are_rejected(self) -> None:
        data = performance()
        data["p95_ms"], data["max_ms"] = 1, 2
        with self.assertRaises(HardwareGpuEvidenceError):
            validate_measurements(log("Microsoft WARP"), data, (1280, 720))


if __name__ == "__main__":
    unittest.main()
