#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Author the R1 jewelry frame in Blender 4.5.13. Never run on normal builds.

Coordinates are normalized card UV. Blender (x,y,z) exports to Godot
(x,z,-y): the face points up, with top towards negative Godot Z.
All sculpting is original analytic geometry; imagegen provides only the
concept and neutral albedo. Shallow relief is baked from a real height mesh,
not derived from albedo in the game shader.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "client/godot/assets/visual/anime_v1/card_frame_r1"
SOURCE = ROOT / "art/card_frame_r1"
W, D = 1.58, 1.58 / .75
MATS = {}
OBJECTS = {}
LOW = False


def p(u, v, h):
    return ((u - .5) * W, (.5 - v) * D, h)


def mesh(name, verts, faces, material, group="CommonFrame", smooth=False):
    # Authoring paths may run either way. Every face with a Z component must
    # face the viewer; vertical bevel walls keep their authored winding.
    faces = [tuple(reversed(f)) if (Vector(verts[f[1]])-Vector(verts[f[0]])).cross(
        Vector(verts[f[2]])-Vector(verts[f[0]])).z < 0 else f for f in faces]
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.materials.append(MATS[material])
    data.update()
    uv = data.uv_layers.new(name="CardUV")
    for polygon in data.polygons:
        polygon.use_smooth = smooth
        for i in polygon.loop_indices:
            co = data.vertices[data.loops[i].vertex_index].co
            uv.data[i].uv = (co.x / W + .5, co.y / D + .5)
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    OBJECTS.setdefault(group, []).append(obj)
    return obj


def polygon(name, coords, z, material, group="CommonFrame", bevel=.004):
    n = len(coords)
    verts = [p(u, v, z - .010) for u, v in coords] + [p(u, v, z) for u, v in coords]
    # UV paths are clockwise on screen, counterclockwise in Blender XY.
    faces = [tuple(range(n, 2 * n))] + [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)]
    obj = mesh(name, verts, faces, material, group)
    if bevel:
        mod = obj.modifiers.new("Jeweler bevel", "BEVEL")
        mod.width, mod.segments = bevel, 2
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def curve(points, segments=20):
    """Cubic Bezier authored paths, not copied coordinates."""
    a,b,c,d = (Vector(q) for q in points)
    return [tuple((1-t)**3*a + 3*(1-t)**2*t*b + 3*(1-t)*t*t*c + t**3*d)
            for t in (i/segments for i in range(segments+1))]


def ribbon(name, points, width, z, material="Platinum", group="CommonFrame", taper=False):
    verts = []
    cross = [(-1,0),(-.77,.004),(0,.011),(.77,.004),(1,0)]
    for i, (u,v) in enumerate(points):
        a = Vector(points[max(0,i-1)]); b = Vector(points[min(len(points)-1,i+1)])
        tangent = (b-a).normalized(); normal = Vector((-tangent.y,tangent.x))
        t=i/max(1,len(points)-1)
        factor = max(.10, math.sin(math.pi*t)**.48) if taper else 1
        for side,lift in cross:
            offset = normal*(width*factor*side*.5)
            verts.append(p(u+offset.x,v+offset.y,z+lift*factor))
    faces=[]
    for i in range(len(points)-1):
        for j in range(4):
            k=i*5+j
            faces.append((k,k+5,k+6,k+1))
    return mesh(name,verts,faces,material,group,smooth=True)


def path(name, points, width, z, material="Platinum", group="CommonFrame", taper=False):
    return ribbon(name,curve(points,10 if LOW else 24),width,z,material,group,taper)


def scaled(poly, cx,cy,sx,sy):
    return [(cx+x*sx,cy+y*sy) for x,y in poly]


EMERALD=[(0,-1),(-.70,-.83),(-1,-.42),(-1,.44),(-.66,.88),(0,1),(.66,.88),(1,.44),(1,-.42),(.70,-.83)]
BLADE=[(-.85,-1),(-1,-.30),(-.86,.60),(0,1),(.87,.63),(1,-.25),(.55,-.78),(0,-.93)]
SHIELD=[(0,-.92),(-.90,-1),(-1,-.44),(-.81,.47),(0,1),(.81,.47),(1,-.44),(.90,-1)]


