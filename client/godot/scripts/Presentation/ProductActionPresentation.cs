// SPDX-License-Identifier: GPL-3.0-or-later
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Presentation;

internal static class ProductActionPresentation
{
    internal static string Format(V05.ActionKind action) => action switch
    {
        V05.ActionKind.Mulligan => "确认调度",
        V05.ActionKind.PlayUnit => "召唤",
        V05.ActionKind.CastSpell => "施放",
        V05.ActionKind.PlayTrap => "设置伏策",
        V05.ActionKind.Attack => "攻击",
        V05.ActionKind.Evolve => "进化",
        V05.ActionKind.Deploy => "部署",
        V05.ActionKind.ActivateTrap => "发动",
        V05.ActionKind.PassReaction => "不过",
        V05.ActionKind.EndTurn => "结束回合",
        V05.ActionKind.Surrender => "投降",
        V05.ActionKind.PlayAmulet => "放置护符",
        V05.ActionKind.PlayField => "展开场地",
        V05.ActionKind.ResolveChoice => "完成选择",
        _ => $"未知行动（{(uint)action}）",
    };

    internal static string FormatMode(string modeId) => modeId switch
    {
        "repair" => "修复裂痕",
        "buff" => "强化随从",
        "damage_follower" => "对随从造成伤害",
        "destroy_permanent" => "破坏护符／场地",
        _ => modeId.Replace('_', ' '),
    };

    internal static string FormatStep(Scgs.Hotseat.Product.ProductHotseatSelectionStep step) =>
        step switch
        {
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.None => "选择一张牌或场上对象",
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.ChooseSource => "选择行动来源",
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.ChooseAction => "选择要执行的行动",
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.ChooseMode => "选择效果模式",
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.ChooseAdditionalCost => "选择额外代价",
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.ChooseSlot => "选择放置格位",
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.ChooseTarget => "选择效果或攻击目标",
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.ChooseAdvance => "选择是否动用未来",
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.ChooseChoiceOptions => "完成待处理选择",
            Scgs.Hotseat.Product.ProductHotseatSelectionStep.Ready => "行动已准备",
            _ => "继续选择",
        };

    internal static string FormatEvent(V05.GameEventView gameEvent, V05.PlayerId viewer)
    {
        string actor = gameEvent.Player == viewer ? "你" : "对手";
        string body = gameEvent.Type switch
        {
            V05.EventType.MatchStarted => "比赛开始。",
            V05.EventType.TurnStarted => $"{actor}的回合开始。",
            V05.EventType.TurnEnded => $"{actor}结束回合。",
            V05.EventType.CardDrawn => gameEvent.HiddenCard
                ? "对手抽了一张牌。"
                : $"{actor}抽了一张牌。",
            V05.EventType.FatigueDamage => $"{actor}受到 {gameEvent.Value} 点疲劳伤害。",
            V05.EventType.PpChanged => $"{actor}的 PP 变为 {gameEvent.Value}/{gameEvent.SecondaryValue}。",
            V05.EventType.CracksChanged => $"{actor}的裂痕变为 {gameEvent.Value}。",
            V05.EventType.PermanentEntered => $"{actor}打出了一张永久物。",
            V05.EventType.PermanentDamaged => $"{actor}的永久物受到 {gameEvent.Value} 点伤害。",
            V05.EventType.LeaderDamaged => $"{actor}的主战者受到 {gameEvent.Value} 点伤害。",
            V05.EventType.LeaderHealed => $"{actor}的主战者回复 {gameEvent.Value} 点生命。",
            V05.EventType.PermanentDestroyed => $"{actor}的永久物被破坏。",
            V05.EventType.AttackDeclared => $"{actor}宣言攻击。",
            V05.EventType.AttackCancelled => "攻击被取消。",
            V05.EventType.FollowerEvolved => $"{actor}进化了随从。",
            V05.EventType.TrapActivated => $"{actor}发动了伏策。",
            V05.EventType.PlayerSurrendered => $"{actor}投降。",
            V05.EventType.MatchEnded => "比赛结束。",
            V05.EventType.MulliganCompleted => $"{actor}完成调度。",
            _ => gameEvent.Text.Length == 0 ? $"事件 {(uint)gameEvent.Type}" : gameEvent.Text,
        };
        return $"#{gameEvent.Sequence}  {body}";
    }
}
