// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Preview;

/// <summary>
/// Gate 6A's immutable resource contract. Missing resources deliberately fall
/// back to code-native artwork so the no-native style slice remains reviewable
/// while the approved raster candidates are generated in parallel.
/// </summary>
internal static class AnimeVisualAssetCatalog
{
    internal const string Root = "res://assets/visual/anime_v1/slice";
    internal const string AureliaLeader = Root + "/leaders/aurelia-master.png";
    internal const string SereiaLeader = Root + "/leaders/theraea-master.png";
    internal const string CardBack = Root + "/shared/card-back.png";
    internal const string MenuKeyArt = Root + "/menu/menu-key-art.png";
    internal const string OpenArena = Root + "/arena/open-fantasy-arena.png";

    internal static IReadOnlyDictionary<string, string> CardArt { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LO-03"] = Root + "/cards/LO-03.png",
            ["LO-07"] = Root + "/cards/LO-07.png",
            ["LO-11"] = Root + "/cards/LO-11.png",
            ["LO-11-EVOLVED"] = Root + "/cards/LO-11-evolved.png",
            ["AP-03"] = Root + "/cards/AP-03.png",
            ["AP-05"] = Root + "/cards/AP-05.png",
            ["AP-11"] = Root + "/cards/AP-11.png",
            ["AP-11-EVOLVED"] = Root + "/cards/AP-11-evolved.png",
            ["NT-04"] = Root + "/cards/NT-04.png",
        };

    internal static IReadOnlyList<string> RequiredPaths { get; } =
        new[] { AureliaLeader, SereiaLeader, CardBack, MenuKeyArt, OpenArena }
            .Concat(CardArt.Values)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    internal static Texture2D? TryLoad(string resourcePath)
    {
        if (!ResourceLoader.Exists(resourcePath, "Texture2D"))
        {
            return null;
        }
        return GD.Load<Texture2D>(resourcePath);
    }

    internal static Texture2D? Card(string designId) =>
        CardArt.TryGetValue(designId, out string? path) ? TryLoad(path) : null;

    internal static IReadOnlyList<string> LoadedPaths() => RequiredPaths
        .Where(path => ResourceLoader.Exists(path, "Texture2D"))
        .ToArray();
}
