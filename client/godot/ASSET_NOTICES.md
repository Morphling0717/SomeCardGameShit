# Gate 4B visual asset notices

The temporary card illustrations, shared card back, menu background, and two
deck-bound leader portraits in `assets/visual/` were created specifically for
SomeCardGameShit with OpenAI's built-in image generation workflow on
2026-08-24. They are original placeholder assets intended to be replaced by
production artwork. Their prompts request no third-party logos, trademarks,
text, watermarks, or copied game assets.

The neutral industrial floor albedo under `assets/visual/arena/` was created
with the same built-in workflow on 2026-08-25 for the Gate 4B-R3.1 visual
candidate. It deliberately contains no perimeter frame, gameplay slots,
faction split, text, logo, watermark, or reference-game imagery. The candidate
arena meshes and shaders are original repository-authored assets.

Original interface frames, badges, symbols, and fallback graphics are authored
in this repository as Godot resources or SVG components. YGOPro2 screenshots
were used only as a clean-room reference for general information density and
interaction rhythm; no YGOPro2/Yu-Gi-Oh code, coordinates, materials, Prefabs,
art, logos, audio, or wording are included in this package.

Exact file paths, SHA-256 digests, purposes, generation methods, dates, and
prompt summaries are recorded in the frozen R2
`assets/visual/ASSET_MANIFEST.json` and the isolated R3 candidate
`assets/visual/arena/R3_ASSET_MANIFEST.json`.

The isolated Gate 6A AnimeV1 visual slice under
`assets/visual/anime_v1/slice/` contains fourteen original fantasy-anime
candidate rasters made for this project with the same built-in workflow on
2026-08-26. It includes two true-alpha leader masters, seven representative
card illustrations, two ace evolution alternatives, one shared card back, one
wordless menu key art, and one open fantasy arena. It uses no reference image
or third-party game asset and is not yet the default playable product path.
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
provenance only: the Gate 6A-R1 card body remains pending explicit user approval
and later human commercial-release review; it does not claim final visual
approval, trademark clearance, or acceptance for shipping.

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
