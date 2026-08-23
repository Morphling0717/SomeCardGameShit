// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;

namespace Scgs.Hotseat;

public enum HotseatSurfaceKind
{
    HandCard,
    Unit,
    Tactic,
    UnitSlot,
    TacticSlot,
    StandbyCard,
    Leader,
    CastZone,
}

public enum HotseatSurfaceGesture
{
    Click,
    Drag,
}

public enum HotseatSurfaceIntentStatus
{
    Applied,
    CommandPrepared,
    RejectedMode,
    StaleRevision,
    InvalidSurface,
    Ambiguous,
}

public readonly record struct HotseatSurfaceRef(
    HotseatSurfaceKind Kind,
    PlayerId? Player,
    int? Index,
    ulong? InstanceId)
{
    public static HotseatSurfaceRef HandCard(PlayerId player, int index, ulong instanceId) =>
        new(HotseatSurfaceKind.HandCard, player, index, instanceId);

    public static HotseatSurfaceRef Unit(PlayerId player, int index, ulong instanceId) =>
        new(HotseatSurfaceKind.Unit, player, index, instanceId);

    public static HotseatSurfaceRef Tactic(PlayerId player, int index, ulong instanceId) =>
        new(HotseatSurfaceKind.Tactic, player, index, instanceId);

    public static HotseatSurfaceRef UnitSlot(PlayerId player, int index) =>
        new(HotseatSurfaceKind.UnitSlot, player, index, null);

    public static HotseatSurfaceRef TacticSlot(PlayerId player, int index) =>
        new(HotseatSurfaceKind.TacticSlot, player, index, null);

    public static HotseatSurfaceRef StandbyCard(PlayerId player, int index, ulong instanceId) =>
        new(HotseatSurfaceKind.StandbyCard, player, index, instanceId);

    public static HotseatSurfaceRef Leader(PlayerId player) =>
        new(HotseatSurfaceKind.Leader, player, null, null);

    public static HotseatSurfaceRef CastZone() =>
        new(HotseatSurfaceKind.CastZone, null, null, null);
}

public sealed record HotseatSurfaceIntent(
    ulong Revision,
    HotseatSurfaceGesture Gesture,
    HotseatSurfaceRef Source)
{
    public HotseatSurfaceRef? Destination { get; init; }

    public ActionKind? Action { get; init; }
}

public sealed record HotseatSurfaceIntentResult
{
    internal HotseatSurfaceIntentResult(
        HotseatSurfaceIntentStatus status,
        HotseatSelectionStep nextStep,
        GameCommandRequest? canonicalCommand,
        bool stateChanged)
    {
        Status = status;
        NextStep = nextStep;
        CanonicalCommand = canonicalCommand;
        StateChanged = stateChanged;
    }

    public HotseatSurfaceIntentStatus Status { get; }

    public HotseatSelectionStep NextStep { get; }

    public GameCommandRequest? CanonicalCommand { get; }

    public bool StateChanged { get; }

    public bool Accepted => Status is HotseatSurfaceIntentStatus.Applied or
        HotseatSurfaceIntentStatus.CommandPrepared;

    public bool CommandPrepared => Status == HotseatSurfaceIntentStatus.CommandPrepared;

    public bool RequiresFurtherSelection =>
        Status == HotseatSurfaceIntentStatus.Applied &&
        NextStep is not HotseatSelectionStep.None and
            not HotseatSelectionStep.Ready;

    public bool RequiresExplicitCommit =>
        Status == HotseatSurfaceIntentStatus.Applied &&
        NextStep == HotseatSelectionStep.Ready;
}

public sealed class HotseatSurfaceInteractionCoordinator
{
    private readonly HotseatMatchController controller;

    public event Action? CommandPreparing;