def jewel(name, shape,cx,cy,rx,ry,mat,group="CommonFrame"):
    polygon(name+"_Recess", scaled(shape,cx,cy,rx+ .012,ry+.008),.071,"DarkRecess",group)
    outline=scaled(shape,cx,cy,rx+.009,ry+.006)
    ribbon(name+"_Bezel",outline+[outline[0]],.014,.079,"Gold",group)
    # The top table is broad and flat so real glyphs rest over one plane.
    rings=[(1,.075),(.96,.095),(.91 if name=="Cost" else .71,.111)]
    n=len(shape); verts=[]
    for scale,h in rings:
        verts += [p(u,v,h) for u,v in scaled(shape,cx,cy,rx*scale,ry*scale)]
    faces=[]
    for r in range(2):
        for i in range(n):
            a=r*n+i;b=r*n+(i+1)%n;c=(r+1)*n+i;d=(r+1)*n+(i+1)%n
            faces.extend([(a,b,c),(b,d,c)])
    faces.append(tuple(range(2*n,3*n)))
    mesh(name+"_Crystal",verts,faces,mat,group)
    for i in ((1,3,6,8) if n==10 else (1,3,5,7) if n==8 else ()):
        x,y=shape[i]
        path(name+"_Claw"+str(i),[(cx+x*rx*1.11,cy+y*ry*1.08),
             (cx+x*rx*1.05,cy+y*ry*1.02),(cx+x*rx*.92,cy+y*ry*.93),
             (cx+x*rx*.82,cy+y*ry*.83)],.018,.102,"Platinum",group,True)


def build():
    # Full thin substrate has a shaped rim and no outside rectangular skirt.
    outline=[(.030,.062),(.075,.022),(.46,.022),(.5,.007),(.54,.022),(.925,.022),
             (.974,.062),(.978,.90),(.949,.981),(.55,.982),(.5,.993),(.45,.982),(.05,.981),(.022,.90)]
    polygon("ThinCardSubstrate",outline,.022,"DarkRecess",bevel=.007)
    ribbon("PerimeterRail",outline+[outline[0]],.012,.048,"Gold")
    # Top ribbon: connected plate with truly empty inner text well.
    plaque=[(.196,.071),(.249,.042),(.881,.042),(.961,.083),(.949,.139),(.900,.172),(.244,.172),(.198,.149)]
    polygon("CrownStructuralRibbon",plaque,.067,"Platinum",bevel=.006)
    inset=[(.253,.064),(.880,.064),(.929,.088),(.923,.133),(.880,.152),(.258,.152),(.235,.125)]
    polygon("IndependentNameWell",inset,.073,"Enamel",bevel=.004)
    ribbon("PlaqueFineLip",inset+[inset[0]],.006,.080,"Gold")
    # Fine paired side splines, thicker only where they join upper/lower scrolls.
    for mirror in (False,True):
        def tr(points): return [(1-u if mirror else u,v) for u,v in points]
        suffix="R" if mirror else "L"
        path("OuterSide"+suffix,tr([(.063,.163),(.013,.330),(.029,.711),(.061,.860)]),.014,.058)
        path("InnerSide"+suffix,tr([(.084,.192),(.050,.380),(.050,.695),(.086,.827)]),.007,.067,"Gold")
        wing=[(.070,.250),(.044,.195),(.067,.162),(.030,.055)] if mirror else [(.090,.250),(.047,.104),(.113,.160),(.197,.059)]
        path("TopWing"+suffix,tr(wing),.035,.075,taper=True)
        path("CrownLeaf"+suffix,tr([(.230,.048),(.297,.004),(.348,.059),(.450,.024)]),.024,.081,taper=True)
        path("CrownTendril"+suffix,tr([(.333,.027),(.413,.055),(.462,.006),(.486,.027)]),.012,.087,"Gold",taper=True)
        path("LowerWing"+suffix,tr([(.060,.755),(.039,.859),(.143,.796),(.203,.955)]),.041,.073,taper=True)
        path("LowerScroll"+suffix,tr([(.188,.935),(.231,.994),(.314,.927),(.400,.972)]),.022,.078,taper=True)
        path("LowerAccent"+suffix,tr([(.207,.964),(.293,.979),(.389,.939),(.483,.976)]),.010,.083,"Gold",taper=True)
        # Fine engraved secondary veins omitted at small LOD, not random noise.
        if not LOW:
            path("VeinTop"+suffix,tr([(.071,.195),(.081,.126),(.141,.144),(.180,.094)]),.004,.091,"Gold",taper=True)
            path("VeinFoot"+suffix,tr([(.076,.817),(.111,.873),(.159,.868),(.193,.930)]),.004,.089,"Gold",taper=True)
        # Small leaf veins are analytic ribs, not texture-derived pseudo normals.
        for j in range(2 if LOW else 4):
            v=.155+j*.018
            path("TopLobe"+suffix+str(j),tr([(.055,v+.050),(.079,v),(.098,v),(.12+j*.012,v-.021)]),.022,.082,taper=True)
    # One structural knot accepts three small, original metal inlays. Export
    # all as separate meshes so Godot selects one public faction and clears it
    # for anonymous backs. These are geometry, not identity-bearing textures.
    jewel("CrownKnot",[(0,-1),(-1,0),(0,1),(1,0)],.50,.033,.020,.013,"Enamel")
    path("LowerBridge",[(.221,.958),(.320,.978),(.419,.950),(.5,.972)],.013,.067,"Platinum")
    path("LowerBridgeR",[(.779,.958),(.680,.978),(.581,.950),(.5,.972)],.013,.067,"Platinum")
    jewel("TypeKnot",[(0,-1),(-1,0),(0,1),(1,0)],.5,.959,.026,.024,"Enamel")
    segments=12 if LOW else 24
    def motif_ring(radius,group):
        points=[(.5+radius*math.cos(i*2*math.pi/segments),
                 .959+radius*.75*math.sin(i*2*math.pi/segments)) for i in range(segments+1)]
        ribbon("InlayRing",points,.0018,.112,"Gold",group)
    sun="CommonFrameMotifOathguard"
    motif_ring(.007,sun)
    for i in range(8):
        angle=i*math.pi/4
        ribbon("SunRay"+str(i),[(.5+r*math.cos(angle),.959+r*.75*math.sin(angle))
            for r in (.010,.016)],.002,.112,"Gold",sun)
    rift="CommonFrameMotifPactmage"
    polygon("RiftLeft",[(.499,.947),(.486,.959),(.496,.957),(.491,.971),(.503,.957)],
            .123,"Platinum",rift,bevel=0)
    polygon("RiftRight",[(.508,.947),(.503,.961),(.514,.954)],.123,"Gold",rift,bevel=0)
    motif_ring(.012,"CommonFrameMotifNeutral")
    jewel("Cost",EMERALD,.139,.125,.104,.100,"Emerald")
    jewel("Attack",BLADE,.147,.872,.110,.108,"Sapphire","AttackFoot")
    jewel("Health",SHIELD,.853,.872,.110,.108,"Ruby","HealthFoot")
    # Foot connections remain inside the card. No enormous disc badges.
    for group,cx,sgn in [("AttackFoot",.147,1),("HealthFoot",.853,-1)]:
        path(group+"Heel",[(cx-sgn*.086,.845),(cx+sgn*.112,.975),(cx+sgn*.163,.944),(cx+sgn*.119,.903)],.024,.085,"Platinum",group,True)


