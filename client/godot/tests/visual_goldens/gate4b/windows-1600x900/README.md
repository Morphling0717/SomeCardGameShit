# Gate 4B Windows visual goldens

This directory contains the reviewed 1600×900 display-backed reference images
for the Gate 4B product visual baseline. They were approved from a visual-suite
schema 3 report. CI never updates these files.

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

The updater first validates all required states, screenshot hashes, asset-manifest
hash, viewport size, and the 600-frame evidence. The `--accept` acknowledgement
is mandatory and is intentionally absent from CI. Neither a software-renderer
run nor a passing comparison can approve or replace a golden automatically;
every replacement remains a deliberate human review decision.
