using Terminal.Gui;

namespace Game_Jam;

/// <summary>
/// Volledig Terminal.Gui TUI voor de slotmachine.
/// Beheert spelerstatus (saldo, inzet) en roept SlotMachine aan voor de logica.
/// </summary>
public class SlotMachineUI : Toplevel
{
    // ── Spelconfiguratie ──────────────────────────────────────────────────────
    private const int StartingBalance = 100;
    private const int MinBet = 1;

    // ── Spelerstatus ──────────────────────────────────────────────────────────
    private int _balance = StartingBalance;
    private readonly SlotMachine _machine = new();
    private readonly Random _rng = new();

    // ── UI-elementen ──────────────────────────────────────────────────────────
    private readonly Label _balanceLabel;
    private readonly TextField _betField;
    private readonly Label[] _wheelLabels = new Label[3];
    private readonly Button _spinButton;
    private readonly Button _stopButton;
    private readonly Label _statusLabel;

    // ── Animatiestatus ────────────────────────────────────────────────────────
    private object? _animToken;
    private int _elapsed;
    private int _pendingPayout;
    private Symbol[]? _finalSymbols;
    private int _currentBet;

    // ── Kleurschema's ─────────────────────────────────────────────────────────
    private readonly ColorScheme _winScheme;
    private readonly ColorScheme _loseScheme;
    private readonly ColorScheme _neutralScheme;

    public SlotMachineUI()
    {
        X = 0; Y = 0; Width = Dim.Fill(); Height = Dim.Fill();

        _winScheme    = MakeScheme(Color.BrightYellow, Color.Black);
        _loseScheme   = MakeScheme(Color.BrightRed,    Color.Black);
        _neutralScheme = MakeScheme(Color.White,        Color.Black);

        var win = new Window("🎰 Slot Machine")
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = _neutralScheme
        };

        // ── Scorebalk ─────────────────────────────────────────────────────────
        _balanceLabel = new Label($"Saldo: {_balance} munten")
        {
            X = 2, Y = 1, Width = 26
        };

        var betLabel = new Label("Inzet:")
        {
            X = Pos.Right(_balanceLabel) + 2, Y = 1
        };

        _betField = new TextField("10")
        {
            X = Pos.Right(betLabel) + 1, Y = 1, Width = 8
        };

        // ── Drie wielen (elk 13 breed, 5 hoog) ───────────────────────────────
        // Totale groepsbreedte: 3×13 + 2×2 (gap) = 43; half = 22
        const int wheelW = 13, wheelH = 5, wheelGap = 2;

        for (int i = 0; i < 3; i++)
        {
            var frame = new FrameView($"Wiel {i + 1}")
            {
                X = Pos.Center() - 22 + i * (wheelW + wheelGap),
                Y = 4,
                Width = wheelW,
                Height = wheelH,
                ColorScheme = _neutralScheme
            };

            _wheelLabels[i] = new Label("    🎰    ")
            {
                X = 1, Y = 1,
                Width = Dim.Fill(1)
            };

            frame.Add(_wheelLabels[i]);
            win.Add(frame);
        }

        // ── Knoppen ───────────────────────────────────────────────────────────
        _spinButton = new Button("Draaien")
        {
            X = Pos.Center() - 11, Y = 11
        };
        _spinButton.Clicked += OnSpin;

        _stopButton = new Button("Stop")
        {
            X = Pos.Center() + 2, Y = 11,
            Enabled = false
        };
        _stopButton.Clicked += OnStop;

        // ── Statusregel ───────────────────────────────────────────────────────
        _statusLabel = new Label("Welkom! Voer een inzet in en druk op [ Draaien ].")
        {
            X = 2, Y = 14,
            Width = Dim.Fill(2),
            ColorScheme = _neutralScheme
        };

        win.Add(_balanceLabel, betLabel, _betField,
                _spinButton, _stopButton, _statusLabel);
        Add(win);

