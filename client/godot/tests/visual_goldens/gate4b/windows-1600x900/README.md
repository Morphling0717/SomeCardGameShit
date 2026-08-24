# Gate 4B Windows visual goldens

This directory contains the manually reviewed 1600×900 display-backed Gate
4B-R2/schema 4 reference set. It contains exactly the required 16 states,
including the four hand fixtures and `field-readability`; CI never updates these
files. CI first validates that `GOLDEN_METADATA.json` names the complete frozen
state inventory and capture contract, then requires exactly one real capture and
one committed PNG for every state. Removing a state or reusing only a subset of
the goldens therefore fails before perceptual comparison begins.

Hardware-accelerated display-backed runs enforce the strict frame-time budget:
p95 must be at most 33.3 ms and every measured frame must remain below 100 ms.
A renderer explicitly identified as pure software is still useful for screenshot,
layout, privacy, and zero-resource-growth checks, but its timing measurements do
not count as hardware performance evidence.

After deliberately reviewing every image from a successful visual-suite run,
replace the goldens with:

```powershell
python scripts/ci/update_gate4b_goldens.py `
  --report C:\absolute\path\to\visual-suite.json `
  --destination client/godot/tests/visual_goldens/gate4b/windows-1600x900 `
  --accept
```

The updater accepts only a Gate 4B-R2/schema 4 report, then validates all 16
required states, two-FramePostDraw capture contract, pixel anchors/ROIs,
screenshot hashes, asset-manifest hash, viewport size, and the 600-frame evidence.
The `--accept` acknowledgement
is mandatory and is intentionally absent from CI. Neither a software-renderer
run nor a passing comparison can approve or replace a golden automatically;
every replacement remains a deliberate human review decision.
