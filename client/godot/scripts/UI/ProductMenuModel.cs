// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.UI;

public enum ProductMenuFeature : byte
{
    LocalHotseat,
    SinglePlayer,
    OnlinePlay,
    DeckEditor,
    CardLibrary,
    ReplayViewer,
    Settings,
    Exit,
}

public enum ProductMenuFeatureStatus : byte
{
    Available,
    InDevelopment,
}

public sealed record ProductMenuEntry(
    ProductMenuFeature Feature,
    string Label,
    ProductMenuFeatureStatus Status,
    bool RequiresNativeSession);

public static class ProductMenuCatalog
{
    public static IReadOnlyList<ProductMenuEntry> Entries { get; } =
    [
        new(ProductMenuFeature.LocalHotseat, "本地热座", ProductMenuFeatureStatus.Available, true),
        new(ProductMenuFeature.SinglePlayer, "单人挑战", ProductMenuFeatureStatus.InDevelopment, false),
        new(ProductMenuFeature.OnlinePlay, "在线对战", ProductMenuFeatureStatus.InDevelopment, false),
        new(ProductMenuFeature.DeckEditor, "牌组编辑", ProductMenuFeatureStatus.InDevelopment, false),
        new(ProductMenuFeature.CardLibrary, "卡牌图鉴", ProductMenuFeatureStatus.InDevelopment, false),
        new(ProductMenuFeature.ReplayViewer, "录像回放", ProductMenuFeatureStatus.InDevelopment, false),
        new(ProductMenuFeature.Settings, "设置", ProductMenuFeatureStatus.Available, false),
        new(ProductMenuFeature.Exit, "退出", ProductMenuFeatureStatus.Available, false),
    ];

    public static ProductMenuEntry Get(ProductMenuFeature feature)
    {
        return Entries.Single(entry => entry.Feature == feature);
    }
}
