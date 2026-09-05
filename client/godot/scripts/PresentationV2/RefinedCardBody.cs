// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.CardFaces;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>Authored Blender frame, disjoint material domains and one clean artwork window.</summary>
internal sealed partial class RefinedCardBody : Node3D
{
    private static readonly PackedScene High = GD.Load<PackedScene>(CardFrameMaster.RootPath+"frame-master.glb");
    private static readonly PackedScene Low = GD.Load<PackedScene>(CardFrameMaster.RootPath+"frame-master-low.glb");
    private static readonly Dictionary<ProductCardFaction,Dictionary<string,Material>> Palettes=new();
    private static readonly Shader CrystalShader=GD.Load<Shader>("res://shaders/refined_card_crystal.gdshader");
    private static readonly Shader ArtShader = new() { Code="""
        shader_type spatial;
        render_mode unshaded, cull_disabled;
        uniform sampler2D artwork : source_color, filter_linear_mipmap_anisotropic;
        uniform vec4 crop = vec4(0.,0.,1.,1.);
        void fragment() {
            vec2 q=abs(UV-vec2(.5))-vec2(.484,.484);
            float d=length(max(q,vec2(0.)))+min(max(q.x,q.y),0.)-.016;
            if(d>0.) discard;
            ALBEDO=texture(artwork,crop.xy+UV*crop.zw).rgb;
        }
        """ };
    private readonly ShaderMaterial artMaterial=new(){Shader=ArtShader};
    private Node3D high=null!, low=null!;
    private MeshInstance3D art=null!, highlight=null!;
    private Node3D? active;
    private readonly StandardMaterial3D highlightMaterial=new() {
        ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor=new("e7f8ed"), Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode=BaseMaterial3D.CullModeEnum.Disabled,
    };
    internal bool HasIdentity {get;private set;}
    internal bool Follower {get;private set;}
    internal bool IsBound => HasIdentity && IsVisibleInTree() && active?.IsVisibleInTree()==true &&
        art.IsVisibleInTree() && artMaterial.GetShaderParameter("artwork").AsGodotObject() is Texture2D;

