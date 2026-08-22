// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Scgs.Client;

namespace Scgs.GodotClient.Presentation;

public static class CardPresentation
{
    private const Keyword KnownKeywords =
        Keyword.Guard | Keyword.Rush | Keyword.Storm | Keyword.Barrier |
        Keyword.Bane | Keyword.Lifesteal | Keyword.Ambush;

    public static bool IsIdentityHidden(CardView card) =>
        card.Definition is null || card.DefinitionId is null || card.InstanceId is null;

    public static string FormatCompact(CardView card)
    {
        if (IsIdentityHidden(card))
        {
            return "隐藏牌：当前观看者没有收到卡牌身份。";
        }

        string stats = card.Kind switch
        {
            CardKind.Unit => $" · {card.CurrentAttack}/{card.CurrentHealth}",
            CardKind.Relic => $" · 倒计时 {card.Countdown}",
            _ => string.Empty,
        };
        string keywords = FormatKeywords(card.Keywords);
        return $"{card.Name} · {FormatKind(card.Kind)} · 费用 {card.Cost}{stats}" +
               (keywords.Length == 0 ? string.Empty : $" · {keywords}");
    }

    public static string FormatRules(CardView card)
    {
        if (IsIdentityHidden(card))
        {
            return "这是一张对当前观看者隐藏身份的牌。不会显示名称、编号或规则。";
        }

        var result = new StringBuilder();
        result.Append(card.Name).Append('\n');
        result.Append(FormatKind(card.Kind)).Append(" · 费用 ").Append(card.Cost);
        if (card.Kind == CardKind.Unit)
        {
            result.Append(" · 当前 ").Append(card.CurrentAttack).Append('/').Append(card.CurrentHealth);
            if (card.Evolved)
            {
                result.Append("（已进化）");
            }
        }
        else if (card.Kind == CardKind.Relic)
        {
            result.Append(" · 倒计时 ").Append(card.Countdown);
        }

        string activeKeywords = FormatKeywords(card.Keywords);
        if (activeKeywords.Length != 0)
        {
            result.Append('\n').Append("关键词：").Append(activeKeywords);
        }

        CardDefinition? definition = card.Definition;
        if (definition is null)
        {
            return result.ToString();
        }

        if (definition.Kind == CardKind.Unit)
        {
            result.Append('\n').Append("基础身材：")
                .Append(definition.Attack).Append('/').Append(definition.Health);
            if (definition.EvolvedAttack != 0 || definition.EvolvedHealth != 0)
            {
                result.Append(" · 进化后 ")
                    .Append(definition.EvolvedAttack).Append('/').Append(definition.EvolvedHealth);
            }
        }

        string printedKeywords = FormatPrintedKeywords(definition);
        if (printedKeywords.Length != 0)
        {
            result.Append('\n').Append("印刷关键词：").Append(printedKeywords);
        }

        if (definition.AdditionalCost.BurnPpCapacity > 0)
        {
            result.Append('\n').Append("额外费用：燃耗 ")
                .Append(definition.AdditionalCost.BurnPpCapacity).Append(" 点 PP 容量");
        }

        if (definition.Deployment is { } deployment)
        {
            result.Append('\n').Append("部署：")
                .Append(FormatDeploymentCondition(deployment.Condition, deployment.ConditionAmount))
                .Append("；支付 ").Append(deployment.PpCost).Append(" PP");
            if (deployment.ArchiveOneFriendlyUnit)
            {
                result.Append("；可选择一个己方单位作为组件并封存");
            }
        }

        if (definition.Component.HasComponent)
        {
            result.Append('\n').Append("组件：")
                .Append(FormatEffectKind(definition.Component.GrantedKind, definition.Component.GrantedAmount));
        }

        foreach (EffectRecord effect in definition.Effects)
        {
            result.Append('\n').Append('【').Append(FormatTrigger(effect.Trigger)).Append("】")
                .Append(FormatEffectKind(effect.Kind, effect.Amount));
            string target = FormatTargetSpec(effect.TargetSpec);
            if (target.Length != 0)
            {
                result.Append("（").Append(target).Append('）');
            }
        }

        return result.ToString();
    }

