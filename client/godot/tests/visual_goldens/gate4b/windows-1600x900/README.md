# Gate 4B Windows visual goldens

This directory contains the reviewed 1600×900 display-backed reference images
for the Gate 4B product visual baseline. CI never updates these files.

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
is mandatory and is intentionally absent from CI.
