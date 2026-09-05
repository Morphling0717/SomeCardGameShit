# AnimeV1 product-card illustration batch provenance

Status: generated product candidates for the first playable Oathguard/Pactmage
build. The user approved the AnimeV1 visual direction and integrated card-body
direction before this batch was accepted into the product catalog. Individual
assets remain subject to final human art, trademark, and commercial-release
review.

Generation date: 2026-08-26. Integration date: 2026-08-30.

The twenty-eight PNG files in this directory were made specifically for this
repository with OpenAI's built-in image-generation workflow. No local YGOPro2
asset, Shadowverse asset, third-party screenshot, card frame, logo, audio,
trademark, named artist, or franchise character was supplied as a generation
reference. The existing AnimeV1 ace illustrations were used only as internal
project palette and rendering-direction references where a consistent faction
look was useful. No generated image contains intentionally embedded gameplay
text, card names, costs, statistics, UI chrome, or card frames; Godot composes
those elements separately.

The adjacent `PRODUCT_CARD_ART_ASSET_MANIFEST.json` records the selected file
hash, purpose, generation method, date, and a truthful summary of the prompt
intent for every output. Those summaries are not represented as verbatim or
complete image-generation transcripts: the original tool-call transcript is
not a repository artifact. This document therefore records the reproducible
art direction and the material selection/refinement history without inventing
an exact prompt record.

## Shared generation intent

Every asset requested an original, polished, painterly Japanese-fantasy anime
game illustration on a 2:3 vertical canvas, with a strong thumbnail silhouette,
controlled detail, cinematic light, complete subject composition, and no
readable text, number, UI, card border, logo, watermark, or signature. Prompts
explicitly prohibited copying a named artist, franchise, character,
composition, card frame, or trademark.

- Oathguard art uses ivory, platinum, azure, and sun-gold; complete rings,
  clean enamel, rising arcs, solemn oath magic, and open daylight architecture.
- Pactmage art uses black-violet, ink-blue, crimson, rift-magenta, and antique
  gold; fractured circles, controlled contract energy, and gothic academy
  architecture. Character prompts varied hair, face, silhouette, and role to
  avoid presenting every card as one repeated character.
- Neutral art uses moon-white, mist blue, stone, leather, and antique gold,
  without either profession's crest.

## Selection and refinement notes

- `LO-05.png`: an earlier horse-like interpretation was rejected. The selected
  candidate was refined around the locked light-armored bipedal land-runner
  silhouette.
- `AP-01.png`: an earlier version was rejected because floating pages contained
  text-like marks. The selected image uses blank pages.
- `AP-06.png`: the first candidate resembled the existing Pactmage ace too
  closely; a distinct ash-silver, high-ponytail discipline officer was generated.
  A later image edit removed all remaining paper and text-like detail.
- `AP-08.png`: the selected split-mode composition was edited to remove floating
  paper and text-like marks while preserving the repair-versus-empower choice.
- The remaining twenty-four files are the selected built-in generation outputs
  for their locked subjects. They were copied without raster retouching.

All twenty-eight committed files are `1024x1536` PNGs. Their combined source
payload is 85,716,418 bytes. A conservative BC7/BC3 estimate including the full
mipmap chain is 58,721,152 bytes. The product runtime must use a dynamic identity
texture working set of no more than twenty-four textures; the manifest's 24-item
limit is not permission to preload all thirty-five base card illustrations.

Together with the seven base illustrations already frozen in
`../slice/cards/`, this batch gives every one of the thirty-four constructible
definitions and derived token `LO-T01` one unique real base illustration. The
two locked ace evolution alternatives remain in `../slice/cards/`. The Gate 6A
fourteen-item slice manifest is intentionally unchanged so its approval and CI
evidence remain historically reproducible.

## Human review boundary

The selected batch passed structural review for valid PNG decoding, exact 2:3
dimensions, unique SHA-256 values, subject coverage, and absence of textual PNG
metadata. Automated checks cannot prove anatomical perfection, trademark
clearance, or that decorative fantasy glyphs will never be perceived as
text-like. Before commercial release, every illustration still needs a human
review at full resolution and inside the final card crop, with particular
attention to hands and faces, small decorative pages, faction-wide silhouette
variety, thumbnail distinction, and unintended similarity to third-party IP.
