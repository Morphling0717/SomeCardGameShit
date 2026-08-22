// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;

namespace Scgs.GodotClient.UI;

public sealed partial class EventLogPanel : PanelContainer
{
    private const int MaximumEntries = 60;
    private readonly Queue<string> _entries = new();
    private RichTextLabel _log = null!;

    internal bool HasSensitiveContentForSmoke =>
        _entries.Count != 0 || !string.IsNullOrEmpty(_log.Text);

    public override void _Ready()
    {
        _log = GetNode<RichTextLabel>("%EventLogText");
    }

    public void Append(PlayerId viewer, IEnumerable<GameEventView> events)
    {
        foreach (GameEventView gameEvent in events)
        {
            _entries.Enqueue(GameEventPresentation.Format(gameEvent, viewer));
        }
        while (_entries.Count > MaximumEntries)
        {
            _entries.Dequeue();
        }
        _log.Text = string.Join('\n', _entries);
        _log.ScrollToLine(Math.Max(0, _entries.Count - 1));
    }

    public void Replace(PlayerId viewer, IEnumerable<GameEventView> events)
    {
        _entries.Clear();
        Append(viewer, events);
    }

    public void ClearSensitive()
    {
        _entries.Clear();
        _log.Text = string.Empty;
    }
}
