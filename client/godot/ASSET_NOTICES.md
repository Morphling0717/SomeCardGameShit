# AnimeV1 product visual asset notices

The product uses original fantasy-anime artwork created specifically for
SomeCardGameShit with OpenAI's built-in image generation workflow. Source
manifests preserve individual prompts, dates, hashes and review limitations.
The retired Gate 4B set of twenty-nine industrial card
illustrations, two industrial leader portraits, its card back and menu raster
was removed on 2026-09-05. Those historical files remain recoverable from Git
history; they are not product assets or licensed contents of this package.

The old industrial floor, machinery model, candidate scene and R3 shaders were
retired on 2026-09-05 with the industrial product profile. They are recoverable
from Git history and are not included in this AnimeV1 player build.

Original interface frames, badges, symbols, and fallback graphics are authored
in this repository as Godot resources or SVG components. YGOPro2 screenshots
were used only as a clean-room reference for general information density and
interaction rhythm; no YGOPro2/Yu-Gi-Oh code, coordinates, materials, Prefabs,
art, logos, audio, or wording are included in this package.

The identity-free neutral front at
`assets/visual/anime_v1/shared/fallback_front.svg` is a repository-authored
moon-gold rune on deep indigo, created on 2026-09-05 without reference art. It
contains no card, profession or series identity. Its path, SHA-256, purpose and
method are recorded in `assets/visual/ASSET_MANIFEST.json`. Both product unknown
cards and synthetic protocol fixtures share this fallback and the AnimeV1
card back; old numeric card identities no longer select artwork.

The isolated Gate 6A AnimeV1 visual slice under
`assets/visual/anime_v1/slice/` contains fourteen original fantasy-anime
candidate rasters made for this project with the same built-in workflow on
2026-08-26. It includes two true-alpha leader masters, seven representative
card illustrations, two ace evolution alternatives, one shared card back, one
wordless menu key art, and one open fantasy arena. It uses no reference image
or third-party game asset. These approved assets now form part of the default
playable AnimeV1 product path; the historical `slice` directory is retained to
preserve their provenance and stable hashes.
The two public leader portraits are cached head-and-shoulders atlas views of
the unmodified 1024x1536 masters: Aurelia uses pixel rectangle (390, 74, 300,
300), and Theraea uses (368, 14, 312, 312). These runtime crops do not create
new raster assets or modify the masters; both HUD and leader targets use them.
Its exact hashes and summaries are recorded in the adjacent source
`ASSET_MANIFEST.json` (packaged as `ANIME_V1_ASSET_MANIFEST.json`); complete
prompts, rejected-background notes, and the human-review boundary are recorded
in source `PROVENANCE.md` (packaged as `ANIME_V1_PROVENANCE.md`).

## Gate 6A-R1 AnimeV1 card body

The approval-candidate card-body set under
`assets/visual/anime_v1/card_body/` contains exactly twenty-three original project
assets. Twenty-one are deterministic, repository-authored SVG components:

- five continuous card silhouettes for follower, spell, amulet, trap, and
  field faces;
- three faction crests for Oathguard, Pactmage, and neutral cards;
- three opaque faction name plaques that keep card names independent from art;
- four integrated gameplay sockets for cost, attack, health, and countdown;
- four rarity ornaments for common, rare, epic, and legendary cards; and
- two shared variant layers for evolved cards and derived tokens.

These SVG components provide the authored frame geometry and gameplay
information sockets. They were created for this repository and are not copied
from a third-party card frame, logo, screenshot, or game asset. Their individual
paths, purposes, prompt summaries, dates, and SHA-256 digests are recorded in
`assets/visual/anime_v1/card_body/CARD_BODY_ASSET_MANIFEST.json`.

The remaining two raster card-body assets are replaceable surface-detail
candidates made with OpenAI's built-in image-generation workflow on
2026-08-26. Neither image contains card identity or gameplay information, and
both are used only on known, visible front faces through the shared frame
material:

- `materials/engraved-metal-v1.png` supplies restrained charcoal-silver
  engraved micro-detail for ordinary visible frames. It was selected from a
  refinement of generated candidate
  `exec-b30dc868-b7bd-4e7e-bca0-ee047eefcb28.png`; SHA-256
  `80f10eb17e31ccf868a14676c561064cbdecfca540e49b5fa60ff35a1fa75ab3`.
- `materials/legendary-foil-v1.png` supplies restrained indigo prismatic
  micro-detail for legendary visible frames. Its generated source candidate was
  `exec-323b01fb-1938-46b3-bfb1-3b88a7ef5022.png`; SHA-256
  `0f7da214b3c0500116499106d8bcd9449bffdbf8052f63138b91aa7dcedfec24`.

The complete generation prompts, rejection history, no-input-image statement,
and review boundary are recorded in
`assets/visual/anime_v1/card_body/PROVENANCE.md`. This notice records project
provenance. The integrated card-body direction was approved as the
playable-product baseline before the product-card batch was integrated; it
remains subject to later human commercial-release review and does not claim
trademark clearance or acceptance for shipping.

