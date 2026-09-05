// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.CardFaces;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>One coherent face surface and a real rounded, bevelled edge. No per-card viewport.</summary>
internal sealed partial class SculptedCardBody : Node3D
{
    internal const string MaterialPath = "res://assets/visual/anime_v1/presentation_v2/engraved-platinum.png";
    private static readonly Shader FrontShader = GD.Load<Shader>("res://shaders/sculpted_card_front.gdshader");
    private static readonly ArrayMesh EdgeMesh = BuildBevel();
    private static readonly ShaderMaterial ContactShadow = new() { Shader = new Shader { Code = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never;
        void fragment() {
            vec2 q = abs(UV - vec2(0.5)) - vec2(0.427, 0.435);
            float d = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - 0.034;
            ALBEDO = vec3(0.015, 0.013, 0.023);
            ALPHA = (1.0 - smoothstep(-0.018, 0.027, d)) * 0.22;
        }
        """ } };
    private static readonly QuadMesh FaceMesh = new() { Size = new(BattlefieldPerspective.CardWidth, BattlefieldPerspective.CardWidth / CardFaceLayout.CardAspectRatio) };
    private readonly ShaderMaterial frontMaterial = new() { Shader = FrontShader };
    private readonly StandardMaterial3D edgeMaterial = new() { Metallic = 0.58f, Roughness = 0.34f };
    private MeshInstance3D front = null!;
    private MeshInstance3D edge = null!;
    private MeshInstance3D shadow = null!;
    private Texture2D? sharedCardBack;
    internal bool HasIdentity { get; private set; }

    // Structural evidence reads the actual visible mesh and bound shader
    // resources. It is not a substitute for the final GPU number/crest ROIs.
    internal bool CiFrontSurfaceBound => HasIdentity && IsVisibleInTree() &&
        front is not null && front.IsVisibleInTree() && front.Mesh is not null &&
        front.MaterialOverride == frontMaterial && frontMaterial.Shader == FrontShader &&
        frontMaterial.GetShaderParameter("artwork").AsGodotObject() is Texture2D &&
        frontMaterial.GetShaderParameter("engraving").AsGodotObject() is Texture2D;
    internal float CiFrontSurfaceLocalY => front is null ? 0 : (Transform * front.Transform).Origin.Y;
    internal bool CiCostSocketBound => CiFrontSurfaceBound;
    internal bool CiFollowerSocketsBound => CiFrontSurfaceBound && frontMaterial.GetShaderParameter("follower").AsBool();
    internal bool CiTypeCrestBound => CiFrontSurfaceBound;

    internal bool CiUsesSharedCardBack(Texture2D expected) =>
        !HasIdentity && IsVisibleInTree() && ReferenceEquals(sharedCardBack, expected) &&
        front is not null && front.IsVisibleInTree() && front.MaterialOverride == frontMaterial &&
        frontMaterial.Shader == FrontShader &&
        ReferenceEquals(frontMaterial.GetShaderParameter("artwork").AsGodotObject(), expected) &&
        frontMaterial.GetShaderParameter("engraving").AsGodotObject() is null &&
        frontMaterial.GetShaderParameter("anonymous_back").AsBool() &&
        !frontMaterial.GetShaderParameter("follower").AsBool() &&
        !frontMaterial.GetShaderParameter("evolved").AsBool();

    public override void _Ready()
    {
        shadow = new MeshInstance3D { Name = "SoftContactShadow", Mesh = FaceMesh,
            MaterialOverride = ContactShadow, RotationDegrees = new(-90, 0, 0),
            Position = new(0.026f, -0.011f, 0.040f), Scale = new(1.10f, 1.07f, 1),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(shadow);
        edge = new MeshInstance3D { Name = "BevelledEdge", Mesh = EdgeMesh, MaterialOverride = edgeMaterial };
        front = new MeshInstance3D
        {
            Name = "UnifiedEnamelFace", Mesh = FaceMesh, MaterialOverride = frontMaterial,
            RotationDegrees = new(-90, 0, 0), Position = new(0, 0.076f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(edge); AddChild(front); Visible = false;
    }

    internal void Bind(CardFaceComposition face)
    {
        if (!IsNodeReady()) throw new InvalidOperationException("Attach the card body before binding it.");
        sharedCardBack = null;
        frontMaterial.SetShaderParameter("anonymous_back", false);
        shadow.Visible = true;
        var faction = face.ViewModel.Faction;
        Color metal = faction switch
        {
            ProductCardFaction.Oathguard => new("d4c09a"),
            ProductCardFaction.Pactmage => new("b6afc9"),
            _ => new("bfc6c8"),
        };
        Color ink = faction == ProductCardFaction.Pactmage ? new("23142d") : new("142336");
        Color accent = faction == ProductCardFaction.Pactmage ? new("c191e6") : new("a9d5ff");
        edgeMaterial.AlbedoColor = metal.Darkened(0.28f);
        frontMaterial.SetShaderParameter("artwork", GD.Load<Texture2D>(face.ArtPath));
        frontMaterial.SetShaderParameter("engraving", GD.Load<Texture2D>(MaterialPath));
        frontMaterial.SetShaderParameter("art_crop", new Vector4(face.ArtCrop.U, face.ArtCrop.V, face.ArtCrop.Width, face.ArtCrop.Height));
        frontMaterial.SetShaderParameter("name_rect", Rect(face.Layout.NamePlate));
        frontMaterial.SetShaderParameter("cost_rect", Rect(face.Layout.CostGem));
        frontMaterial.SetShaderParameter("attack_rect", Rect(face.Layout.AttackGem ?? new CardFaceRect(0,0,0.1f,0.1f)));
        frontMaterial.SetShaderParameter("health_rect", Rect(face.Layout.HealthGem ?? new CardFaceRect(0,0,0.1f,0.1f)));
        frontMaterial.SetShaderParameter("metal_color", metal);
        frontMaterial.SetShaderParameter("enamel_color", ink);
        frontMaterial.SetShaderParameter("accent_color", accent);
        frontMaterial.SetShaderParameter("follower", face.ViewModel.Kind == ProductCardKind.Follower);
        frontMaterial.SetShaderParameter("evolved", face.ViewModel.Variant == CardFrameVariant.Evolved);
        frontMaterial.SetShaderParameter("highlight", 0.0f);
        frontMaterial.SetShaderParameter("energy", 0.0f);
        HasIdentity = true; Visible = true;
    }

    // Deliberately accepts only the shared back texture: no card composition,
    // definition/instance identifier, faction, number or identity-derived tint.
    internal void BindSharedCardBack(Texture2D cardBack)
    {
        ArgumentNullException.ThrowIfNull(cardBack);
        if (!IsNodeReady()) throw new InvalidOperationException("Attach the card body before binding it.");
        ClearSensitive();
        sharedCardBack = cardBack;
        frontMaterial.SetShaderParameter("artwork", cardBack);
        frontMaterial.SetShaderParameter("anonymous_back", true);
        edgeMaterial.AlbedoColor = new Color("707383");
        // A floating anonymous hand has no offset contact-shadow skirt.
        shadow.Visible = false;
        HasIdentity = false;
        Visible = true;
    }

    internal void SetHighlight(float value) => frontMaterial.SetShaderParameter("highlight", value);
    private static Vector4 Rect(CardFaceRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    internal void SetEnergy(float value) => frontMaterial.SetShaderParameter("energy", value);
    internal void ClearSensitive()
    {
        Visible = false; HasIdentity = false;
        sharedCardBack = null;
        frontMaterial.SetShaderParameter("artwork", default(Variant));
        frontMaterial.SetShaderParameter("engraving", default(Variant));
        frontMaterial.SetShaderParameter("anonymous_back", false);
        frontMaterial.SetShaderParameter("art_crop", new Vector4(0, 0, 1, 1));
        frontMaterial.SetShaderParameter("name_rect", Vector4.Zero);
        frontMaterial.SetShaderParameter("cost_rect", Vector4.Zero);
        frontMaterial.SetShaderParameter("attack_rect", Vector4.Zero);
        frontMaterial.SetShaderParameter("health_rect", Vector4.Zero);
        frontMaterial.SetShaderParameter("metal_color", Colors.Gray);
        frontMaterial.SetShaderParameter("enamel_color", Colors.Black);
        frontMaterial.SetShaderParameter("accent_color", Colors.Transparent);
        frontMaterial.SetShaderParameter("highlight", 0.0f);
        frontMaterial.SetShaderParameter("energy", 0.0f);
        frontMaterial.SetShaderParameter("follower", false);
        frontMaterial.SetShaderParameter("evolved", false);
        edgeMaterial.AlbedoColor = Colors.Gray;
        if (shadow is not null) shadow.Visible = false;
        ClearMetadata(this);
    }

    private static void ClearMetadata(Node node)
    {
        foreach (StringName key in node.GetMetaList()) node.RemoveMeta(key);
        foreach (Node child in node.GetChildren()) ClearMetadata(child);
    }

    private static ArrayMesh BuildBevel()
    {
        const int segments = 12;
        float width = BattlefieldPerspective.CardWidth;
        float depth = width / CardFaceLayout.CardAspectRatio;
        var vertices = new List<Vector3>(); var normals = new List<Vector3>();
        Vector2 Point(int index, float inset)
        {
            int corner = index / segments;
            float angle = (corner * 90 + (index % segments) * 90.0f / segments) * Mathf.Pi / 180;
            float radius = 0.063f;
            float cx = (corner is 0 or 3 ? 1 : -1) * (width * 0.488f - radius - inset);
            float cz = (corner is 0 or 1 ? 1 : -1) * (depth * 0.490f - radius - inset);
            return new(cx + Mathf.Cos(angle) * radius, cz + Mathf.Sin(angle) * radius);
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = (b - a).Cross(c - a).Normalized();
            vertices.AddRange([a, b, c]); normals.AddRange([normal, normal, normal]);
        }
        (float Y, float Inset)[] rings = [(0.005f, 0.010f), (0.018f, 0), (0.058f, 0), (0.075f, 0.015f)];
        for (int ring = 0; ring < rings.Length - 1; ring++)
        for (int i = 0; i < segments * 4; i++)
        {
            Vector2 a = Point(i, rings[ring].Inset), b = Point((i + 1) % (segments * 4), rings[ring].Inset);
            Vector2 c = Point(i, rings[ring + 1].Inset), d = Point((i + 1) % (segments * 4), rings[ring + 1].Inset);
            Vector3 va = new(a.X, rings[ring].Y, a.Y), vb = new(b.X, rings[ring].Y, b.Y);
            Vector3 vc = new(c.X, rings[ring + 1].Y, c.Y), vd = new(d.X, rings[ring + 1].Y, d.Y);
            Triangle(va, vc, vb); Triangle(vb, vc, vd);
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); return mesh;
    }
}
