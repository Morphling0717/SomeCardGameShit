// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;

namespace Scgs.GodotClient.Presentation;

public static class GameEventPresentation
{
    public static string Format(GameEventView gameEvent, PlayerId viewer)
    {
        string actor = gameEvent.Player == viewer ? "你" : "对手";
        string text = gameEvent.Type switch
        {
            EventType.MatchStarted => $"比赛开始，{PlayerLabel(gameEvent.FirstPlayer)}先手。",
            EventType.TurnStarted => $"{actor}的回合开始。",
            EventType.TurnEnded => $"{actor}结束了回合。",
            EventType.CardDrawn => gameEvent.HiddenCard ? "对手抽了一张牌。" : $"{actor}抽了一张牌。",
            EventType.FatigueDamage => $"{actor}受到 {gameEvent.Value} 点疲劳伤害。",
            EventType.HandOverflowArchived => $"{actor}因手牌已满封存了一张牌。",
            EventType.PpChanged => $"{actor}的 PP 变为 {gameEvent.Value}/{gameEvent.SecondaryValue}。",
            EventType.CracksChanged => $"{actor}的裂痕变为 {gameEvent.Value}。",
            EventType.CardMoved => gameEvent.HiddenCard ? "对手设置了一张背面伏策。" : $"{actor}移动了一张牌。",
            EventType.UnitEntered => $"{actor}的单位进入了第 {gameEvent.Value + 1} 格。",
            EventType.UnitDamaged => $"{actor}的单位受到 {gameEvent.Value} 点伤害。",
            EventType.LeaderDamaged => $"{actor}的主战者受到 {gameEvent.Value} 点伤害。",
            EventType.LeaderHealed => $"{actor}的主战者回复 {gameEvent.Value} 点生命。",
            EventType.UnitDestroyed => $"{actor}的单位被破坏。",
            EventType.AttackDeclared => $"{actor}宣言攻击。",
            EventType.AttackCancelled => $"{actor}的攻击被取消。",
            EventType.UnitEvolved => $"{actor}进化了一个单位。",
            EventType.EvolutionEnergyChanged => $"{actor}的进化能量变为 {gameEvent.Value}。",
            EventType.UnitDeployed => $"{actor}从战备区部署了一个单位。",
            EventType.TrapWindowOpened => $"等待{actor}响应（可用伏策 {gameEvent.SecondaryValue} 张）。",
            EventType.TrapActivated => $"{actor}发动了一张伏策。",
            EventType.LeaderSkillUsed => $"{actor}使用了主战技。",
            EventType.PlayerSurrendered => $"{actor}投降。",
            EventType.MatchEnded => "比赛结束。",
            EventType.MulliganCompleted => gameEvent.HiddenCard
                ? "对手完成了调度。"
                : $"{actor}完成调度，替换 {gameEvent.Value} 张牌。",
            _ => $"未知事件（{(uint)gameEvent.Type}）。",
        };

        return $"#{gameEvent.Sequence}  {text}";
    }

    private static string PlayerLabel(PlayerId? player) => player switch
    {
        PlayerId.Player0 => "玩家 0 ",
        PlayerId.Player1 => "玩家 1 ",
        _ => string.Empty,
    };
}
