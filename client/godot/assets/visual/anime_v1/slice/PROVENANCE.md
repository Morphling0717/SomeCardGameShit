# Gate 6A AnimeV1 slice provenance

Status: project-bound visual candidate, pending user approval. Generation date: 2026-08-26.

All fourteen raster candidates were created for this repository with OpenAI's built-in image-generation workflow in new-generation mode. No input image, local YGOPro2 asset, third-party screenshot, logo, card frame, audio, trademark or artist reference was supplied. The generated source artifacts remain in the Codex generated-images workspace; the selected outputs were copied unchanged into this directory. Text, card names, costs, stats, rules, frames and interface chrome are rendered by Godot and are not part of these images.

## Shared prompt block

The following requirements are part of every prompt below:

> Create an original, ornate, painterly Japanese-fantasy anime game illustration for a new card game. Use polished commercial key-art rendering, crisp silhouette, expressive face or unmistakable subject, controlled detail, dramatic cinematic light, strong thumbnail readability and an original world design. Do not imitate a named artist, franchise, character, composition, card frame, logo or trademark. Include no readable text, numbers, UI, watermark, signature or border. Keep the content suitable for a teen-rated fantasy game.

曜誓 prompts additionally require an ivory, platinum, azure and sun-gold palette with complete rings, rising arcs, clean enamel and solemn oath magic. 渊契 prompts additionally require black-violet, ink-blue, crimson, rift-magenta and antique-gold with broken circles, floating blank pages and luminous contract energy. Neutral prompts use moon-white, mist blue, stone and antique gold without either faction emblem.

## Selected assets and complete subject prompts

### leaders/aurelia-master.png

> Transparent-background character master, 2:3 vertical canvas. An original adult oath-guardian princess, Aurelia: very long pale-gold hair, clear azure eyes, layered ivory-and-platinum ceremonial armor, royal blue fabric, fine sun-gold filigree, a non-religious segmented solar halo cape and a slender oath sword with a closing ring guard. Calm, reliable expression; one open hand accepts a promise. Full body, centered, all hair, cape, weapon and boots inside the canvas, clean isolated alpha silhouette, no floor, scenery, fog rectangle or baked checkerboard.

The first leader candidate had a checker pattern rendered into RGB pixels. The selected file is a second built-in generation specifically requesting a true alpha cutout; it was copied without post-processing. PNG mode is RGBA and transparent corner pixels are audited.

### leaders/theraea-master.png

> Transparent-background character master, 2:3 vertical canvas. An original adult pact-mage academy director, Theraea: very long black-violet hair, crimson-violet eyes, a closed high-collar asymmetrical black and ink-blue academy coat with long tails, layered modest skirt and trousers, fine antique-gold contract clasps, one blank floating contract book and one held book. She confidently controls a narrow magenta rift with her raised hand. Full body, centered, all hair, coat, books and boots inside the canvas, clean isolated alpha silhouette, no hat, horns, exposed lingerie, floor, scenery, glow rectangle or baked checkerboard.

The selected file is the final built-in alpha-isolation generation after rejecting RGB-background variants. It was copied without post-processing. PNG mode is RGBA and transparent corner pixels are audited.

### cards/LO-03.png

> 2:3 vertical amulet illustration. Morning Bell Oath Monument in a luminous open ivory courtyard: a monumental free-standing ring-shaped bell stele, three distinct concentric countdown bands awakening one by one, platinum stone, blue enamel channels and warm sunrise rays. A subtle guardian silhouette gathers at the base, but the enduring monument remains the unmistakable central subject. No people in the foreground.

### cards/LO-07.png

> 2:3 vertical trap illustration. Exactly three oath-guardian shield soldiers close their separate round shields into one translucent blue-gold barrier at the instant before an enemy strike lands. The final crack in the barrier visibly seals with a thin sun-gold line. Defensive tension and clear left-to-right cause-and-effect; nobody defeated or injured; three guards must remain countable at thumbnail size.

### cards/LO-11.png

> 2:3 vertical follower illustration. Oath Grand Commander Leonie, an original pale-gold-haired adult knight distinct from Aurelia, charges toward the viewer on a heavily armored white pegasus. She holds a radiant straight sword and large circular sun shield; a fully closed golden solar ring opens behind them in a bright azure sky. Powerful 8/8 finisher silhouette, dynamic foreshortening, both rider and pegasus face readable.