    public override void _Ready()
    {
        high=High.Instantiate<Node3D>();high.Name="FineFrame";AddChild(high);
        low=Low.Instantiate<Node3D>();low.Name="SmallFrame";AddChild(low);
        art=new MeshInstance3D {Name="ArtworkWindow",Mesh=new QuadMesh(),
            RotationDegrees=new(-90,0,0),MaterialOverride=artMaterial,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(art);
        highlight=new MeshInstance3D {Name="OuterFilletHint",Mesh=MakeContour(),MaterialOverride=highlightMaterial,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(highlight);
        ClearSensitive();
    }

    internal void Bind(CardFaceComposition face)
    {
        ClearSensitive();
        var palette=Palette(face.ViewModel.Faction);
        active=face.Layout.Context==CardFaceContext.Field ? low:high;
        Follower=face.ViewModel.Kind==ProductCardKind.Follower;
        foreach(MeshInstance3D part in Meshes(active))
        {
            string name=part.Name.ToString();
            part.Visible=name.StartsWith("CommonFrameMotif",StringComparison.Ordinal)
                ? name=="CommonFrameMotif"+face.ViewModel.Faction
                : Follower || !(name.StartsWith("AttackFoot",StringComparison.Ordinal) ||
                    name.StartsWith("HealthFoot",StringComparison.Ordinal));
            for(int i=0;i<part.Mesh.GetSurfaceCount();i++)
            {
                string slot=part.Mesh.SurfaceGetMaterial(i)?.ResourceName??"";
                if(!palette.TryGetValue(slot,out Material? mat))
                    throw new InvalidOperationException($"Unexpected card frame material: {slot}");
                part.SetSurfaceOverrideMaterial(i,mat);
            }
        }
        CardFaceRect box=face.Layout.ArtWindow;
        float depth=BattlefieldPerspective.CardWidth/CardFaceLayout.CardAspectRatio;
        ((QuadMesh)art.Mesh).Size=new(box.Width*BattlefieldPerspective.CardWidth,box.Height*depth);
        art.Position=new((box.X+box.Width*.5f-.5f)*BattlefieldPerspective.CardWidth,.041f,
            (box.Y+box.Height*.5f-.5f)*depth);
        artMaterial.SetShaderParameter("artwork",GD.Load<Texture2D>(face.ArtPath));
        var uv=face.ArtCrop;artMaterial.SetShaderParameter("crop",new Vector4(uv.U,uv.V,uv.Width,uv.Height));
        active.Visible=true;art.Visible=true;HasIdentity=true;Visible=true;
    }

    internal void SetHighlight(float value)
    {
        highlight.Visible=HasIdentity && value>0;
        highlightMaterial.AlbedoColor=value>1 ? new Color("fff0c1"):new Color("b6e9d4");
    }

    internal void ClearSensitive()
    {
        HasIdentity=false;Follower=false;Visible=false;active=null;
        artMaterial.SetShaderParameter("artwork",default(Variant));
        artMaterial.SetShaderParameter("crop",new Vector4(0,0,1,1));
        foreach(Node3D? frame in new[]{high,low})
        {
            if(frame is null) continue;
            frame.Visible=false;
            foreach(MeshInstance3D part in Meshes(frame))
            {
                part.Visible=false;
                for(int i=0;i<part.Mesh.GetSurfaceCount();i++)part.SetSurfaceOverrideMaterial(i,null);
            }
        }
        if(art is not null)art.Visible=false;
        if(highlight is not null)highlight.Visible=false;
        highlightMaterial.AlbedoColor=Colors.White;
        foreach(Node node in Descendants(this))foreach(StringName key in node.GetMetaList())node.RemoveMeta(key);
    }

    private static Dictionary<string,Material> Palette(ProductCardFaction faction)
    {
        if(Palettes.TryGetValue(faction,out var existing))return existing;
        var result=new Dictionary<string,Material>();
        bool oath=faction==ProductCardFaction.Oathguard,pact=faction==ProductCardFaction.Pactmage;
        (string Name,string Color,float Metal,float Rough)[] specs=[
            ("Platinum",oath?"e8e3d9":pact?"d1cddd":"d9e0e4",.68f,.28f),
            ("Gold",oath?"d3b77e":pact?"a294bc":"aebbc9",.74f,.24f),
            ("Enamel",oath?"e1d2b2":pact?"382642":"b6bfcb",.10f,.22f),
            ("Emerald","087342",.18f,.13f),("Sapphire","164492",.15f,.13f),
            ("Ruby","9d1e40",.15f,.13f),("DarkRecess","494451",.36f,.44f),
        ];
        foreach(var s in specs)
        {
            if(s.Name is "Emerald" or "Sapphire" or "Ruby")
            {
                var crystal=new ShaderMaterial {Shader=CrystalShader,ResourceName="R1:SharedGem:"+s.Name};
                crystal.SetShaderParameter("tint",new Color(s.Color));
                crystal.SetShaderParameter("jewel_uv",s.Name switch {
                    "Emerald"=>new Vector4(.139f,.125f,.104f,.100f),
                    "Sapphire"=>new Vector4(.147f,.872f,.110f,.108f),
                    _=>new Vector4(.853f,.872f,.110f,.108f),
                });
                result[s.Name]=crystal;continue;
            }
            var mat=new StandardMaterial3D {ResourceName="R1:"+faction+":"+s.Name,
                AlbedoColor=new(s.Color),Metallic=s.Metal,Roughness=s.Rough,
                CullMode=BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter=BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic};
            if(s.Name is "Emerald" or "Sapphire" or "Ruby" or "Enamel")
            {
                mat.ClearcoatEnabled=true;mat.Clearcoat=.75f;mat.ClearcoatRoughness=.12f;
            }
            if(s.Name is "Platinum" or "Gold")
            {
                mat.AlbedoTexture=GD.Load<Texture2D>(CardFrameMaster.RootPath+"platinum-albedo-source.png");
                mat.NormalEnabled=true;mat.NormalTexture=GD.Load<Texture2D>(CardFrameMaster.RootPath+"relief-normal.png");
                mat.NormalScale=.25f;
                mat.AOEnabled=true;mat.AOTexture=GD.Load<Texture2D>(CardFrameMaster.RootPath+"relief-ao.png");
                mat.AOLightAffect=.25f;
                mat.RoughnessTexture=GD.Load<Texture2D>(CardFrameMaster.RootPath+"relief-roughness.png");
                // The bake contains .27; multiply to the intended .24-.28 range.
                mat.Roughness=s.Rough/.27f;
            }
            result[s.Name]=mat;
        }
        Palettes[faction]=result;return result;
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach(Node child in node.GetChildren())foreach(Node item in Descendants(child))yield return item;
    }
    private static IEnumerable<MeshInstance3D> Meshes(Node node)=>Descendants(node).OfType<MeshInstance3D>();
    private static ArrayMesh MakeContour()
    {
        Vector2[] points=[new(.030f,.062f),new(.075f,.022f),new(.46f,.022f),new(.5f,.007f),
            new(.54f,.022f),new(.925f,.022f),new(.974f,.062f),new(.978f,.90f),
            new(.949f,.981f),new(.55f,.982f),new(.5f,.993f),new(.45f,.982f),new(.05f,.981f),new(.022f,.90f)];
        var vertices=new List<Vector3>();float width=BattlefieldPerspective.CardWidth,depth=width/.75f;
        for(int i=0;i<points.Length;i++)
        {
            Vector2 a=points[i]-.5f*Vector2.One,b=points[(i+1)%points.Length]-.5f*Vector2.One;
            Vector3 pa=new(a.X*width,.012f,a.Y*depth),pb=new(b.X*width,.012f,b.Y*depth);
            Vector3 oa=new(a.X*width*1.013f,.012f,a.Y*depth*1.009f),ob=new(b.X*width*1.013f,.012f,b.Y*depth*1.009f);
            vertices.AddRange([pa,pb,oa,pb,ob,oa]);
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=vertices.ToArray();
        var result=new ArrayMesh();result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);return result;
    }
}
