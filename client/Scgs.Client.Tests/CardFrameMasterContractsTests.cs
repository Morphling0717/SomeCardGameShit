// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Scgs.GodotClient.CardFaces;
using Scgs.GodotClient.PresentationV2;

namespace Scgs.Client.Tests;

/// <summary>CPU layout/source geometry contracts, not a claim about GPU glyph legibility.</summary>
[TestClass]
[DoNotParallelize]
public sealed class CardFrameMasterContractsTests
{
    [TestInitialize]
    public void EnableCandidate() => CardFrameReviewRuntime.Configure(true);

    [TestCleanup]
    public void RestoreOrdinaryProduct() => CardFrameReviewRuntime.Configure(false);

    [TestMethod]
    public void HandFieldAndDetailUseOnePhysicalComposition()
    {
        foreach (string designId in new[] { "LO-11", "AP-11", "NT-04" })
        {
            CardFaceComposition baseline = Compose(designId, CardFaceContext.Hand);
            foreach (CardFaceContext context in Enum.GetValues<CardFaceContext>())
            {
                CardFaceComposition face = Compose(designId, context);
                Assert.AreEqual(baseline.Layout, face.Layout with { Context = CardFaceContext.Hand });
                Assert.AreEqual(baseline.ArtCrop, face.ArtCrop);
                Assert.AreEqual(baseline.ArtPath, face.ArtPath);
            }
        }
    }

    [TestMethod]
    public void NameIsCompleteAndSeparatedFromArtworkAndDecorations()
    {
        const string fullName = "曜誓大团长·蕾奥妮完整长名称不能使用省略号";
        CardFaceComposition face = Compose("LO-11", CardFaceContext.Detail, name: fullName);
        CardFaceLayout layout = face.Layout;
        Assert.AreEqual(fullName, face.ViewModel.DisplayName);
        Assert.IsTrue(layout.NamePlate.Bottom <= layout.ArtWindow.Y,
            "The complete nameplate belongs outside the illustration window.");
        Assert.IsTrue(layout.NameText.X - layout.NamePlate.X >= CardFaceLayout.MinimumNameDecorationInset);
        Assert.IsTrue(layout.NamePlate.Right - layout.NameText.Right >= CardFaceLayout.MinimumNameDecorationInset);
        AssertInside(layout.NameText, layout.NamePlate, "name well");
    }

    [TestMethod]
    public void OrdinaryProductAndNonRepresentativeCardsRemainUnchanged()
    {
        CardFaceComposition original = BaseFace("LO-11", CardFaceContext.Hand);
        CardFrameReviewRuntime.Configure(false);
        Assert.AreSame(original, CardFrameMaster.Compose(original));
        CardFrameReviewRuntime.Configure(true);
        CardFaceComposition other = BaseFace("LO-01", CardFaceContext.Hand);
        Assert.AreSame(other, CardFrameMaster.Compose(other));
    }

    [TestMethod]
    public void SpellHasNoAttackHealthOrCountdownSockets()
    {
        CardFaceLayout layout = Compose("NT-04", CardFaceContext.Field).Layout;
        Assert.IsNull(layout.AttackGem);
        Assert.IsNull(layout.AttackText);
        Assert.IsNull(layout.HealthGem);
        Assert.IsNull(layout.HealthText);
        Assert.IsNull(layout.CountdownGem);
        Assert.IsNull(layout.CountdownText);
    }

    [TestMethod]
    public void ZeroAndMultiDigitNumbersArePreservedWithoutChangingPhysicalSlots()
    {
        CardFaceComposition zero = Compose("LO-11", CardFaceContext.Hand, cost: 0, attack: 0, health: 0);
        CardFaceComposition large = Compose("LO-11", CardFaceContext.Hand, cost: 12, attack: 123, health: 10);
        Assert.AreEqual(zero.Layout, large.Layout);
        Assert.AreEqual(0, zero.ViewModel.Cost);
        Assert.AreEqual(0, zero.ViewModel.Attack);
        Assert.AreEqual(0, zero.ViewModel.Health);
        Assert.AreEqual(12, large.ViewModel.Cost);
        Assert.AreEqual(123, large.ViewModel.Attack);
        Assert.AreEqual(10, large.ViewModel.Health);
    }