### cards/LO-11-evolved.png

> 2:3 vertical evolved alternate illustration of the same Leonie and the same armored white pegasus. Preserve her face, pale-gold tied hair, armor motifs, sword and circular shield, but elevate the scene: luminous platinum wing plates, a complete double solar ring, blue-gold oath particles and a stronger forward dive through dawn clouds. This must read as the same character in an evolved state, not a new costume or leader.

### cards/AP-03.png

> 2:3 vertical spell illustration. A single crimson-magenta spear formed from folded blank contract ribbons pierces an enemy armor projection. Four broken violet rifts light in sequence behind the spear, visually escalating from three damage to five. Strong diagonal impact, one clear target, luminous ink and paper fragments, no gore and no caster portrait dominating the frame.

### cards/AP-05.png

> 2:3 vertical field illustration. Zero-Hour Lecture Hall, an immense gothic fantasy academy amphitheater frozen exactly at midnight. Tiered desks descend toward a circular dais; blank pages travel from the front seats along violet time rails toward the rear while one page sinks into the far depth, clearly suggesting draw then place on deck bottom. Black-violet stone, antique gold mechanisms, magenta rifts, no students or readable writing.

### cards/AP-11.png

> 2:3 vertical follower illustration. Forbidden Graduate Noctia, an original young adult pact-mage distinct from Theraea, bursts forward after tearing open an on-time seal. Four debt marks glow behind her; a fitted but non-revealing black-violet graduation coat streams into magenta haste trails. Determined aggressive expression, one hand shapes a contract blade, full action silhouette, no hat, horns or school logo.

### cards/AP-11-evolved.png

> 2:3 vertical evolved alternate illustration of the same Noctia. Preserve her face, dark-violet hair, graduation coat and contract blade while intensifying four debt seals into a broken halo, crimson-magenta rift wings and a high-speed frontal lunge. Stronger lighting and particles show evolution; it must remain recognizably the same character and must not become Theraea.

### cards/NT-04.png

> 2:3 vertical neutral spell illustration. An original neutral boundary arbiter stands before one vertical moon-white spatial seam and makes a clear two-way judgment gesture. On the left, a hostile follower projection fractures under a precise four-point impact; on the right, an amulet and a field core are cleanly dismantled into geometric light. Balanced split composition, mist-blue stone and antique gold, neither faction emblem, no leader being harmed.

### shared/card-back.png

> 2:3 vertical universal hidden card back, flat centered ornamental design. Deep-indigo base, a silver outer ring and gold inner ring surrounding an abstract sun disc pierced by one narrow purple rift. Fine original oath-line engraving, restrained corner ornaments and a double metallic border. Symmetric enough for repeated hidden cards but with a readable upright orientation. No eye, spiral, religious symbol, text, logo, watermark or trading-card-game imitation.

### menu/menu-key-art.png

> 16:9 wordless menu key art for the same original world. Aurelia stands in the left foreground with ivory-platinum armor, azure cloth and warm solar rings; Theraea stands deeper near center with black-violet formal academy clothing, blank floating books and a controlled magenta rift. Between them is a distant open fantasy duel stage beneath a moonlit citadel. Reserve the rightmost third as calmer dark-indigo negative space for Godot navigation. No UI, title, logo, text or card frame.

### arena/open-fantasy-arena.png

> 16:9 wordless open fantasy arena background viewed from a fixed elevated oblique duel-camera angle. A broad floating moonstone platform sits within a luminous sky citadel, with no enclosing table rectangle, perimeter frame, walls or pre-painted gameplay slots. The near side catches ivory-blue dawn light; the far side catches restrained black-violet and magenta rift light; a subtle neutral seam crosses the middle. Leave clear low-detail space for five main-board runes, three tactic runes and one field rune per side. No characters, cards, UI, labels, logo or watermark.

## Authorization and review status

These images are original project candidates produced through the built-in image-generation workflow at the user's direction. They are not third-party redistributable assets and require no third-party attribution. They remain temporary candidates until the user explicitly approves the Gate 6A visual slice. A later commercial release still requires human review for anatomy, hands, costume continuity, thumbnail readability, accidental text, watermarks and potential IP similarity.