    public HotseatSurfaceInteractionCoordinator(HotseatMatchController controller) =>
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));

    public HotseatSurfaceIntentResult ApplyIntent(HotseatSurfaceIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        HotseatUiState initialState = controller.State;
        if (initialState.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction ||
            initialState.Snapshot is null || !initialState.Viewer.HasValue)
        {
            return Rejected(HotseatSurfaceIntentStatus.RejectedMode, initialState);
        }

        if (intent.Revision != initialState.Interaction.Revision)
        {
            return Rejected(HotseatSurfaceIntentStatus.StaleRevision, initialState);
        }

        if (!Enum.IsDefined(intent.Gesture) ||
            (intent.Action.HasValue && !Enum.IsDefined(intent.Action.Value)) ||
            !TryValidateSurface(initialState, intent.Source) ||
            (intent.Destination.HasValue &&
             !TryValidateSurface(initialState, intent.Destination.Value)))
        {
            return Rejected(HotseatSurfaceIntentStatus.InvalidSurface, initialState);
        }

        IntentPlanResult planned = intent.Gesture switch
        {
            HotseatSurfaceGesture.Click => PlanClick(initialState, intent),
            HotseatSurfaceGesture.Drag => PlanDrag(initialState, intent),
            _ => IntentPlanResult.Invalid,
        };
        if (planned.Status.HasValue)
        {
            return Rejected(planned.Status.Value, initialState);
        }

        IntentPlan plan = planned.Plan!;
        if (plan.BeginSource)
        {
            controller.BeginSourceSelection(plan.Source);
            if (controller.State.Selection.Source != plan.Source)
            {
                return Interrupted(initialState);
            }
        }

        if (plan.Action.HasValue && controller.State.Selection.Action != plan.Action)
        {
            controller.ChooseAction(plan.Action.Value);
            if (controller.State.Selection.Action != plan.Action)
            {
                return Interrupted(initialState);
            }
        }

        switch (plan.Role)
        {
            case DestinationRole.None:
                break;
            case DestinationRole.Target:
                controller.SelectTarget(plan.Target);
                if (!controller.State.Selection.HasTarget ||
                    !Equals(controller.State.Selection.Target, plan.Target))
                {
                    return Interrupted(initialState);
                }

                break;
            case DestinationRole.Slot:
                controller.SelectSlot(plan.Slot);
                if (!controller.State.Selection.HasSlot ||
                    controller.State.Selection.Slot != plan.Slot)
                {
                    return Interrupted(initialState);
                }

                break;
            case DestinationRole.Donor:
                controller.SelectDonor(plan.Donor);
                if (!controller.State.Selection.HasDonor ||
                    controller.State.Selection.Donor != plan.Donor)
                {
                    return Interrupted(initialState);
                }

                break;
            default:
                throw new InvalidOperationException("Unsupported surface destination role.");
        }

        HotseatSelectionStep nextStep = controller.State.Interaction.Step;
        GameCommandRequest? canonical = CloneCommand(
            controller.State.Interaction.CanonicalAction?.Command);
        if (plan.PrepareWhenReady && nextStep == HotseatSelectionStep.Ready)
        {
            CommandPreparing?.Invoke();
            if (!controller.PrepareSelectedCommand())
            {
                throw new InvalidOperationException(
                    "A preflighted surface intent failed to prepare its canonical command.");
            }

            return new HotseatSurfaceIntentResult(
                HotseatSurfaceIntentStatus.CommandPrepared,
                nextStep,
                canonical,
                stateChanged: !ReferenceEquals(initialState, controller.State));
        }

        return new HotseatSurfaceIntentResult(
            HotseatSurfaceIntentStatus.Applied,
            nextStep,
            canonical,
            stateChanged: !ReferenceEquals(initialState, controller.State));
    }

    private static IntentPlanResult PlanClick(
        HotseatUiState state,
        HotseatSurfaceIntent intent)
    {
        if (intent.Destination.HasValue)
        {
            return IntentPlanResult.Invalid;
        }

        if (intent.Source.Kind == HotseatSurfaceKind.CastZone)
        {
            if (intent.Action is not null and not ActionKind.CastSpell ||
                state.Selection.Action != ActionKind.CastSpell)
            {
                return IntentPlanResult.Invalid;
            }

            bool nullTargetExists = state.CandidateOptions.Actions.Any(action =>
                action.Command.Action == ActionKind.CastSpell &&
                action.Command.Target is null);
            if (!nullTargetExists)
            {
                return IntentPlanResult.Invalid;
            }

            return IntentPlanResult.Success(new IntentPlan(
                BeginSource: false,
                Source: state.Selection.Source!.Value,
                Action: ActionKind.CastSpell,
                Role: state.Selection.HasTarget && state.Selection.Target is null
                    ? DestinationRole.None
                    : DestinationRole.Target,
                Target: null,
                Slot: null,
                Donor: null,
                PrepareWhenReady: true));
        }

        if (intent.Action.HasValue)
        {
            if (!TryGetCardSource(intent.Source, out ulong source) ||
                !state.LegalActions.Any(action =>
                    action.Command.Source == source &&
                    action.Command.Action == intent.Action.Value))
            {
                return IntentPlanResult.Invalid;
            }

            return IntentPlanResult.Success(new IntentPlan(
                BeginSource: state.Selection.Source != source,
                Source: source,
                Action: intent.Action,
                Role: DestinationRole.None,
                Target: null,
                Slot: null,
                Donor: null,
                PrepareWhenReady: true));
        }

        DestinationRole expectedRole = RoleForStep(state.Interaction.Step);
        if (expectedRole != DestinationRole.None &&
            TryResolveDestination(
                state.CandidateOptions.Actions,
                intent.Source,
                state.Viewer!.Value,
                expectedRole,
                out DestinationValue destination))
        {
            return IntentPlanResult.Success(new IntentPlan(
                BeginSource: false,
                Source: state.Selection.Source!.Value,
                Action: state.Selection.Action,
                Role: expectedRole,
                Target: destination.Target,
                Slot: destination.Slot,
                Donor: destination.Donor,
                PrepareWhenReady: true));
        }

        if (!TryGetCardSource(intent.Source, out ulong clickedSource) ||
            !state.LegalActions.Any(action => action.Command.Source == clickedSource))
        {
            return IntentPlanResult.Invalid;
        }

        if (state.Selection.Source == clickedSource)
        {
            return new IntentPlanResult(HotseatSurfaceIntentStatus.Ambiguous, null);
        }

        return IntentPlanResult.Success(new IntentPlan(
            BeginSource: true,
            Source: clickedSource,
            Action: null,
            Role: DestinationRole.None,
            Target: null,
            Slot: null,
            Donor: null,
            PrepareWhenReady: false));
    }

    private static IntentPlanResult PlanDrag(
        HotseatUiState state,
        HotseatSurfaceIntent intent)
    {
        if (!intent.Destination.HasValue ||
            !TryGetCardSource(intent.Source, out ulong source))
        {
            return IntentPlanResult.Invalid;
        }

        ActionKind? selectedAction = state.Selection.Source == source
            ? state.Selection.Action
            : null;
        if (intent.Action.HasValue && selectedAction.HasValue &&
            intent.Action.Value != selectedAction.Value)
        {
            return IntentPlanResult.Invalid;
        }

        ActionKind? constrainedAction = intent.Action ?? selectedAction;
        LegalAction[] sourceCandidates = state.LegalActions.Where(action =>
            action.Command.Source == source &&
            (!constrainedAction.HasValue ||
             action.Command.Action == constrainedAction.Value)).ToArray();
        if (sourceCandidates.Length == 0)
        {
            return IntentPlanResult.Invalid;
        }

        var matches = new List<(LegalAction Action, DestinationRole Role, DestinationValue Value)>();
        foreach (LegalAction action in sourceCandidates)
        {
            foreach ((DestinationRole role, DestinationValue value) in MatchDestinations(
                         action,
                         intent.Destination.Value,
                         state.Viewer!.Value))
            {
                matches.Add((action, role, value));
            }
        }

        if (matches.Count == 0)
        {
            return IntentPlanResult.Invalid;
        }

        ActionKind[] actionKinds = matches
            .Select(match => match.Action.Command.Action)
            .Distinct()
            .ToArray();
        if (actionKinds.Length != 1 || !Enum.IsDefined(actionKinds[0]))
        {
            return new IntentPlanResult(HotseatSurfaceIntentStatus.Ambiguous, null);
        }

        DestinationRole[] roles = matches
            .Where(match => match.Action.Command.Action == actionKinds[0])
            .Select(match => match.Role)
            .Distinct()
            .ToArray();
        if (roles.Length != 1)
        {
            return new IntentPlanResult(HotseatSurfaceIntentStatus.Ambiguous, null);
        }

        DestinationValue selected = matches.First(match =>
            match.Action.Command.Action == actionKinds[0] && match.Role == roles[0]).Value;
        return IntentPlanResult.Success(new IntentPlan(
            BeginSource: true,
            Source: source,
            Action: actionKinds[0],
            Role: roles[0],
            Target: selected.Target,
            Slot: selected.Slot,
            Donor: selected.Donor,
            PrepareWhenReady: true));
    }

    private static IEnumerable<(DestinationRole Role, DestinationValue Value)> MatchDestinations(
        LegalAction action,
        HotseatSurfaceRef surface,
        PlayerId viewer)
    {
        GameCommandRequest command = action.Command;
        switch (surface.Kind)
        {
            case HotseatSurfaceKind.Unit:
                {
                    Target target = Target.UnitTarget(surface.Player!.Value, surface.InstanceId!.Value);
                    if (Equals(command.Target, target))
                    {
                        yield return (DestinationRole.Target, DestinationValue.ForTarget(target));
                    }

                    if (surface.Player == viewer && command.ComponentDonor == surface.InstanceId)
                    {
                        yield return (
                            DestinationRole.Donor,
                            DestinationValue.ForDonor(surface.InstanceId.Value));
                    }

                    break;
                }
            case HotseatSurfaceKind.Leader:
                {
                    Target target = Target.Leader(surface.Player!.Value);
                    if (Equals(command.Target, target))
                    {
                        yield return (DestinationRole.Target, DestinationValue.ForTarget(target));
                    }

                    break;
                }
            case HotseatSurfaceKind.UnitSlot:
                if (surface.Player == viewer &&
                    command.Action is ActionKind.PlayUnit or ActionKind.Deploy &&
                    command.Slot == (ulong)surface.Index!.Value)
                {
                    yield return (
                        DestinationRole.Slot,
                        DestinationValue.ForSlot((ulong)surface.Index.Value));
                }

                break;
            case HotseatSurfaceKind.TacticSlot:
                if (surface.Player == viewer && command.Action == ActionKind.PlayTactic &&
                    command.Slot == (ulong)surface.Index!.Value)
                {
                    yield return (
                        DestinationRole.Slot,
                        DestinationValue.ForSlot((ulong)surface.Index.Value));
                }

                break;
            case HotseatSurfaceKind.CastZone:
                if (command.Action == ActionKind.CastSpell && command.Target is null)
                {
                    yield return (DestinationRole.Target, DestinationValue.ForTarget(null));
                }

                break;
        }
    }

    private static bool TryResolveDestination(
        IReadOnlyList<LegalAction> candidates,
        HotseatSurfaceRef surface,
        PlayerId viewer,
        DestinationRole expectedRole,
        out DestinationValue value)
    {
        DestinationValue[] values = candidates
            .SelectMany(action => MatchDestinations(action, surface, viewer))
            .Where(match => match.Role == expectedRole)
            .Select(match => match.Value)
            .Distinct()
            .ToArray();
        if (values.Length == 1)
        {
            value = values[0];
            return true;
        }

        value = default;
        return false;
    }

    private static DestinationRole RoleForStep(HotseatSelectionStep step) => step switch
    {
        HotseatSelectionStep.ChooseTarget => DestinationRole.Target,
        HotseatSelectionStep.ChooseSlot => DestinationRole.Slot,
        HotseatSelectionStep.ChooseDonor => DestinationRole.Donor,
        _ => DestinationRole.None,
    };

    private static bool TryValidateSurface(HotseatUiState state, HotseatSurfaceRef surface)
    {
        if (!Enum.IsDefined(surface.Kind))
        {
            return false;
        }

        if (surface.Kind == HotseatSurfaceKind.CastZone)
        {
            return !surface.Player.HasValue && !surface.Index.HasValue &&
                   !surface.InstanceId.HasValue;
        }

        if (!surface.Player.HasValue ||
            surface.Player.Value is not PlayerId.Player0 and not PlayerId.Player1)
        {
            return false;
        }

        PlayerView player = state.Snapshot!.Players[(int)surface.Player.Value];
        return surface.Kind switch
        {
            HotseatSurfaceKind.HandCard =>
                MatchesCard(player.Hand, surface.Index, surface.InstanceId),
            HotseatSurfaceKind.Unit =>
                MatchesCard(player.Units, surface.Index, surface.InstanceId),
            HotseatSurfaceKind.Tactic =>
                MatchesCard(player.Tactics, surface.Index, surface.InstanceId),
            HotseatSurfaceKind.StandbyCard =>
                MatchesCard(player.Standby, surface.Index, surface.InstanceId),
            HotseatSurfaceKind.UnitSlot =>
                IsSlot(surface, player.Units.Length),
            HotseatSurfaceKind.TacticSlot =>
                IsSlot(surface, player.Tactics.Length),
            HotseatSurfaceKind.Leader =>
                !surface.Index.HasValue && !surface.InstanceId.HasValue,
            _ => false,
        };
    }

    private static bool MatchesCard(
        IReadOnlyList<CardView?> cards,
        int? index,
        ulong? instanceId) =>
        index is >= 0 && index.Value < cards.Count && instanceId.HasValue &&
        cards[index.Value]?.InstanceId == instanceId;

    private static bool IsSlot(HotseatSurfaceRef surface, int slotCount) =>
        surface.Index is >= 0 && surface.Index.Value < slotCount &&
        !surface.InstanceId.HasValue;

    private static bool TryGetCardSource(HotseatSurfaceRef surface, out ulong source)
    {
        if (surface.Kind is HotseatSurfaceKind.HandCard or HotseatSurfaceKind.Unit or
                HotseatSurfaceKind.Tactic or HotseatSurfaceKind.StandbyCard &&
            surface.InstanceId.HasValue)
        {
            source = surface.InstanceId.Value;
            return true;
        }

        source = 0;
        return false;
    }

    private static HotseatSurfaceIntentResult Rejected(
        HotseatSurfaceIntentStatus status,
        HotseatUiState state) => new(
            status,
            state.Interaction.Step,
            CloneCommand(state.Interaction.CanonicalAction?.Command),
            stateChanged: false);

    private HotseatSurfaceIntentResult Interrupted(HotseatUiState initialState) => new(
        HotseatSurfaceIntentStatus.StaleRevision,
        controller.State.Interaction.Step,
        CloneCommand(controller.State.Interaction.CanonicalAction?.Command),
        stateChanged: !ReferenceEquals(initialState, controller.State));

    private static GameCommandRequest? CloneCommand(GameCommandRequest? command) =>
        command is null
            ? null
            : command with
            {
                MulliganCards = Array.AsReadOnly(command.MulliganCards.ToArray()),
            };

    private enum DestinationRole
    {
        None,
        Target,
        Slot,
        Donor,
    }

    private readonly record struct DestinationValue(
        Target? Target,
        ulong? Slot,
        ulong? Donor)
    {
        internal static DestinationValue ForTarget(Target? target) => new(target, null, null);

        internal static DestinationValue ForSlot(ulong slot) => new(null, slot, null);

        internal static DestinationValue ForDonor(ulong donor) => new(null, null, donor);
    }

    private sealed record IntentPlan(
        bool BeginSource,
        ulong Source,
        ActionKind? Action,
        DestinationRole Role,
        Target? Target,
        ulong? Slot,
        ulong? Donor,
        bool PrepareWhenReady);

    private sealed record IntentPlanResult(
        HotseatSurfaceIntentStatus? Status,
        IntentPlan? Plan)
    {
        internal static IntentPlanResult Invalid { get; } =
            new(HotseatSurfaceIntentStatus.InvalidSurface, null);

        internal static IntentPlanResult Success(IntentPlan plan) => new(null, plan);
    }
}
