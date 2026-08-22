// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.UI;

public sealed partial class DirectDropButton : Button
{
    private static readonly StringName DragMarker = new("scgs_direct_drag");
    private bool _dropEnabled;

    public event Action? DropReceived;

    public void SetDropEnabled(bool enabled)
    {
        _dropEnabled = enabled;
        Modulate = enabled
            ? new Color(0.66f, 1.0f, 0.89f, 1.0f)
            : Colors.White;
    }

    public void SetDirectInteraction(bool clickable, bool droppable)
    {
        _dropEnabled = droppable;
        Disabled = !clickable;
        FocusMode = clickable ? FocusModeEnum.All : FocusModeEnum.None;
        Modulate = clickable || droppable
            ? new Color(0.66f, 1.0f, 0.89f, 1.0f)
            : Colors.White;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!_dropEnabled || data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        Godot.Collections.Dictionary payload = data.AsGodotDictionary();
        return payload.ContainsKey(DragMarker) && payload[DragMarker].AsBool();
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (_CanDropData(atPosition, data))
        {
            DropReceived?.Invoke();
        }
    }
}
