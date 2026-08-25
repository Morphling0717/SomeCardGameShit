#!/usr/bin/env python3
"""Generate the original R3 open-arena mechanical dressing as a deterministic GLB.

Run with the repository-pinned Blender build:

  blender.exe --background --python generate_r3_arena_machinery.py -- \
      --output ../r3_arena_machinery.glb

The output contains scenery only. It deliberately has no perimeter rail, board
surface, card slot, collision, camera, light, text, logo, or gameplay metadata.
"""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


GAMEPLAY_HALF_WIDTH = 9.9
GAMEPLAY_HALF_DEPTH = 8.3
ENVIRONMENT_HALF_WIDTH = 23.0
ENVIRONMENT_HALF_DEPTH = 17.0


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "r3_arena_machinery.glb",
    )
    return parser.parse_args(argv)


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.materials,
        bpy.data.cameras,
        bpy.data.lights,
    ):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def create_material(
    name: str,
    base_color: tuple[float, float, float, float],
    metallic: float,
    roughness: float,
    emission: tuple[float, float, float, float] | None = None,
    emission_strength: float = 0.0,
) -> bpy.types.Material:
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = base_color
    material.metallic = metallic
    material.roughness = roughness
    principled = material.node_tree.nodes.get("Principled BSDF")
    if principled is not None:
        principled.inputs["Base Color"].default_value = base_color
        principled.inputs["Metallic"].default_value = metallic
        principled.inputs["Roughness"].default_value = roughness
        emission_input = principled.inputs.get("Emission Color") or principled.inputs.get("Emission")
        if emission is not None and emission_input is not None:
            emission_input.default_value = emission
        strength_input = principled.inputs.get("Emission Strength")
        if strength_input is not None:
            strength_input.default_value = emission_strength
    return material


def godot_position(x: float, y: float, z: float) -> tuple[float, float, float]:
    """Map Godot's X/Y-up/Z coordinates to Blender's X/Y/Z-up coordinates."""

    return (x, -z, y)


def godot_dimensions(x: float, y: float, z: float) -> tuple[float, float, float]:
    return (x, z, y)


def add_empty(name: str, x: float, y: float, z: float, yaw_degrees: float = 0.0) -> bpy.types.Object:
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.location = godot_position(x, y, z)
    obj.rotation_euler[2] = math.radians(-yaw_degrees)
    return obj


def add_box(
    name: str,
    dimensions: tuple[float, float, float],
    location: tuple[float, float, float],
    material: bpy.types.Material,
    bevel: float,
    parent: bpy.types.Object | None = None,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=godot_position(*location))
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = godot_dimensions(*dimensions)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0.0:
        modifier = obj.modifiers.new(name="EdgeSoftening", type="BEVEL")
        modifier.width = bevel
        modifier.segments = 3
        modifier.limit_method = "ANGLE"
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.materials.append(material)
    obj.parent = parent
    if parent is not None:
        obj.location = godot_position(*location)
    return obj


