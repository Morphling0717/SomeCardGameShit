// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Presentation;

internal static class ProductCardPresentation
{
    private const V05.Keyword KnownKeywords =
        V05.Keyword.Guard | V05.Keyword.Rush | V05.Keyword.Storm |
        V05.Keyword.Barrier | V05.Keyword.Bane | V05.Keyword.Lifesteal |
        V05.Keyword.Ambush;

    internal static bool HasKnownIdentity(V05.CardView card) =>
        card.InstanceId.HasValue && !string.IsNullOrWhiteSpace(card.DesignId);

    internal static string FormatRules(V05.CardView card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!HasKnownIdentity(card))
        {
            return "这是一张对当前观看者隐藏身份的牌。";
        }

        var output = new StringBuilder();
        output.Append(card.Name).Append('\n')
            .Append(FormatKind(card.Kind)).Append(" · 费用 ").Append(card.Cost);
        if (card.Kind == V05.CardKind.Follower)
        {
            output.Append(" · ").Append(card.CurrentAttack).Append('/')
                .Append(card.CurrentHealth);
            if (card.Evolved)
            {
                output.Append("（已进化）");
            }
        }
        else if (card.Kind is V05.CardKind.Amulet or V05.CardKind.Trap)
        {
            output.Append(" · 倒数 ").Append(card.Countdown);
        }

        string keywords = FormatKeywords(card.Keywords);
        if (keywords.Length != 0)
        {
            output.Append('\n').Append("关键词：").Append(keywords);
        }

        ProductCardTextEntry? text = ProductCardTextCatalog.Find(card.DesignId);
        if (text is not null)
        {
            output.Append("\n\n").Append(text.CanonicalRulesText);
        }
        else
        {
            output.Append("\n\n规则文本尚未收录；对局数值仍以原生引擎为准。");
        }
        return output.ToString();
    }

    internal static string FormatKind(V05.CardKind? kind) => kind switch
    {
        V05.CardKind.Follower => "随从",
        V05.CardKind.Spell => "法术",
        V05.CardKind.Amulet => "护符",
        V05.CardKind.Trap => "伏策",
        V05.CardKind.Field => "场地",
        null => "隐藏种类",
        _ => $"未知种类（{(uint)kind.Value}）",
    };

    internal static string FormatKeywords(V05.Keyword keywords)
    {
        var names = new List<string>();
        Add(names, keywords, V05.Keyword.Guard, "守护");
        Add(names, keywords, V05.Keyword.Rush, "突进");
        Add(names, keywords, V05.Keyword.Storm, "疾驰");
        Add(names, keywords, V05.Keyword.Barrier, "屏障");
        Add(names, keywords, V05.Keyword.Bane, "必杀");
        Add(names, keywords, V05.Keyword.Lifesteal, "吸血");
        Add(names, keywords, V05.Keyword.Ambush, "潜伏");
        uint unknown = (uint)(keywords & ~KnownKeywords);
        if (unknown != 0)
        {
            names.Add($"未知关键词 0x{unknown:X8}");
        }
        return string.Join("、", names);
    }

    private static void Add(
        ICollection<string> output,
        V05.Keyword value,
        V05.Keyword bit,
        string label)
    {
        if ((value & bit) != 0)
        {
            output.Add(label);
        }
    }
}