## Gate 5C-6C AnimeV1 product-card illustrations

The product batch under `assets/visual/anime_v1/cards/` contains twenty-eight
original 1024x1536 card illustrations generated specifically for this project
with OpenAI's built-in image-generation workflow on 2026-08-26. Together with
the seven frozen base illustrations under `anime_v1/slice/cards/`, it gives all
thirty-four constructible Oathguard/Pactmage/neutral definitions and derived
token `LO-T01` one unique real base illustration. The two locked ace evolution
alternatives remain in the frozen Gate 6A slice.

The illustrations contain no intentionally embedded gameplay text, card frame,
cost, statistics, logo, watermark, named franchise character, or third-party
game asset. Exact paths, SHA-256 hashes, purposes and truthful prompt summaries
are packaged in `ANIME_V1_PRODUCT_CARD_ART_ASSET_MANIFEST.json`; generation and
selection history is packaged in `ANIME_V1_PRODUCT_CARD_ART_PROVENANCE.md`.
The raw batch is capped at 96 MiB and its conservative desktop-compressed mip
estimate at 64 MiB. The runtime identity-texture working set remains capped at
twenty-four textures and must not preload the complete thirty-five-card base-art
catalog. Human full-resolution anatomy, text-like mark, trademark and IP review
is still required before commercial release.

## Battle Presentation V2, first-stage candidates

Exactly three additional raster candidates were generated on 2026-09-06 under
`assets/visual/anime_v1/presentation_v2/`. They extend the existing sixty-six
registered assets to sixty-nine; they do not replace or silently alter any of
the earlier illustration, card-body or shared asset hashes.

- `engraved-platinum.png` is a new 1254x1254 opaque RGB material generated
  from text without an input image: fine satin platinum, pale gold and shallow
  celestial engraving for visible card rims. SHA-256:
  `7b900cc0fe23c7262a791c54f15ac122aeac63edd674a46d37826a7facb4577b`.
- `LO-11-cutin.png` is a 1024x1536 RGB green-background cut-in derived with
  image generation from the project's `slice/cards/LO-11-evolved.png`.
  Final SHA-256:
  `de38f7b498328f9240804dba796acc146127e17837baee38db8aa716b50d036f`.
- `AP-11-cutin.png` is a 1024x1536 RGB green-background cut-in derived with
  image generation from the project's `slice/cards/AP-11-evolved.png`.
  Final SHA-256:
  `f722acc8a78f02d23cb1dfdbf35e5b4abe3ba93041900c56353534a2453338c5`.

The two cut-ins are **not native-alpha transparent PNGs**. Two extraction
attempts returned RGB with baked checkerboard and were rejected. The first
rejected extraction was subsequently edited with image generation to replace
the checkerboard by a flat green background; transparency is produced by a
runtime CanvasItem chroma-key shader. Rejected intermediate rasters are not
shipped. Foreground-edge quality, green spill and the final composited result
remain subject to actual GPU review; passing the inventory audit is not visual
approval. The selected files were copied without raster post-processing.

The complete material, extraction and final green-edit prompts are retained in
`assets/visual/anime_v1/presentation_v2/GENERATION_RECORD.json`. The shared
`assets/visual/ASSET_MANIFEST.json` (packaged as
`ANIME_V1_SHARED_ASSET_MANIFEST.json`) records each final path and hash, original
input artwork and its hash, dated modification history, use and generation
method. Runtime card names, numbers, frames and interface text are not baked
into these candidates. Their desktop imports use high-quality VRAM compression
and mipmaps; cut-in identity textures must never bind to hidden cards.

These assets were generated for the project under the user's explicit
development authorization using OpenAI's built-in image-generation workflow;
the cut-ins use only recorded original project artwork as reference. No
third-party game's artwork, frame, logo, audio or trademark material is
included. They are project-authorized development and redistribution
candidates, not third-party MIT/OFL assets and not a claim of exclusive
copyright or trademark clearance. Human commercial-release review remains
required. This registration covers only the three first-stage presentation
candidates, not completion of the entire presentation overhaul.

## Noto Serif CJK SC SemiBold

`assets/fonts/NotoSerifCJKsc-SemiBold.otf` is copied without modification from
the immutable Noto CJK upstream commit
`f8d157532fbfaeda587e826d4cd5b21a49186f7c`:
<https://github.com/notofonts/noto-cjk/blob/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Serif/OTF/SimplifiedChinese/NotoSerifCJKsc-SemiBold.otf>.
It is distributed under the SIL Open Font License 1.1 included as
`assets/fonts/OFL.txt`. Its expected SHA-256 is
`d627b53dbcde61e07de1498d2623a8b287f78585ffbc90cc0618d0caaa2ed6b0`,
also recorded in `assets/fonts/SHA256SUMS`. The font is used for card names and
large fantasy-display numerals; Noto Sans remains the rules and interface body
font.