def add_cylinder(
    name: str,
    radius: float,
    height: float,
    location: tuple[float, float, float],
    material: bpy.types.Material,
    vertices: int = 16,
    parent: bpy.types.Object | None = None,
    lay_along_depth: bool = False,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=height,
        location=godot_position(*location),
    )
    obj = bpy.context.object
    obj.name = name
    if lay_along_depth:
        obj.rotation_euler[0] = math.radians(90.0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    bevel = obj.modifiers.new(name="EdgeSoftening", type="BEVEL")
    bevel.width = min(radius * 0.24, 0.045)
    bevel.segments = 2
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    obj.data.materials.append(material)
    obj.parent = parent
    if parent is not None:
        obj.location = godot_position(*location)
    return obj


def add_fastener(
    name: str,
    x: float,
    z: float,
    parent: bpy.types.Object,
    material: bpy.types.Material,
) -> None:
    add_cylinder(name, 0.055, 0.032, (x, 0.435, z), material, 10, parent)


def service_island(
    name: str,
    position: tuple[float, float],
    yaw_degrees: float,
    core_variant: bool,
    materials: dict[str, bpy.types.Material],
) -> None:
    root = add_empty(name, position[0], 0.0, position[1], yaw_degrees)
    add_box(f"{name}_Base", (2.4, 0.22, 1.55), (0.0, 0.11, 0.0), materials["foundation"], 0.08, root)
    add_box(f"{name}_Inset", (2.02, 0.055, 1.18), (0.0, 0.235, 0.0), materials["groove"], 0.035, root)
    add_box(f"{name}_Top", (1.68, 0.2, 1.02), (0.0, 0.35, 0.0), materials["dark"], 0.075, root)
    add_box(f"{name}_OuterRib", (0.13, 0.25, 1.3), (-0.98, 0.36, 0.0), materials["mid"], 0.035, root)
    add_box(f"{name}_CrossRib", (1.48, 0.12, 0.12), (0.12, 0.49, -0.42), materials["mid"], 0.028, root)
    add_cylinder(f"{name}_Pipe", 0.105, 1.46, (0.67, 0.51, 0.0), materials["mid"], 16, root, True)
    if core_variant:
        add_cylinder(f"{name}_Core", 0.45, 0.46, (-0.38, 0.62, 0.0), materials["mid"], 6, root)
        add_cylinder(f"{name}_CoreCap", 0.27, 0.08, (-0.38, 0.89, 0.0), materials["indicator"], 16, root)
    else:
        for index, x in enumerate((-0.44, -0.12, 0.2), start=1):
            add_box(
                f"{name}_Vent{index:02d}",
                (0.18, 0.17, 0.72),
                (x, 0.52, 0.08),
                materials["groove"],
                0.022,
                root,
            )
        add_box(f"{name}_Indicator", (0.64, 0.035, 0.07), (0.42, 0.575, -0.31), materials["indicator"], 0.012, root)
    for index, (x, z) in enumerate(((-0.92, -0.52), (-0.92, 0.52), (0.92, -0.52), (0.92, 0.52)), start=1):
        add_fastener(f"{name}_Fastener{index:02d}", x, z, root, materials["mid"])


def gantry_bank(
    name: str,
    position: tuple[float, float],
    yaw_degrees: float,
    materials: dict[str, bpy.types.Material],
) -> None:
    root = add_empty(name, position[0], 0.0, position[1], yaw_degrees)
    add_box(f"{name}_Base", (3.2, 0.3, 1.08), (0.0, 0.15, 0.0), materials["foundation"], 0.09, root)
    add_box(f"{name}_Recess", (2.78, 0.055, 0.76), (0.0, 0.325, 0.0), materials["groove"], 0.035, root)
    add_box(f"{name}_RibLeft", (0.18, 0.58, 0.94), (-1.27, 0.48, 0.0), materials["mid"], 0.04, root)
    add_box(f"{name}_RibRight", (0.18, 0.58, 0.94), (1.27, 0.48, 0.0), materials["mid"], 0.04, root)
    add_cylinder(f"{name}_Core", 0.47, 0.52, (0.0, 0.59, 0.0), materials["dark"], 8, root)
    add_cylinder(f"{name}_CoreCap", 0.27, 0.08, (0.0, 0.89, 0.0), materials["indicator"], 16, root)
    add_cylinder(f"{name}_ConduitLeft", 0.09, 1.15, (-0.68, 0.47, 0.0), materials["mid"], 14, root, True)
    add_cylinder(f"{name}_ConduitRight", 0.09, 1.15, (0.68, 0.47, 0.0), materials["mid"], 14, root, True)


def quantize_generated_meshes(decimal_places: int = 6) -> None:
    """Remove modifier-thread least-significant-bit drift before GLB export."""

    for mesh in bpy.data.meshes:
        for vertex in mesh.vertices:
            vertex.co = tuple(round(float(component), decimal_places) for component in vertex.co)
        # The generated materials are scalar PBR colors, so UV layers are both
        # unused and a source of modifier-thread sub-ULP drift in Blender GLBs.
        while mesh.uv_layers:
            mesh.uv_layers.remove(mesh.uv_layers[0])
        mesh.update(calc_edges=True)


def validate_open_gameplay_footprint() -> int:
    """Prove every scenery mesh stays outside the invisible gameplay rectangle."""

    bpy.context.view_layer.update()
    mesh_count = 0
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        mesh_count += 1
        world_corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
        godot_x = [corner.x for corner in world_corners]
        godot_z = [-corner.y for corner in world_corners]
        minimum_x, maximum_x = min(godot_x), max(godot_x)
        minimum_z, maximum_z = min(godot_z), max(godot_z)
        separated_from_play = (
            maximum_x <= -GAMEPLAY_HALF_WIDTH
            or minimum_x >= GAMEPLAY_HALF_WIDTH
            or maximum_z <= -GAMEPLAY_HALF_DEPTH
            or minimum_z >= GAMEPLAY_HALF_DEPTH
        )
        inside_environment = (
            minimum_x >= -ENVIRONMENT_HALF_WIDTH
            and maximum_x <= ENVIRONMENT_HALF_WIDTH
            and minimum_z >= -ENVIRONMENT_HALF_DEPTH
            and maximum_z <= ENVIRONMENT_HALF_DEPTH
        )
        if not separated_from_play:
            raise RuntimeError(f"Scenery mesh overlaps gameplay footprint: {obj.name}")
        if not inside_environment:
            raise RuntimeError(f"Scenery mesh exceeds open-floor environment: {obj.name}")
    if mesh_count == 0:
        raise RuntimeError("R3 arena machinery generation produced no meshes")
    return mesh_count


def main() -> None:
    args = parse_args()
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    clear_scene()

    materials = {
        "foundation": create_material("Foundation", (0.045, 0.052, 0.057, 1.0), 0.84, 0.38),
        "dark": create_material("GunmetalDark", (0.078, 0.086, 0.092, 1.0), 0.8, 0.31),
        "mid": create_material("GunmetalMid", (0.225, 0.242, 0.252, 1.0), 0.72, 0.29),
        "groove": create_material("Recess", (0.015, 0.018, 0.02, 1.0), 0.3, 0.82),
        "indicator": create_material(
            "WarmIndicator",
            (0.76, 0.61, 0.34, 1.0),
            0.12,
            0.22,
            (0.58, 0.39, 0.14, 1.0),
            1.35,
        ),
    }

    service_island("FarLeftService", (-11.65, -5.85), -8.0, False, materials)
    service_island("FarRightService", (11.75, -4.95), 188.0, False, materials)
    service_island("NearLeftService", (-11.75, 4.85), 10.0, True, materials)
    service_island("NearRightService", (11.65, 6.05), 170.0, True, materials)
    gantry_bank("FarGantryLeft", (-5.25, -10.35), -4.0, materials)
    gantry_bank("FarGantryRight", (5.55, -10.55), 5.0, materials)

    root = add_empty("R3ArenaMechanicalDressing", 0.0, 0.0, 0.0)
    for obj in list(bpy.context.scene.objects):
        if obj is not root and obj.parent is None:
            obj.parent = root

    quantize_generated_meshes()
    mesh_count = validate_open_gameplay_footprint()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=str(output),
        export_format="GLB",
        export_apply=True,
        export_yup=True,
        export_materials="EXPORT",
        export_cameras=False,
        export_lights=False,
        export_extras=False,
    )
    print(f"SCGS_R3_ARENA_FOOTPRINT_OK meshes={mesh_count}")
    print(f"SCGS_R3_ARENA_GLB_OK path={output}")


if __name__ == "__main__":
    main()
