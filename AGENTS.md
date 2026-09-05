# Development workflow

Preserve unrelated work in progress. Do not reset, stash, commit or push it as part
of tool installation or investigation.

## Godot: observe the real editor and game

For UI, scenes, animation, layout, input, collisions or visual effects, prefer the
configured `godot-scgs` MCP connection to the actual Godot 4.7.2 .NET editor:

1. Confirm the connected project is this repository's `client/godot`.
2. Open the relevant scene and inspect its live scene tree/properties.
3. Run the scene/project and inspect runtime logs and an actual game screenshot.
4. Exercise relevant input and verify the resulting runtime state.
5. Make the smallest change, rebuild C# when needed, and repeat the real run.

Static file edits and automated tests complement this loop; they do not replace
visual and interaction evidence. Never report an editor-only screenshot, an old
legacy smoke, or a successful build as proof that the product v05 game works.
Keep user-private hotseat state covered and never bypass the viewer reveal gate
for a product screenshot.

If MCP is unavailable, diagnose and state the limitation. Configuration changes
may require Codex's MCP restart before this task can call newly installed tools.
Do not silently claim terminal-only MCP probes are in-session tool acceptance.

Use `scripts/dev/start_godot_editor.ps1` to launch with the locked local SDK;
the machine's default PATH may resolve a dotnet installation without the SDK.
Do not run multiple editors for this project or overwrite unsaved human edits.

The MCP addon is development-only. Export with its stripping hook enabled, then
run `scripts/dev/check_godot_mcp_export.py` on the new export and launch it.
Neither addon files nor autoload/plugin references, tokens or probe scenes may
remain in a player build. Keep evidence under ignored `build/` or `artifacts/`.
