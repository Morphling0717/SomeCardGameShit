# Third-party notices

This repository's original source code is licensed under GPL-3.0-or-later.

## JSON for Modern C++

- Upstream: `nlohmann/json`
- Version: 3.12.0
- License: MIT
- Retrieved by CMake from the upstream release archive with a pinned SHA-256;
  not committed to this repository.
- The complete license is installed as `nlohmann-json-LICENSE.MIT` beside this
  notice in Gate 2 native packages.

## Godot Engine

- Upstream: `godotengine/godot`
- Version: 4.7.2 .NET
- License: MIT
- The editor and export templates are downloaded from the official release
  with a pinned digest; they are not committed to this repository.
- Desktop client packages include `Godot-LICENSE.txt` and the exhaustive
  upstream `Godot-COPYRIGHT.txt` for Godot's bundled third-party components.
  The pinned 4.7.2 copyright file has SHA-256
  `cb1980c88089573bcacd7221d777c689bb8bbd778799f24c27fca0fe5f774d6d`.

## Microsoft .NET Runtime

- Upstream: `dotnet/runtime`
- Version: 8.0.30, as embedded by the locked Godot 4.7.2 .NET export templates
- License: MIT, with separately attributed third-party components
- Desktop client packages include `Dotnet-Runtime-LICENSE.txt` and
  `Dotnet-Runtime-THIRD-PARTY-NOTICES.txt`. Their pinned SHA-256 values are
  `cfc21f5e8bd655ae997eec916138b707b1d290b83272c02a95c9f821b8c87310`
  and `97c1a7b3da6a4c6ad516448719f45114b41a4d4c5aa300a944476e2e4f5da438`.

## Noto Sans CJK SC

- Upstream: `notofonts/noto-cjk`
- Version: 2.004, Regular, Simplified Chinese
- License: SIL Open Font License 1.1
- The font is committed at
  `client/godot/assets/fonts/NotoSansCJKsc-Regular.otf`; its pinned SHA-256 is
  `2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b`.
- The complete license is stored at `client/godot/assets/fonts/OFL.txt` and is
  copied into desktop client packages.

## YGOProUnity_V2

- Upstream: `lllyasviel/YGOProUnity_V2`
- License: GPL-3.0
- Not vendored in this repository.
- The bootstrap script checks out the exact revision recorded in `upstream/upstream.lock.json`.

## ygopro-core

- Upstream: `Fluorohydride/ygopro-core`
- License: see the upstream repository at the pinned revision.
- Not vendored in this repository.
- Its pinned revision is a research baseline and is not yet claimed to be ABI-compatible with the pinned YGOPro2 client.

## Assets

No Yu-Gi-Oh card images, logos, music, voice, or other proprietary game assets
are intentionally distributed here. Gate 3A visuals are original geometric
placeholders and text; the only bundled third-party visual asset is the
explicitly licensed Noto font listed above.