    public static string FormatKind(CardKind? kind) => kind switch
    {
        CardKind.Unit => "单位",
        CardKind.Spell => "法术",
        CardKind.Relic => "遗物",
        CardKind.Trap => "伏策",
        null => "隐藏种类",
        _ => $"未知种类（{(uint)kind.Value}）",
    };

    public static string FormatKeywords(Keyword keywords)
    {
        var names = new List<string>();
        AddKeyword(names, keywords, Keyword.Guard, "守护");
        AddKeyword(names, keywords, Keyword.Rush, "突进");
        AddKeyword(names, keywords, Keyword.Storm, "疾驰");
        AddKeyword(names, keywords, Keyword.Barrier, "屏障");
        AddKeyword(names, keywords, Keyword.Bane, "必杀");
        AddKeyword(names, keywords, Keyword.Lifesteal, "吸血");
        AddKeyword(names, keywords, Keyword.Ambush, "潜伏");
        uint unknown = (uint)(keywords & ~KnownKeywords);
        if (unknown != 0)
        {
            names.Add($"未知关键词 0x{unknown:X8}");
        }

        return string.Join("、", names);
    }

    public static string FormatTrigger(EffectTrigger trigger) => trigger switch
    {
        EffectTrigger.OnPlay => "打出时",
        EffectTrigger.OnPlayIfAdvanced => "预支打出时",
        EffectTrigger.OnPlayIfNotAdvanced => "未预支打出时",
        EffectTrigger.OnEntry => "登场时",
        EffectTrigger.OnEvolution => "进化时",
        EffectTrigger.OnLastWords => "遗言",
        EffectTrigger.OnCountdownExpire => "倒计时结束时",
        EffectTrigger.OnSpellDeclared => "法术宣言时",
        EffectTrigger.OnAttackDeclared => "攻击宣言时",
        EffectTrigger.OnEntryEffectPending => "登场效果待结算时",
        _ => $"未知触发（{(uint)trigger}）",
    };

    public static string FormatEffectKind(EffectKind kind, int amount) => kind switch
    {
        EffectKind.DrawCards => $"抽 {amount} 张牌",
        EffectKind.DealDamageToEnemyUnit => $"对敌方单位造成 {amount} 点伤害",
        EffectKind.DealDamageToLeader => $"对主战者造成 {amount} 点伤害",
        EffectKind.HealLeader => $"回复主战者 {amount} 点生命",
        EffectKind.RepairCracks => $"修复 {amount} 点裂痕",
        EffectKind.GainPpCapacity => $"获得 {amount} 点 PP 容量",
        EffectKind.BuffFriendlyUnit => $"使一个己方单位获得 +{amount}/+{amount}",
        EffectKind.GrantRush => "获得突进",
        EffectKind.CancelAttack => "取消该次攻击",
        EffectKind.DamageEnteredUnit => $"对登场单位造成 {amount} 点伤害",
        _ => $"未知效果（{(uint)kind}，数值 {amount}）",
    };

    private static string FormatPrintedKeywords(CardDefinition definition)
    {
        var names = new List<string>();
        if (definition.PrintedGuard) names.Add("守护");
        if (definition.PrintedRush) names.Add("突进");
        if (definition.PrintedStorm) names.Add("疾驰");
        if (definition.PrintedBarrier) names.Add("屏障");
        if (definition.PrintedLifesteal) names.Add("吸血");
        if (definition.PrintedBane) names.Add("必杀");
        return string.Join("、", names);
    }

    private static string FormatDeploymentCondition(DeploymentCondition condition, int amount) => condition switch
    {
        DeploymentCondition.None => "无额外条件",
        DeploymentCondition.FriendlyUnitsMin => $"己方至少有 {amount} 个单位",
        DeploymentCondition.SpellsThisTurnMin => $"本回合至少使用过 {amount} 张法术",
        _ => $"未知条件（{(uint)condition}，数值 {amount}）",
    };

    private static string FormatTargetSpec(TargetSpec target) => target switch
    {
        TargetSpec.None => string.Empty,
        TargetSpec.EnemyUnit => "选择一个敌方单位",
        TargetSpec.FriendlyUnit => "选择一个己方单位",
        _ => $"未知目标（{(uint)target}）",
    };

    private static void AddKeyword(List<string> output, Keyword value, Keyword bit, string name)
    {
        if ((value & bit) != 0)
        {
            output.Add(name);
        }
    }
}