    [TestMethod]
    public void ArtworkCoverRetainsItsAspectRatioAndFillsTheWindow()
    {
        CardFaceComposition face = Compose("AP-11", CardFaceContext.Detail);
        CardArtCrop crop = face.ArtCrop;
        float ratio = face.ViewModel.ArtPixelWidth * crop.Width /
            (face.ViewModel.ArtPixelHeight * crop.Height);
        Assert.AreEqual(face.Layout.ArtWindowAspectRatio, ratio, 0.00001f);
        Assert.IsTrue(Math.Abs(crop.Width - 1) < 0.00001f || Math.Abs(crop.Height - 1) < 0.00001f);
        Assert.IsTrue(crop.U >= 0 && crop.V >= 0 && crop.U + crop.Width <= 1.00001f && crop.V + crop.Height <= 1.00001f);
    }

    [TestMethod]
    public void BothModelLodsPlaceNumericSocketsWithinActualGemTables()
    {
        CardFaceLayout layout = Compose("LO-11", CardFaceContext.Hand).Layout;
        foreach (string fileName in new[] { "frame-master.glb", "frame-master-low.glb" })
        {
            Dictionary<string, GemTable> tables = ReadGemTables(fileName);
            AssertInside(layout.CostText, tables["Emerald"].Bounds, fileName + ": cost");
            AssertInside(layout.AttackText!.Value, tables["Sapphire"].Bounds, fileName + ": attack");
            AssertInside(layout.HealthText!.Value, tables["Ruby"].Bounds, fileName + ": health");
        }
    }

    [TestMethod]
    public void GlyphDepthClearsBothActualGemLodsAndDeclaredHighestSurface()
    {
        AssertClearance(CardFrameMaster.GlyphSurface, CardFrameMaster.HighestSurface);
        foreach (string fileName in new[] { "frame-master.glb", "frame-master-low.glb" })
        foreach (GemTable table in ReadGemTables(fileName).Values)
            Assert.IsTrue(CardFrameMaster.GlyphSurface - table.Height >= 0.012f,
                "This is model depth clearance only; final GPU text visibility requires separate acceptance.");
    }

    private static CardFaceComposition Compose(string designId, CardFaceContext context,
        string? name = null, int cost = 10, int attack = 8, int health = 8) =>
        CardFrameMaster.Compose(BaseFace(designId, context, name, cost, attack, health));

    private static CardFaceComposition BaseFace(string designId, CardFaceContext context,
        string? name = null, int cost = 10, int attack = 8, int health = 8)
    {
        ProductCardVisualEntry entry = ProductCardVisualCatalog.Shared.Resolve(designId);
        return CardFaceComposer.Compose(new CardFaceViewModel {
            DesignId = designId, DisplayName = name ?? designId, Kind = entry.Kind,
            Faction = entry.Faction, Rarity = entry.Rarity, Cost = cost,
            Attack = entry.Kind == ProductCardKind.Follower ? attack : null,
            Health = entry.Kind == ProductCardKind.Follower ? health : null,
        }, context, ProductCardVisualCatalog.Shared, CardFrameStyleCatalog.Shared);
    }

    private static void AssertInside(CardFaceRect inner, CardFaceRect outer, string label)
    {
        const float epsilon = 0.00001f;
        Assert.IsTrue(inner.X >= outer.X - epsilon && inner.Y >= outer.Y - epsilon &&
            inner.Right <= outer.Right + epsilon && inner.Bottom <= outer.Bottom + epsilon,
            $"{label}: {inner} exceeds the real table bounds {outer}.");
    }

    private static void AssertClearance(float glyphSurface, float physicalSurface) =>
        Assert.IsTrue(glyphSurface - physicalSurface >= 0.012f);

    private sealed record GemTable(CardFaceRect Bounds, float Height);