def materials():
    values={"Platinum":((.71,.74,.77,1),.82,.27),"Gold":((.61,.43,.19,1),.82,.23),
      "Enamel":((.080,.105,.18,1),.18,.19),"Emerald":((.009,.21,.08,1),.24,.13),
      "Sapphire":((.018,.073,.29,1),.22,.13),"Ruby":((.34,.009,.040,1),.22,.14),
      "DarkRecess":((.078,.068,.092,1),.5,.43)}
    for name,(color,metal,rough) in values.items():
        mat=bpy.data.materials.new(name);mat.diffuse_color=color;mat.use_nodes=True
        bs=mat.node_tree.nodes.get("Principled BSDF")
        bs.inputs["Base Color"].default_value=color
        bs.inputs["Metallic"].default_value=metal;bs.inputs["Roughness"].default_value=rough
        MATS[name]=mat


def export(lod):
    global OBJECTS, LOW
    bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
    OBJECTS={};LOW=lod=="low";build()
    root=bpy.data.objects.new("CardFrameMaster",None);bpy.context.collection.objects.link(root)
    for group,objects in OBJECTS.items():
        bpy.ops.object.select_all(action="DESELECT")
        for ob in objects:ob.select_set(True)
        bpy.context.view_layer.objects.active=objects[0]
        bpy.ops.object.join()
        ob=bpy.context.object;ob.name=group;ob.parent=root
        triangulate=ob.modifiers.new("Export triangulation","TRIANGULATE")
        bpy.ops.object.modifier_apply(modifier=triangulate.name)
    path=OUT/("frame-master-low.glb" if LOW else "frame-master.glb")
    bpy.ops.export_scene.gltf(filepath=str(path),export_format="GLB",export_yup=True,
        export_apply=True,export_texcoords=True,export_normals=True,export_materials="EXPORT")
    if not LOW:
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/"card_frame_r1.blend"),check_existing=False)


