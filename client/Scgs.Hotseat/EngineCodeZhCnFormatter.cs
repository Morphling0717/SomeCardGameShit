// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;

namespace Scgs.Hotseat;

public static class EngineCodeZhCnFormatter
{
    public static string Format(EngineStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return Format(status.RawCode);
    }

    public static string Format(EngineCode code) => Format((uint)code);

    public static string Format(uint rawCode) => rawCode switch
    {
        (uint)EngineCode.Ok => "操作成功。",
        (uint)EngineCode.InvalidPhase => "当前阶段不能执行此操作。",
        (uint)EngineCode.NotActivePlayer => "现在不是该玩家的行动时机。",
        (uint)EngineCode.InvalidPlayer => "玩家席位无效。",
        (uint)EngineCode.InvalidCard => "卡牌无效。",
        (uint)EngineCode.InvalidZone => "卡牌所在区域无效。",
        (uint)EngineCode.InvalidTarget => "目标无效。",
        (uint)EngineCode.InvalidSlot => "位置无效。",
        (uint)EngineCode.InsufficientPp => "PP 不足。",
        (uint)EngineCode.HandLimit => "手牌已达上限。",
        (uint)EngineCode.UnitZoneFull => "单位区已满。",
        (uint)EngineCode.TacticZoneFull => "策略区已满。",
        (uint)EngineCode.SummoningSickness => "该单位本回合不能攻击。",
        (uint)EngineCode.AlreadyAttacked => "该单位本回合已经攻击。",
        (uint)EngineCode.GuardBlocksTarget => "必须先攻击具有守护的单位。",
        (uint)EngineCode.EvolutionLocked => "尚未解锁进化。",
        (uint)EngineCode.NoEvolutionPoints => "进化能量不足。",
        (uint)EngineCode.EvolutionAlreadyUsed => "本回合已经进化。",
        (uint)EngineCode.AlreadyEvolved => "该单位已经进化。",
        (uint)EngineCode.AdvanceAlreadyUsed => "本回合已经动用未来。",
        (uint)EngineCode.AdvanceWouldExceedCap => "动用未来会超过 PP 容量上限。",
        (uint)EngineCode.DeployAlreadyUsed => "本回合已经部署。",
        (uint)EngineCode.DeployConditionNotMet => "未满足部署条件。",
        (uint)EngineCode.InvalidDeployment => "部署选择无效。",
        (uint)EngineCode.ResponseDepthExceeded => "响应层数已达上限。",
        (uint)EngineCode.TrapAlreadySetThisTurn => "本回合已经设置伏策。",
        (uint)EngineCode.NoPendingReaction => "当前没有待处理的响应。",
        (uint)EngineCode.TrapNotEligible => "该伏策不能在当前窗口发动。",
        (uint)EngineCode.LeaderSkillLocked => "主战技尚未解锁。",
        (uint)EngineCode.LeaderSkillAlreadyUsed => "本局已经使用主战技。",
        (uint)EngineCode.MatchAlreadyStarted => "比赛已经开始。",
        (uint)EngineCode.MatchNotStarted => "比赛尚未开始。",
        (uint)EngineCode.MulliganAlreadyDone => "该玩家已经完成调度。",
        (uint)EngineCode.DuplicateSelection => "选择中含有重复卡牌。",
        (uint)EngineCode.GameOver => "比赛已经结束。",
        (uint)EngineCode.StaleRevision => "对局状态已更新，请重新选择。",
        _ => $"未知规则错误（代码 {rawCode}）。",
    };
}
