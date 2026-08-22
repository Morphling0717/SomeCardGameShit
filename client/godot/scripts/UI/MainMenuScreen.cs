using Godot;
using Scgs.GodotClient.Match;

namespace Scgs.GodotClient.UI;

public sealed partial class MainMenuScreen : Control
{
    private static readonly (string Key, string Label)[] FixedDecks =
    [
        ("midrange", "常规中速"),
        ("advance", "预支实验")
    ];

    private OptionButton _player0Deck = null!;
    private OptionButton _player1Deck = null!;
    private Label _errorLabel = null!;

    public event Action<MatchSetup>? StartRequested;

    public override void _Ready()
    {
        _player0Deck = GetNode<OptionButton>("%Player0Deck");
        _player1Deck = GetNode<OptionButton>("%Player1Deck");
        _errorLabel = GetNode<Label>("%ErrorLabel");

        PopulateDecks(_player0Deck, defaultIndex: 0);
        PopulateDecks(_player1Deck, defaultIndex: 1);
        GetNode<Button>("%StartButton").Pressed += OnStartPressed;
    }

    public void SetBusy(bool busy)
    {
        _player0Deck.Disabled = busy;
        _player1Deck.Disabled = busy;
        GetNode<Button>("%StartButton").Disabled = busy;
    }

    public void ShowError(string message)
    {
        SetBusy(false);
        _errorLabel.Text = message;
        _errorLabel.Visible = true;
    }

    public void ShowUnavailable(string message)
    {
        SetBusy(true);
        _errorLabel.Text = message;
        _errorLabel.Visible = true;
    }

    private static void PopulateDecks(OptionButton selector, int defaultIndex)
    {
        selector.Clear();
        foreach (var (key, label) in FixedDecks)
        {
            selector.AddItem(label);
            selector.SetItemMetadata(selector.ItemCount - 1, key);
        }

        selector.Select(defaultIndex);
    }

    private void OnStartPressed()
    {
        _errorLabel.Visible = false;
        SetBusy(true);
        StartRequested?.Invoke(new MatchSetup(
            ReadDeckKey(_player0Deck),
            ReadDeckKey(_player1Deck)));
    }

    private static string ReadDeckKey(OptionButton selector)
    {
        return selector.GetItemMetadata(selector.Selected).AsString();
    }
}
