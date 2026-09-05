# Godot Editor MCP — local installation and acceptance

Updated: 2026-09-05. This is a development integration, not a product release.

Product follow-up: the subsequent v05 work fixed the recorded startup blockers
and completed a new in-session menu/setup/reveal/mulligan/handoff run. Current
product, export and CI evidence is in [TEST_REPORT](../TEST_REPORT.md); the
installation log below is historical evidence, not a report of current errors.
The installed addon and the editor-written `project.godot` MCP entries remain
local installation work, deliberately separate from the product commit. A clean
checkout has no autoload/plugin references to missing addon files; this local
machine retains the working connection. The bridge's user config and tokens
are never committed.

Resume checkpoint: installation, SDK-path repair, Windows export isolation and
registered Codex MCP A–H communication acceptance are complete. One editor is
open with no running game. Product startup errors were observed and recorded
below; this is not a clean product-playability acceptance. Do not reinstall the
packages or discard current product work to resume.

## Status

Installed: **NPGameDev Godot MCP Toolkit 1.0.0**, with its matching npm bridge.
The Editor is connected on loopback. The continuing Codex turn loaded **36 core
tools** and actually exercised 22 distinct tools through its registered
`mcp__godot_scgs__*` namespace, with 96 retained calls and 10 runtime screenshots.
This includes editor scene/node/script operations, real runtime input and state,
both error streams, screenshots, and reconnect. Terminal SDK diagnostics remain
separately labelled; they are not substituted for in-session acceptance.

No existing product WIP was committed, pushed, reset or stashed. Game logic was
not changed for this installation.

## Installed components

| Component | Location / version |
|---|---|
| Addon source | <https://github.com/NPGameDev/godot-mcp-toolkit>, MIT |
| Addon release | `v1.0.0`, commit `06761dfe4241714e82cc4830777927bd084f1a99` |
| Addon directory | `client/godot/addons/godot_mcp_toolkit` |
| Local bridge | `C:/Users/ASUS/AppData/Local/SCGS/tools/godot-mcp-toolkit/1.0.0` |
| npm dependency | `@npgamedev/godot-mcp-server@1.0.0`, exact version plus local package-lock |
| Codex configuration | `C:/Users/ASUS/.codex/config.toml`, server `godot-scgs` |
| Bound Godot project | `C:/Users/ASUS/Documents/ChatGPT/SomeCardGameShit/client/godot` |
| Godot | `4.7.2.stable.mono.official.ed1daf0bf` |
| SDK / Node | .NET SDK `10.0.400`; Node `22.22.3` |

Release ZIP SHA-256:
`2cb4989204cdea85eff3282f8c44e5a8ce874ef5f39b372b498144bc03b382d6`.

npm tarball SRI:
`sha512-ZPp8+WsF7nDozqwJA/A9TNlRIeBKyL5nUqIlnCGMVEJQjpAxXUoInoWxyTKIWYsgcl/vMXuxYIxZdjZLo6b+cA==`.

`npm audit --omit=dev --audit-level=high` reported zero vulnerabilities at install.
Registry signature validation verified all 96 installed packages; 9 also had
verified attestations. These point-in-time checks are not a security guarantee.

Pre-install config copies are outside Git at
`C:/Users/ASUS/AppData/Local/SCGS/backups/godot-mcp-20260905`.
Do not publish the Codex configuration backup; it may contain private settings.

## Daily launch and connection

Run `scripts/dev/start_godot_editor.ps1` from PowerShell. It selects the installed
Godot .NET editor (or accepts an absolute `-GodotPath`) and sets the locked SDK
**only for the child process**. This is important: the system dotnet location has
no SDK, and DOTNET_ROOT alone did not fix Godot's MSBuild discovery. No global
PATH or SDK was changed. Do not start a second editor for the same project.

Codex starts the stdio bridge automatically from its absolute Node/package path;
no manual npm server command and no floating npx download are needed. Godot loads
the enabled addon and its editor-only runtime autoload. The editor binds
127.0.0.1 in 6550–6560; a launched game has a separate loopback runtime channel
in 6570–6585. Discovery is project-bound, with per-session authentication. Do not
open these ports to the LAN or copy tokens into Git or reports.

