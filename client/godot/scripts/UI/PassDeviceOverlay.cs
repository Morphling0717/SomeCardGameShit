using Godot;

namespace Scgs.GodotClient.UI;

public sealed partial class PassDeviceOverlay : Control
{
    private Label _prompt = null!;
    private Button _revealButton = null!;

    public event Action? RevealRequested;

    public event Action? ExitRequested;

    public bool IsCovering => Visible;

    public override void _Ready()
    {
        _prompt = GetNode<Label>("%Prompt");
        _revealButton = GetNode<Button>("%RevealButton");
        _revealButton.Pressed += OnRevealPressed;
        GetNode<Button>("%ExitButton").Pressed += () => ExitRequested?.Invoke();
    }

    public void Cover(string playerLabel)
    {
        _prompt.Text = $"请交给{playerLabel}\n\n确认周围无人能看到屏幕后再揭示。";
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        _revealButton.Disabled = false;
        _revealButton.GrabFocus();
    }

    public void CompleteReveal()
    {
        _revealButton.Disabled = true;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void KeepCoveredAfterFailure(string message)
    {
        _prompt.Text = $"快照读取失败\n\n{message}\n\n画面仍保持遮挡。";
        _revealButton.Disabled = false;
    }

    public void RequestRevealForSmoke()
    {
        OnRevealPressed();
    }

    private void OnRevealPressed()
    {
        if (!Visible || _revealButton.Disabled)
        {
            return;
        }

        _revealButton.Disabled = true;
        RevealRequested?.Invoke();
    }
}