        // Initiële symbolen tonen
        var init = _machine.CurrentSymbols;
        for (int i = 0; i < 3; i++)
            _wheelLabels[i].Text = FormatSymbol(init[i]);
    }

    // ── Acties ────────────────────────────────────────────────────────────────

    private void OnSpin()
    {
        if (_animToken != null) return;

        string betText = _betField.Text?.ToString()?.Trim() ?? "";
        if (!int.TryParse(betText, out int bet) || bet < MinBet || bet > _balance)
        {
            SetStatus($"Ongeldige inzet! Voer een getal in tussen {MinBet} en {_balance}.", _loseScheme);
            return;
        }

        _currentBet = bet;
        _balance -= bet;
        UpdateBalanceLabel();

        // Resultaat pre-berekenen zodat animatie enkel cosmetisch is
        _pendingPayout = _machine.SpinAndCalculate(bet);
        _finalSymbols  = _machine.CurrentSymbols;

        _spinButton.Enabled = false;
        _stopButton.Enabled = true;
        _elapsed = 0;
        SetStatus("🎰  Draaien...", _neutralScheme);

        _animToken = Application.MainLoop.AddTimeout(
            TimeSpan.FromMilliseconds(100), AnimationTick);
    }

    private void OnStop()
    {
        if (_animToken == null) return;
        Application.MainLoop.RemoveTimeout(_animToken);
        _animToken = null;

        for (int i = 0; i < 3; i++)
            _wheelLabels[i].Text = FormatSymbol(_finalSymbols![i]);

        FinishSpin();
        Application.Refresh();
    }

    // ── Animatietimer ─────────────────────────────────────────────────────────

    private bool AnimationTick(MainLoop _)
    {
        _elapsed++;

        if (_elapsed <= 15)
        {
            // Alle drie draaien (~1500 ms)
            for (int i = 0; i < 3; i++)
                _wheelLabels[i].Text = FormatSymbol(RandomSymbol());
        }
        else if (_elapsed <= 17)
        {
            // Wiel 1 stopt
            _wheelLabels[0].Text = FormatSymbol(_finalSymbols![0]);
            _wheelLabels[1].Text = FormatSymbol(RandomSymbol());
            _wheelLabels[2].Text = FormatSymbol(RandomSymbol());
        }
        else if (_elapsed <= 19)
        {
            // Wiel 2 stopt
            _wheelLabels[1].Text = FormatSymbol(_finalSymbols![1]);
            _wheelLabels[2].Text = FormatSymbol(RandomSymbol());
        }
        else
        {
            // Wiel 3 stopt → klaar
            _wheelLabels[2].Text = FormatSymbol(_finalSymbols![2]);
            FinishSpin();
            Application.Refresh();
            _animToken = null;
            return false;
        }

        Application.Refresh();
        return true;
    }

    // ── Resultaatverwerking ───────────────────────────────────────────────────

    private void FinishSpin()
    {
        _balance += _pendingPayout;
        UpdateBalanceLabel();
        _stopButton.Enabled = false;

        if (_balance <= 0)
        {
            SetStatus("GAME OVER!  U heeft geen munten meer.", _loseScheme);
            _spinButton.Enabled = false;
            return;
        }

        _spinButton.Enabled = true;

        if (_pendingPayout == 0)
        {
            SetStatus($"Helaas! Geen match. U verliest {_currentBet} munten.", _loseScheme);
            return;
        }

        int profit   = _pendingPayout - _currentBet;
        bool jackpot = _pendingPayout != (int)(_currentBet * 1.5);
        string tag   = jackpot ? "JACKPOT!  3x hetzelfde!" : "Kleine win!  2x hetzelfde.";
        string sign  = profit >= 0 ? $"+{profit}" : $"{profit}";
        SetStatus($"{tag}  Uitbetaling: {_pendingPayout} munten ({sign})", _winScheme);
    }

    // ── Hulpmethoden ──────────────────────────────────────────────────────────

    private static string FormatSymbol(Symbol sym) => $"    {sym.Glyph}    ";

    private Symbol RandomSymbol() => Symbol.All[_rng.Next(Symbol.All.Length)];

    private void UpdateBalanceLabel() =>
        _balanceLabel.Text = $"Saldo: {_balance} munten";

    private void SetStatus(string msg, ColorScheme scheme)
    {
        _statusLabel.Text = msg;
        _statusLabel.ColorScheme = scheme;
    }

    private static ColorScheme MakeScheme(Color fore, Color back) => new ColorScheme
    {
        Normal    = Application.Driver.MakeAttribute(fore, back),
        Focus     = Application.Driver.MakeAttribute(fore, back),
        HotNormal = Application.Driver.MakeAttribute(Color.BrightYellow, back),
        HotFocus  = Application.Driver.MakeAttribute(Color.BrightYellow, back),
        Disabled  = Application.Driver.MakeAttribute(Color.Gray, back)
    };
}