The previously suggested **Settings → MCP servers → Restart** path was not
verified on this user's app and must not be repeated as a requirement. On the
continuing turn, the 36 registered tools were already available without that
manual action. No settings-page hunt is needed for the tested core connection.
`codex mcp get godot-scgs` proves configuration readability, not whether an active
turn has loaded new tools. If a future change is not reflected, first inspect
actual registered tools; a subsequent server/app reload may be necessary, but
do not invent an app button or inject private Electron IPC.

The config-file method is documented in the [official MCP configuration
guide](https://learn.chatgpt.com/docs/extend/mcp?surface=cli). This installation
uses the existing user TOML and does not depend on a particular settings UI.

The addon may warn that `.mcp.json` is absent. This installation intentionally
uses Codex TOML; do not create a duplicate Claude-style configuration just to
satisfy that unrelated health hint.

## Tools and limits

Initial tools include scene open/tree/create/delete/save, node properties and
methods, script read/write/edit, game start/stop, runtime screenshot, simulated
input, runtime variables, editor console, runtime logs, and code execution.
The `discover_tools` tool exposes further groups, including runtime node state,
editor screenshots, animation and resource tools. The installed schema snapshot
is in ignored `build/godot-mcp-acceptance/diagnostic-tools.json`.

Dynamic discovery was tested: activation of `runtime_advanced`,
`editor_advanced`, `debugger` and `cleanup` succeeded server-side, but the current
turn's callable namespace remained at 36 tools. In particular,
`editor_wait_for_idle` was not callable. Core `execute_code` successfully read
live runtime trees, visibility, rectangles and isolated probe state instead.

A minimal pinned bridge-only patch now pre-registers those four groups on
startup/reconciliation, preserving upstream handlers, read-only and version
guards. A fresh independent stdio diagnostic returned **52 unique initial
tools**, without calling `discover_tools`. This proves startup registration,
**not** in-session execution of the 16 additions. The already-running Codex
bridge still has its old module loaded; verify the additions after its next
reload before using them as accepted capabilities. No all-112 claim is made.
Patch/rollback details are in the addon's `LOCAL_PATCHES.md`.

The 36 currently registered tools are listed verbatim in ignored
`build/godot-mcp-acceptance/registered-tool-manifest.json`. They cover:

- Scenes: `scene_open`, `scene_get_tree`, `scene_query`, `scene_spatial_map`,
  `scene_create`, `scene_create_node`, `scene_delete_node`, `editor_save_scene`.
- Nodes: `node_get_property`, `node_get_property_list`, `node_set_property`,
  `node_manage`, `node_groups`, `node_set_script`, `node_call_method`,
  `control_set_layout`, `signal_list`, `signal_manage`.
- Scripts: `script_read`, `script_write`, `script_edit`, `script_check`.
- Running/observing: `game_start`, `game_stop`, `execute_code`,
  `runtime_get_script_vars`, `runtime_screenshot`, `input_simulate`,
  `editor_get_console`, `debugger_get_log`.
- Project/discovery: `project_get_settings`, `project_set_setting`,
  `autoload_manage`, `folder_create`, `discover_tools`, `extensions_refresh`.

Supported tools are not blanket authorization to mutate unrelated scenes,
inspect hidden product state, or delete user files. Full gameplay test planning
still belongs to the agent/test harness, not an automatic built-in game solver.

C# compilation remains `dotnet build` with SDK 10.0.400. `script_check` is a
GDScript checker, not a Roslyn/MSBuild diagnostic. Editor and runtime Logger
buffers are genuinely connected, but the upstream debugger error hook does not
guarantee a complete managed exception stack. Test actual error capture rather
than treating an empty buffer as proof of no errors.

For screenshots prefer `image_response_mode: "disk"`, then inspect the actual
returned game PNG. Editor screenshots are not interchangeable with runtime
screenshots. Wait for stable rendered frames before visual assertions.

## Registered-tool acceptance — 2026-09-05

All rows below used registered Codex tools, not a terminal MCP client.

| Check | Actual evidence and result |
|---|---|
| A: connection | Project settings identified SomeCardGameShit/C#/Bootstrap. An isolated `@tool` method, invoked through MCP, returned live engine 4.7.2, hash `ed1daf0bf001b61586d9930840f2f1394092c079`, exact `client/godot/` path, editor PID 41600 and separate runtime PID 40492. |
| B: open scene | `scene_open` opened `res://scenes/menu/MainMenu.tscn`. |
| C: live editor tree | `scene_get_tree` returned MainMenu/BackgroundBase/MenuBackground/SafeArea/MainLayout and children, shown below. Runtime tree separately included C#-created menu pages. |
| D: Inspector | BackgroundBase was ColorRect, visible, color RGBA approximately `(0.008,0.018,0.031,1)`, mouse_filter 2, right/bottom anchors 1. Formal scene properties were not modified. |
| E: lifecycle | Standalone MainMenu, Bootstrap main, and isolated probe started with `runtime_ready:true`, runtime port 6570. Stop reported `was_running:true`, repeated stop `false`. Bootstrap reached the actual v05 ProductMatch, with the product errors below. |
| F: errors | Explicit isolated `SCGS_MCP_EDITOR_PROBE_20260905_EXPECTED` and `SCGS_MCP_RUNTIME_PROBE_20260905_EXPECTED` errors appeared in their respective buffers. A new Bootstrap menu run had no errors or probe markers; product transition errors were retained, not hidden. |
| G: game screenshots | Ten 1280×720 runtime PNGs were returned via MCP, opened and inspected: standalone/menu/settings, product setup/Covered/reveal/pause, keyboard probe and clean menu. No editor screenshot was substituted. |
| H: actual input | Coordinate clicks through `Viewport.push_input` opened Settings; live `is_visible_in_tree()` changed false→true. The actual Cancel button returned to Home without saving settings. KEY_K press/release changed probe counter 0→1 and its visible Label. |

Live editor tree excerpt (not a disk parse):

```text
MainMenu (Control)
├─ BackgroundBase (ColorRect)
├─ MenuBackground (TextureRect)
├─ BlueAtmosphere / VioletAtmosphere / Shade (ColorRect)
├─ GlassBackBuffer (BackBufferCopy)
├─ TopRule (ColorRect)
├─ SafeArea (MarginContainer)
│  └─ MainLayout (HBoxContainer)
│     ├─ BrandColumn (VBoxContainer)
│     │  └─ Protocol, HeaderRule, RiftMark, Title, Subtitle, ...
│     └─ RightShell (PanelContainer)
│        ├─ GlassSurface
│        └─ PageRoot
└─ AboutDialog (AcceptDialog)
```

Extra authoring acceptance created only `res://__mcp_probe/BridgeProbe.tscn`:
created Label and Node, renamed Node, changed Label text, wrote/edited/attached
GDScript, saved, switched scenes and reopened, read persisted properties,
deleted the temporary child through MCP and confirmed its absence in the tree.
All three temporary scene/script/UID files were then removed after stopping and
closing the test editor. Recoverable source copies are in ignored
`build/godot-mcp-acceptance/probe-source/`; no formal game scene was altered.

One mistaken `res://scenes/Bootstrap.tscn` open returned NOT_FOUND and was
corrected to `res://scenes/bootstrap/Bootstrap.tscn`; this is retained in the
96-call evidence, not counted as an engine failure.

The product flow was ordinary Bootstrap → Local Hotseat → new-deck setup →
opaque Covered → actual Reveal button → four-card product mulligan → Pause →
Return to Menu. No smoke/legacy flags, controller shortcut, private-variable
read or viewer-gate bypass was used. This observes the v05 entry, **not** a full
match, visual-quality approval, or an error-free product initialization.

Reconnect was verified after normal editor close (no force kill): PID 41600
ended; the locked launcher started PID 34000; registered MCP calls reconnected,
read the same project identity and MainMenu tree, and ran/stopped a new menu
session. Only editor loopback port 6550 remained; no runtime listener remained.
The new editor buffer had no retained probe error. Core use needs no manual npm
process. The final editor is open on MainMenu with no running game.

Evidence is under ignored `build/godot-mcp-acceptance/`: the authoritative
`registered-tool-calls-final.json`, exact tool manifest, screenshots, probe
source, and separately-labelled `bridge-eager-diagnostic.json`. Never include
auth token contents. Current native/C# preflight passed: Release and Debug C# builds
had zero warnings/errors; five targeted product/native-v05 CTests passed; both
x64 libraries passed the existing 14-export/static-runtime audit and were staged.
The original v04 DLL was preserved under that directory's `native-backup/`.

## Export isolation and rollback

Both export presets explicitly exclude the addon, `.mcp.json` and `__mcp_probe`.
Export with the addon **enabled** so its export hook removes the runtime autoload
from baked settings. File exclusion alone is insufficient. Run:

```powershell
python scripts/dev/check_godot_mcp_export.py --export <fresh-export-directory>
```

The audit checks PCK contents and baked project settings, not just directory
names. Then launch the export and check for missing scripts/autoloads and unwanted
listeners. The audit has 16 isolated tests. macOS was not revalidated by this setup.

The first fresh Windows export exposed an upstream omission: baked
`editor_plugins/enabled` still referenced the excluded addon. A local patch in
the addon's `core/export_strip.gd` temporarily filters only Toolkit's entry and
its `mcp_toolkit/` settings and restores their values/order after export. See
the addon's `LOCAL_PATCHES.md`. The separate bridge startup-list patch does not
enter the game export. No game-code change was needed for isolation. The original
upstream archive/source remain available outside Git.

The second fresh Windows export passed the strict audit: PCK v4, **348 resources
and 20 project settings**. The global script class cache was also inspected and
contained no Toolkit references. The exported EXE ran headlessly for 180 frames
and exited 0, with no exception or missing-autoload error. This proves export
isolation and menu startup, **not** gameplay or display-backed MCP acceptance.
Both project/preset file hashes were unchanged by export. The export is under
`build/godot-mcp-acceptance/export-windows/`; `*-r2.log` records success while
the first failed isolation report is retained.

To remove the integration: first stop this project's game/editor and the Codex
server, remove only `godot-scgs` from the user config, disable/remove only the
Toolkit plugin and `MCPRuntimeServer` autoload, and remove the addon directory.
Rebuild and verify export isolation. Restore backups only after comparing later
changes; never overwrite unrelated current settings or product WIP wholesale.

## Resume product development after acceptance

The interrupted product work is on `codex/product-playable-v1@6e0204e` plus WIP.
It contains product-v05 code and the interactive entrypoint, but existing Godot
`--ci-smoke` still selects the legacy v04 path. The next product work includes
optional-target skipping, surrender access during Reaction/Choice, and genuine
product-v05 full-match acceptance. Those changes are deliberately outside this
MCP installation.

Actual product run findings to address next, not fixed during installation:

1. `ProductMatchScreen.cs:103` queries `%Title`, but ProductMatch.tscn's Title
   lacks `unique_name_in_owner`. The runtime logged Node not found and subsequent
   `_Ready` initialization can be skipped. Fix this scene contract explicitly.
2. The runtime logged an object freed while emitting a signal. The synchronous
   StartButton → StartRequested → ReplaceScreen path calls `Free()` on the old
   screen (`BootstrapController.cs:974`); this is the likely cause. Defer disposal
   without weakening input locks/private-state clearing, then re-test menu,
   return and restart transitions through actual input.
3. Product presentation explicitly selects R3Candidate, so the observed
   industrial floor/old HUD is an unfinished AnimeV1 integration, not an image
   cache issue. The 1280×720 setup text also clips at the panel edge.

MCP communication acceptance is complete for core tools; clean product startup,
new v05 full-match acceptance and advanced-tool in-session acceptance remain
separate follow-up work. No macOS, full pressure or long visual CI matrix was
rerun for this installation.