    // Deliberately decode actual GLB FLOAT positions, not accessor min/max or a second
    // hand-authored geometry table. The standalone Python audit validates the full GLB.
    private static Dictionary<string, GemTable> ReadGemTables(string fileName)
    {
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "global.json"))) repo = repo.Parent;
        Assert.IsNotNull(repo);
        byte[] bytes = File.ReadAllBytes(Path.Combine(repo.FullName,
            "client/godot/assets/visual/anime_v1/card_frame_r1", fileName));
        Assert.AreEqual(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        int jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
        using JsonDocument json = JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
        JsonElement doc = json.RootElement;
        int binaryStart = 20 + jsonLength + 8;
        Dictionary<string, List<Vector3>> positions = new(StringComparer.Ordinal) {
            ["Emerald"] = [], ["Sapphire"] = [], ["Ruby"] = [],
        };

        void Visit(int index, Matrix4x4 parent)
        {
            JsonElement node = doc.GetProperty("nodes")[index];
            Matrix4x4 local;
            if (node.TryGetProperty("matrix", out JsonElement matrix))
            {
                float[] m = matrix.EnumerateArray().Select(value => value.GetSingle()).ToArray();
                local = new(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],
                    m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
            }
            else
            {
                Vector3 scale = Vector(node, "scale", Vector3.One);
                Vector3 translation = Vector(node, "translation", Vector3.Zero);
                Quaternion rotation = Quaternion.Identity;
                if (node.TryGetProperty("rotation", out JsonElement r))
                    rotation = new(r[0].GetSingle(),r[1].GetSingle(),r[2].GetSingle(),r[3].GetSingle());
                local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) *
                    Matrix4x4.CreateTranslation(translation);
            }
            Matrix4x4 transform = local * parent;
            if (node.TryGetProperty("mesh", out JsonElement meshId))
            foreach (JsonElement primitive in doc.GetProperty("meshes")[meshId.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                string material = doc.GetProperty("materials")[primitive.GetProperty("material").GetInt32()].GetProperty("name").GetString()!;
                if (!positions.TryGetValue(material, out List<Vector3>? target)) continue;
                JsonElement accessor = doc.GetProperty("accessors")[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                Assert.AreEqual(5126, accessor.GetProperty("componentType").GetInt32());
                Assert.AreEqual("VEC3", accessor.GetProperty("type").GetString());
                JsonElement view = doc.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
                int start = binaryStart + OptionalInt(view, "byteOffset") + OptionalInt(accessor, "byteOffset");
                int stride = OptionalInt(view, "byteStride", 12);
                for (int i = 0; i < accessor.GetProperty("count").GetInt32(); ++i)
                {
                    int offset = start + i * stride;
                    Vector3 point = new(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset+4),
                        BitConverter.ToSingle(bytes, offset+8));
                    target.Add(Vector3.Transform(point, transform));
                }
            }
            if (node.TryGetProperty("children", out JsonElement children))
                foreach (JsonElement child in children.EnumerateArray()) Visit(child.GetInt32(), transform);
        }

        int scene = OptionalInt(doc, "scene");
        foreach (JsonElement root in doc.GetProperty("scenes")[scene].GetProperty("nodes").EnumerateArray())
            Visit(root.GetInt32(), Matrix4x4.Identity);
        Dictionary<string, GemTable> result = new(StringComparer.Ordinal);
        const float width = 1.58f, depth = width / CardFaceLayout.CardAspectRatio;
        foreach ((string material, List<Vector3> points) in positions)
        {
            Assert.IsTrue(points.Count > 0, material);
            float top = points.Max(point => point.Y);
            Vector3[] table = points.Where(point => Math.Abs(point.Y - top) <= 0.00001f).ToArray();
            Assert.IsTrue(table.Length >= 3, material + " must have a real planar table");
            float x = table.Min(point => point.X)/width + .5f;
            float y = table.Min(point => point.Z)/depth + .5f;
            float right = table.Max(point => point.X)/width + .5f;
            float bottom = table.Max(point => point.Z)/depth + .5f;
            result[material] = new(new(x,y,right-x,bottom-y), top);
        }
        return result;
    }

    private static int OptionalInt(JsonElement value, string name, int fallback = 0) =>
        value.TryGetProperty(name, out JsonElement result) ? result.GetInt32() : fallback;

    private static Vector3 Vector(JsonElement value, string name, Vector3 fallback) =>
        value.TryGetProperty(name, out JsonElement result) ?
            new(result[0].GetSingle(),result[1].GetSingle(),result[2].GetSingle()) : fallback;
}
