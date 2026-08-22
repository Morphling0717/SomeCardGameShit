// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Scgs.Client;

namespace Scgs.GodotClient.Presentation;

public static class ActionPresentation
{
    public static string FormatAction(ActionKind action) => action switch
    {
        ActionKind.Mulligan => "提交调度",
        ActionKind.PlayUnit => "打出单位",
        ActionKind.CastSpell => "施放法术",
        ActionKind.PlayTactic => "设置策略",
        ActionKind.Attack => "攻击",
        ActionKind.Evolve => "进化",
        ActionKind.Deploy => "从战备部署",
        ActionKind.ActivateTrap => "发动伏策",
        ActionKind.PassReaction => "不响应",
        ActionKind.EndTurn => "结束回合",
        ActionKind.Surrender => "投降",
        _ => $"未知行动（{(uint)action}）",
    };

    public static string FormatTarget(Target? target, PlayerId viewer) => target switch
    {
        null => "无需目标",
        { Kind: TargetKind.Leader } =>
            target.Player == viewer ? "己方主战者" : "对方主战者",
        { Kind: TargetKind.Unit } =>
            target.Player == viewer ? "己方单位" : "对方单位",
        _ => $"未知目标（{(uint)target.Kind}）",
    };

    public static string FormatPayment(PaymentPreview payment)
    {
        if (!payment.Status.IsSuccess)
        {
            return $"当前无法支付：{FormatEngineStatus(payment.Status)}";
        }

        var result = new StringBuilder();
        result.Append("PP ").Append(payment.CurrentPpBefore).Append(" → ").Append(payment.CurrentPpAfter);
        if (payment.PpCapacityBefore != payment.PpCapacityAfter)
        {
            result.Append(" · 容量 ").Append(payment.PpCapacityBefore).Append(" → ").Append(payment.PpCapacityAfter);
        }
        if (payment.CracksBefore != payment.CracksAfter)
        {
            result.Append(" · 裂痕 ").Append(payment.CracksBefore).Append(" → ").Append(payment.CracksAfter);
        }
        if (payment.EvolutionEnergyBefore != payment.EvolutionEnergyAfter)
        {
            result.Append(" · 进化能量 ").Append(payment.EvolutionEnergyBefore)
                .Append(" → ").Append(payment.EvolutionEnergyAfter);
        }
        if (payment.BurnCost > 0)
        {
            result.Append(" · 燃耗 ").Append(payment.BurnCost);
        }
        if (payment.UsedAdvance)
        {
            result.Append(" · 使用预支（").Append(payment.AdvanceCost).Append("）");
        }
        return result.ToString();
    }

    public static string FormatConfirmation(
        GameCommandRequest command,
        PaymentPreview payment,
        string? sourceName,
        string? targetDescription)
    {
        var result = new StringBuilder(FormatAction(command.Action));
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            result.Append("：").Append(sourceName);
        }
        if (!string.IsNullOrWhiteSpace(targetDescription))
        {
            result.Append('\n').Append("目标：").Append(targetDescription);
        }
        if (command.Slot.HasValue)
        {
            result.Append('\n').Append("位置：第 ").Append(command.Slot.Value + 1).Append(" 格");
        }
        if (command.ComponentDonor.HasValue)
        {
            result.Append('\n').Append("组件：使用所选己方单位");
        }
        if (command.Action == ActionKind.Mulligan)
        {
            result.Append('\n').Append("替换 ").Append(command.MulliganCards.Count).Append(" 张手牌");
        }
        result.Append('\n').Append(FormatPayment(payment));
        return result.ToString();
    }

    public static string FormatEngineStatus(EngineStatus status) => status.Code switch
    {
        EngineCode.Ok => "成功",
        EngineCode.InvalidPhase => "当前阶段不能执行该行动",
        EngineCode.NotActivePlayer => "现在不是该玩家的行动时机",
        EngineCode.InvalidPlayer => "玩家参数无效",
        EngineCode.InvalidCard => "卡牌已不存在或不可使用",
        EngineCode.InvalidZone => "卡牌所在区域不正确",
        EngineCode.InvalidTarget => "目标无效",
        EngineCode.InvalidSlot => "位置无效",
        EngineCode.InsufficientPp => "PP 不足",
        EngineCode.HandLimit => "手牌已满",
        EngineCode.UnitZoneFull => "单位区已满",
        EngineCode.TacticZoneFull => "策略区已满",
        EngineCode.SummoningSickness => "该单位本回合不能攻击",
        EngineCode.AlreadyAttacked => "该单位本回合已经攻击",
        EngineCode.GuardBlocksTarget => "必须优先攻击具有守护的单位",
        EngineCode.EvolutionLocked => "进化尚未解锁",
        EngineCode.NoEvolutionPoints => "进化能量不足",
        EngineCode.EvolutionAlreadyUsed => "本回合已经进化",
        EngineCode.AlreadyEvolved => "该单位已经进化",
        EngineCode.AdvanceAlreadyUsed => "本回合已经使用预支",
        EngineCode.AdvanceWouldExceedCap => "预支会超过容量上限",
        EngineCode.DeployAlreadyUsed => "本回合已经部署",
        EngineCode.DeployConditionNotMet => "部署条件未满足",
        EngineCode.InvalidDeployment => "部署选择无效",
        EngineCode.ResponseDepthExceeded => "响应层数已达上限",
        EngineCode.TrapAlreadySetThisTurn => "本回合已经设置伏策",
        EngineCode.NoPendingReaction => "当前没有等待中的响应",
        EngineCode.TrapNotEligible => "该伏策不能响应当前行动",
        EngineCode.LeaderSkillLocked => "主战技尚未解锁",
        EngineCode.LeaderSkillAlreadyUsed => "本回合已经使用主战技",
        EngineCode.MatchAlreadyStarted => "比赛已经开始",
        EngineCode.MatchNotStarted => "比赛尚未开始",
        EngineCode.MulliganAlreadyDone => "该玩家已经完成调度",
        EngineCode.DuplicateSelection => "选择中包含重复卡牌",
        EngineCode.GameOver => "比赛已经结束",
        EngineCode.StaleRevision => "画面状态已经更新，请重新选择",
        _ => string.IsNullOrWhiteSpace(status.Message)
            ? $"未知引擎错误（{status.RawCode}）"
            : $"未知引擎错误（{status.RawCode}）：{status.Message}",
    };

    public static string FormatReactionOrigin(
        ReactionContext context,
        PlayerId viewer,
        string? sourceName)
    {
        if (context.Origin is not { } origin)
        {
            return "正在等待引擎提供原行动信息。";
        }

        string actor = origin.Player == viewer ? "你" : "对手";
        string source = string.IsNullOrWhiteSpace(sourceName) ? "公开来源" : sourceName;
        string target = origin.Target is null
            ? string.Empty
            : $"，目标为{FormatTarget(origin.Target, viewer)}";
        return $"原行动：{actor}{FormatAction(origin.Action)}「{source}」{target}。";
    }
}
