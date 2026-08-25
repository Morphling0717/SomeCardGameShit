# Gate 4B-R3.1 arena assets

This directory contains original temporary assets for the R3.1 open-arena
visual candidate. They do not include or derive from YGOPro2/Yu-Gi-Oh code,
coordinates, models, textures, logos, audio, or wording.

## Generated floor

- `r3_industrial_floor_albedo.png`
- SHA-256: `9892b03ff0ab3dbe6fb0e733b32461a36e2bc960f7105110f0d6a34b79dd1343`
- Generated with OpenAI's built-in image generation workflow on 2026-08-25.
- Used once across the central 46 x 34 portion of an 80 x 60 open floor. The
  shader preserves its original world-space density, fades it into procedural
  steel, and never tiles or exposes the texture as an arena perimeter.

The exact generation summary is registered in the candidate-only
`R3_ASSET_MANIFEST.json` beside this file. The frozen Gate 4B-R2 product
manifest is intentionally unchanged.

## Original mechanical dressing

- `r3_arena_machinery.glb`
- SHA-256: `4ce416e3828dbcdbdf94b407c7f800144497af5afb5f2801bd08b35b267c9108`
- Generator: `source/generate_r3_arena_machinery.py`
- Generator SHA-256: `927b099463c0a0c634a25bd75fe40c780f3b83ffadfd91be61291a34038395f3`

The GLB is generated with the official Blender 5.2.0 LTS Windows x64 portable
build. Its downloaded ZIP was verified against Blender's official checksum:

`2d184b626c001692c362291911293b6a297179d618d95e9e9192c3a80318adc4`

From the repository root:

```powershell
build/blender-toolchain/blender-5.2.0-windows-x64/blender.exe `
  --background `
  --python client/godot/assets/visual/arena/source/generate_r3_arena_machinery.py `
  -- `
  --output client/godot/assets/visual/arena/r3_arena_machinery.glb
```

The generator validates that every scenery mesh remains outside the invisible
19.8 x 16.6 gameplay footprint. It emits no camera, light, collision, card
slot, text, logo, or enclosing perimeter.
