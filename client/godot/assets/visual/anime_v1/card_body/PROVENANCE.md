# AnimeV1 card-body material provenance

Status: project-bound Gate 6A-R1 approval candidate. Generation date: 2026-08-26.

The two raster material candidates in `materials/` were created with OpenAI's
built-in image-generation workflow. They are supporting surface detail only:
the shipping card silhouettes, full-bleed masked artwork, opaque faction name
plaques, cost sockets,
stat sockets, type crests and rarity ornaments are deterministic project SVGs
and Godot layout data. No Shadowverse, YGOPro2, third-party frame, screenshot,
logo, trademark, artist reference or input image was supplied.

The selected files were copied unchanged from the generated-images workspace.
They contain no text, card identity or gameplay information and may only be
sampled through the shared card-frame material. Hidden cards must never receive
either texture or any other front-face material.

The three SVGs in `nameplates/` were authored directly in the repository. Each
uses a fully opaque faction-colored interior so the card name remains a UI
element of the frame rather than text painted over the illustration. The five
silhouette SVGs and four gameplay-gem SVGs were also revised in the same pass:
the illustration now covers the full 3:4 silhouette, while dark outer fills
were replaced with restrained violet-metal and antique-gold rims. Cost, attack
and health now contain explicit central numeric bays, and the countdown socket
is a wider hourglass form with a two-digit bay. Their independent inner text
rectangles are measured with the actual product font before drawing, so glyphs
and outlines remain inside the authored decoration. Card names likewise use a
symmetric undecorated center bay that clears both faction flourishes: their
complete source text is measured with the font's real width, ascent and descent,
scaled without truncation, and centered on the plaque geometry. No generated or
third-party input was used for these deterministic edits.

## `materials/engraved-metal-v1.png`

The first draft used large repeating filigree and was rejected because the
quadrant repetition would compete with the authored frame. The selected second
generation used the previous draft only as an edit target and applied this
targeted request:

> Use case: precise-object-edit. Asset type: seamless tileable game UI material
> texture. Refine the previous grayscale engraved-metal texture so the ornament
> is much finer and quieter, suitable as subtle micro-detail inside a card frame
> rather than the main design. Preserve neutral charcoal-silver metal and
> shallow relief; remove the obvious four-quadrant repetition and visible center
> seams; reduce every filigree motif to roughly one quarter of its current scale;
> make all four edges seamless; keep contrast restrained; no central focal
> point, text, symbols, logo, watermark, card silhouette or border. Avoid large
> leaf motifs, obvious repeated squares, deep cracks, bright highlights and
> industrial panels.

- SHA-256: `80f10eb17e31ccf868a14676c561064cbdecfca540e49b5fa60ff35a1fa75ab3`
- Original generated file: `exec-b30dc868-b7bd-4e7e-bca0-ee047eefcb28.png`

## `materials/legendary-foil-v1.png`

> Use case: stylized-concept. Asset type: seamless tileable game UI foil
> material texture. Create an original subtle prismatic fantasy foil texture
> for the legendary tier of a digital card frame: premium Japanese-fantasy
> material study, abstract pearlescent film and fine crystalline interference.
> Use an evenly distributed square microstructure with no central emblem or
> large shapes; restrained dark indigo with soft violet, cyan, rose and pale-gold
> iridescence; microscopic foil facets, faint aurora threads and subtle paper
> grain. It must be tileable and quiet enough not to compete with card art. No
> card silhouette, border, frame, characters, objects, text, symbols, logo,
> watermark or signature. Avoid rainbow stripes, loud neon, repeated quadrants,
> lens flares, galaxy scenery and industrial panels.

- SHA-256: `0f7da214b3c0500116499106d8bcd9449bffdbf8052f63138b91aa7dcedfec24`
- Original generated file: `exec-323b01fb-1938-46b3-bfb1-3b88a7ef5022.png`

## Authorization and review

These are original project candidates generated at the user's direction. They
are not third-party redistributable assets and require no third-party
attribution. They remain replaceable until the card-body approval slice is
explicitly accepted. Commercial release still requires human review for
seams, color banding, unintended symbols and similarity to existing IP.