def bake():
    """Cycles selected-to-active bakes of restrained original shallow incision."""
    bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
    scene=bpy.context.scene;scene.render.engine="CYCLES";scene.cycles.samples=8
    scene.cycles.device="CPU";scene.render.bake.use_selected_to_active=True
    scene.render.bake.cage_extrusion=.015;scene.render.bake.max_ray_distance=.035
    scene.render.bake.margin=8
    low=mesh("BakeReceiver",[(-.5,-.5,0),(.5,-.5,0),(.5,.5,0),(-.5,.5,0)],[(0,1,2,3)],"Platinum")
    for i,uv in enumerate(((0,0),(1,0),(1,1),(0,1))):low.data.uv_layers.active.data[i].uv=uv
    n=256;verts=[];faces=[]
    for y in range(n+1):
        for x in range(n+1):
            u=x/n;v=y/n
            # Shallow interlaced curved incision, deliberately not albedo/noise-derived.
            a=math.sin(u*math.tau*6 + math.sin(v*math.tau*3)*.45)
            b=math.sin(v*math.tau*6 + math.sin(u*math.tau*3)*.45)
            groove=math.exp(-a*a*170)*math.exp(-b*b*1.4)
            h=.004-.0009*groove
            verts.append((u-.5,v-.5,h))
    for y in range(n):
        for x in range(n):
            a=y*(n+1)+x;faces.append((a,a+1,a+n+2,a+n+1))
    high=mesh("EditableShallowIncisions",verts,faces,"Platinum",smooth=True)
    low.data.materials.clear()
    lowmat=bpy.data.materials.new("BakeTarget");lowmat.use_nodes=True;low.data.materials.append(lowmat)
    node=lowmat.node_tree.nodes.new("ShaderNodeTexImage")
    for role,baketype in (("normal","NORMAL"),("ao","AO"),("roughness","ROUGHNESS")):
        img=bpy.data.images.new("Frame relief "+role,width=1024,height=1024,alpha=False)
        img.colorspace_settings.name="Non-Color";node.image=img;lowmat.node_tree.nodes.active=node
        bpy.ops.object.select_all(action="DESELECT");high.select_set(True);low.select_set(True)
        bpy.context.view_layer.objects.active=low
        bpy.ops.object.bake(type=baketype)
        img.filepath_raw=str(OUT/("relief-"+role+".png"));img.file_format="PNG";img.save()
    # Keep the editable source of the bake in its own blend, not exported.
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/"engraving-bake.blend"),check_existing=False)


def entry(path):
    return {"path":path.relative_to(ROOT).as_posix(),"sha256":hashlib.sha256(path.read_bytes()).hexdigest()}


def main():
    parser=argparse.ArgumentParser();parser.add_argument("--skip-bake",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if bpy.app.version[:3]!=(4,5,13):raise RuntimeError("Authoring requires Blender 4.5.13")
    bpy.context.preferences.filepaths.save_version=0
    OUT.mkdir(parents=True,exist_ok=True);SOURCE.mkdir(parents=True,exist_ok=True)
    materials()
    if not args.skip_bake:bake()
    export("high");export("low")
    manifest={"schema_version":1,"kind":"card-frame-r1","blender_version":"4.5.13",
      "sources":{"blend":entry(SOURCE/"card_frame_r1.blend"),"script":entry(Path(__file__)),
                 "concept":entry(SOURCE/"concept-master-r1.png")},
      "models":[dict(entry(OUT/("frame-master"+("-low" if lod=="low" else "")+".glb")),lod=lod) for lod in ("high","low")],
      "textures":[dict(entry(OUT/("relief-"+role+".png")),role=role) for role in ("normal","ao","roughness")]
                +[dict(entry(OUT/"platinum-albedo-source.png"),role="engraving")],
      "extra_sources":[entry(SOURCE/"engraving-bake.blend")],
      "coordinates":"Godot metres: X width 1.58, Z depth 2.106667, Y face upward; maximum relief 0.13",
      "text_clearance":.018,"approval":"candidate_not_user_approved"}
    (SOURCE/"frame-manifest.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")


if __name__=="__main__":main()
