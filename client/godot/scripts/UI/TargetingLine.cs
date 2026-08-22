// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.UI;

public sealed partial class TargetingLine : Control
{
    private Vector2 _origin;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(false);
        Visible = false;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (!Visible)
        {
            return;
        }

        Vector2 end = GetLocalMousePosition();
        Color color = new(1.0f, 0.38f, 0.32f, 0.92f);
        DrawLine(_origin, end, color, 5.0f, antialiased: true);
        DrawCircle(end, 10.0f, color);
    }

    public void BeginAtGlobal(Vector2 globalOrigin)
    {
        _origin = globalOrigin - GlobalPosition;
        Visible = true;
        SetProcess(true);
        QueueRedraw();
    }

    public void Stop()
    {
        SetProcess(false);
        Visible = false;
    }
}
