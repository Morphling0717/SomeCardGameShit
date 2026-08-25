// SPDX-License-Identifier: GPL-3.0-or-later
using V05 = Scgs.Client.V05;

namespace Scgs.Hotseat;

/// <summary>
/// Viewer-safe projection of a schema-2 pending choice. This is intentionally
/// independent of the existing v04 match controller until the product client
/// switches to v05 in Gate 5C.
/// </summary>
public sealed record ProductHotseatChoiceState
{
    private ProductHotseatChoiceState(
        ulong revision,
        HotseatSelectionStep step,
        bool waitingForOpponent,
        string? choiceId,
        ulong minimumSelections,
        ulong maximumSelections,
        bool ordered,
        IReadOnlyList<V05.PendingChoiceOptionView> options)
    {
        Revision = revision;
        Step = step;
        WaitingForOpponent = waitingForOpponent;
        ChoiceId = choiceId;
        MinimumSelections = minimumSelections;
        MaximumSelections = maximumSelections;
        Ordered = ordered;
        Options = Array.AsReadOnly(options.ToArray());
    }

    public ulong Revision { get; }

    public HotseatSelectionStep Step { get; }

    public bool WaitingForOpponent { get; }

    public string? ChoiceId { get; }

    public ulong MinimumSelections { get; }

    public ulong MaximumSelections { get; }

    public bool Ordered { get; }

    public IReadOnlyList<V05.PendingChoiceOptionView> Options { get; }

    public bool RequiresInput =>
        !WaitingForOpponent && Step is HotseatSelectionStep.ChooseMode or
            HotseatSelectionStep.ChooseCards or
            HotseatSelectionStep.OrderTriggers or
            HotseatSelectionStep.ChooseAdditionalCost;

    public static ProductHotseatChoiceState From(
        V05.PendingChoiceView choice,
        V05.PlayerId viewer)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ValidatePlayer(viewer);
        if (!choice.Pending)
        {
            return new ProductHotseatChoiceState(
                choice.Revision,
                HotseatSelectionStep.None,
                false,
                null,
                0,
                0,
                false,
                Array.Empty<V05.PendingChoiceOptionView>());
        }

        V05.PlayerId chooser = choice.Chooser ??
            throw new ArgumentException("A pending v05 choice must name its chooser.", nameof(choice));
        ValidatePlayer(chooser);
        if (chooser != viewer)
        {
            if (choice.ChoiceId is not null || choice.Kind is not null ||
                choice.MinimumSelections is not null || choice.MaximumSelections is not null ||
                choice.Ordered is not null || choice.Options.Length != 0)
            {
                throw new ArgumentException(
                    "An opponent pending choice must not expose private choice details.",
                    nameof(choice));
            }
            return new ProductHotseatChoiceState(
                choice.Revision,
                HotseatSelectionStep.None,
                true,
                null,
                0,
                0,
                false,
                Array.Empty<V05.PendingChoiceOptionView>());
        }

        string choiceId = string.IsNullOrWhiteSpace(choice.ChoiceId)
            ? throw new ArgumentException("A selectable v05 choice needs an opaque choice id.", nameof(choice))
            : choice.ChoiceId;
        V05.PendingChoiceKind kind = choice.Kind ??
            throw new ArgumentException("A selectable v05 choice needs a kind.", nameof(choice));
        ulong minimum = choice.MinimumSelections ??
            throw new ArgumentException("A selectable v05 choice needs a minimum.", nameof(choice));
        ulong maximum = choice.MaximumSelections ??
            throw new ArgumentException("A selectable v05 choice needs a maximum.", nameof(choice));
        if (minimum > maximum || maximum > (ulong)choice.Options.Length)
        {
            throw new ArgumentException("The v05 choice selection bounds are invalid.", nameof(choice));
        }
        if (choice.Options.Any(option => string.IsNullOrWhiteSpace(option.OptionId)) ||
            choice.Options.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count() !=
                choice.Options.Length)
        {
            throw new ArgumentException("The v05 choice option ids must be non-empty and unique.", nameof(choice));
        }

        HotseatSelectionStep step = kind switch
        {
            V05.PendingChoiceKind.Mode => HotseatSelectionStep.ChooseMode,
            V05.PendingChoiceKind.Cards => HotseatSelectionStep.ChooseCards,
            V05.PendingChoiceKind.TriggerOrder => HotseatSelectionStep.OrderTriggers,
            V05.PendingChoiceKind.AdditionalCost => HotseatSelectionStep.ChooseAdditionalCost,
            _ => throw new ArgumentOutOfRangeException(nameof(choice), kind, "Unknown v05 choice kind."),
        };
        bool ordered = choice.Ordered ??
            throw new ArgumentException("A selectable v05 choice needs an ordering flag.", nameof(choice));
        if (step == HotseatSelectionStep.OrderTriggers && !ordered)
        {
            throw new ArgumentException("Trigger-order choices must preserve order.", nameof(choice));
        }

        return new ProductHotseatChoiceState(
            choice.Revision,
            step,
            false,
            choiceId,
            minimum,
            maximum,
            ordered,
            choice.Options);
    }

    private static void ValidatePlayer(V05.PlayerId player)
    {
        if (player is not V05.PlayerId.Player0 and not V05.PlayerId.Player1)
        {
            throw new ArgumentOutOfRangeException(nameof(player), player, "Unknown v05 player.");
        }
    }
}
