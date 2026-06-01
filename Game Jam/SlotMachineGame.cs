using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Game_Jam;

/// <summary>
/// Raylib-venster voor de slotmachine.
/// Beheert spelerstatus en roept SlotMachine aan voor de logica.
/// </summary>
public class SlotMachineGame
{
    // ── Venster ───────────────────────────────────────────────────────────────
    private const int W = 800, H = 600;

    // ── Spelconfiguratie ──────────────────────────────────────────────────────
    private const int StartingBalance = 100;
    private const int MinBet = 1;

    // ── Spelerstatus ──────────────────────────────────────────────────────────
    private int _balance = StartingBalance;
    private string _betInput = "10";
    private bool _betFocused;
    private int _currentBet;
    private int _pendingPayout;
    private Symbol[]? _finalSymbols;
    private Symbol[] _displaySymbols = Array.Empty<Symbol>();
    private string _statusMsg = "Welkom! Voer een inzet in en druk op DRAAIEN.";
    private StatusKind _statusKind = StatusKind.Neutral;
    private bool _gameOver;

    // ── Animatie ──────────────────────────────────────────────────────────────
    private bool _spinning;
    private double _spinStart;
    private double _lastShuffle;
    private readonly Random _rng = new();
    private readonly SlotMachine _machine = new();

    private enum StatusKind { Neutral, Win, Lose }

    // ── Run ───────────────────────────────────────────────────────────────────

    public void Run()
    {
        Raylib.InitWindow(W, H, "Slot Machine");
        Raylib.SetTargetFPS(60);

        _displaySymbols = _machine.CurrentSymbols;

        while (!Raylib.WindowShouldClose())
        {
            Update();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(18, 18, 35, 255));
            Draw();
            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        HandleBetInput();
        HandleAnimation();
        HandleClicks();
    }

    private void HandleBetInput()
    {
        if (!_betFocused || _spinning || _gameOver) return;

        int key = Raylib.GetCharPressed();
        while (key > 0)
        {
            if (key >= '0' && key <= '9' && _betInput.Length < 5)
                _betInput += (char)key;
            key = Raylib.GetCharPressed();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _betInput.Length > 0)
            _betInput = _betInput[..^1];

        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
            TrySpin();
    }

    private void HandleAnimation()
    {
        if (!_spinning) return;

        double now = Raylib.GetTime();
        if (now - _lastShuffle < 0.08) return;
        _lastShuffle = now;

        double elapsed = now - _spinStart;

        if (elapsed < 1.5)
        {
            for (int i = 0; i < 3; i++)
                _displaySymbols[i] = RandomSymbol();
        }
        else if (elapsed < 1.7)
        {
            _displaySymbols[0] = _finalSymbols![0];
            _displaySymbols[1] = RandomSymbol();
            _displaySymbols[2] = RandomSymbol();
        }
        else if (elapsed < 1.9)
        {
            _displaySymbols[1] = _finalSymbols![1];
            _displaySymbols[2] = RandomSymbol();
        }
        else
        {
            _displaySymbols = _finalSymbols!.ToArray();
            _spinning = false;
            FinishSpin();
        }
    }

    private void HandleClicks()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        Vector2 m = Raylib.GetMousePosition();

        _betFocused = Hit(m, BetFieldRect());

        if (!_spinning && !_gameOver && Hit(m, SpinButtonRect())) TrySpin();
        if (_spinning && Hit(m, StopButtonRect()))               ForceStop();
        if (_gameOver && Hit(m, RestartButtonRect()))            Restart();
    }

    // ── Game logica ───────────────────────────────────────────────────────────

    private void TrySpin()
    {
        if (_spinning || _gameOver) return;
        if (!int.TryParse(_betInput, out int bet) || bet < MinBet || bet > _balance)
        {
            SetStatus($"Ongeldige inzet! Voer {MinBet}–{_balance} in.", StatusKind.Lose);
            return;
        }

        _currentBet    = bet;
        _balance      -= bet;
        _betFocused    = false;
        _pendingPayout = _machine.SpinAndCalculate(bet);
        _finalSymbols  = _machine.CurrentSymbols;
        _spinning      = true;
        _spinStart     = Raylib.GetTime();
        _lastShuffle   = _spinStart;

        SetStatus("Draaien...", StatusKind.Neutral);
    }

    private void ForceStop()
    {
        _displaySymbols = _finalSymbols!.ToArray();
        _spinning = false;
        FinishSpin();
    }

    private void FinishSpin()
    {
        _balance += _pendingPayout;

        if (_balance <= 0)
        {
            _gameOver = true;
            SetStatus("GAME OVER!  U heeft geen munten meer.", StatusKind.Lose);
            return;
        }

        if (_pendingPayout == 0)
        {
            SetStatus($"Helaas! Geen match. -{_currentBet} munten.", StatusKind.Lose);
            return;
        }

        int profit   = _pendingPayout - _currentBet;
        bool jackpot = _pendingPayout != (int)(_currentBet * 1.5);
        string tag   = jackpot ? "JACKPOT!  3x hetzelfde!" : "Kleine win!  2x hetzelfde.";
        string sign  = profit >= 0 ? $"+{profit}" : $"{profit}";
        SetStatus($"{tag}  Uitbetaling: {_pendingPayout} ({sign})", StatusKind.Win);
    }

    private void Restart()
    {
        _balance        = StartingBalance;
        _betInput       = "10";
        _gameOver       = false;
        _spinning       = false;
        _displaySymbols = _machine.CurrentSymbols;
        SetStatus("Welkom terug! Druk op DRAAIEN.", StatusKind.Neutral);
    }

    private void SetStatus(string msg, StatusKind kind)
    {
        _statusMsg  = msg;
        _statusKind = kind;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    private void Draw()
    {
        DrawTitle();
        DrawScoreBar();
        DrawWheels();
        DrawPayTable();
        DrawButtons();
        DrawStatus();
        if (_gameOver) DrawGameOverOverlay();
    }

    private void DrawTitle()
    {
        const string title = "SLOT  MACHINE";
        int tw = Raylib.MeasureText(title, 52);
        Raylib.DrawText(title, (W - tw) / 2, 16, 52, Color.Gold);
        Raylib.DrawRectangle(40, 78, W - 80, 2, new Color(80, 70, 20, 255));
    }

    private void DrawScoreBar()
    {
        string bal = $"Saldo:  {_balance} munten";
        Raylib.DrawText(bal, 50, 94, 22, Color.White);

        Raylib.DrawText("Inzet:", W - 230, 98, 20, new Color(180, 180, 180, 255));
        DrawTextBox(BetFieldRect(), _betInput, _betFocused);
    }

    private void DrawWheels()
    {
        for (int i = 0; i < 3; i++)
            DrawWheel(i);
    }

    private void DrawWheel(int index)
    {
        Rectangle rect = WheelRect(index);
        Symbol sym     = _displaySymbols[index];
        Color symColor = SymbolColor(sym);
        Color bgColor  = DimColor(symColor, 0.12f);

        Raylib.DrawRectangleRec(rect, bgColor);
        Raylib.DrawRectangleLinesEx(rect, 3, _spinning ? Color.DarkGray : Color.Gold);

        // Wiel-label bovenaan
        string lbl = $"Wiel {index + 1}";
        int lw = Raylib.MeasureText(lbl, 15);
        Raylib.DrawText(lbl, (int)(rect.X + (rect.Width - lw) / 2), (int)rect.Y + 6, 15, Color.Gray);

        // Symboolnaam gecentreerd
        string sym_text = SymbolText(sym);
        int fs = 44;
        int tw = Raylib.MeasureText(sym_text, fs);
        Raylib.DrawText(sym_text,
            (int)(rect.X + (rect.Width - tw) / 2),
            (int)(rect.Y + (rect.Height - fs) / 2 + 8),
            fs, symColor);

        // Multiplier onderaan
        string mult = $"x{sym.Multiplier}";
        int mw = Raylib.MeasureText(mult, 17);
        Raylib.DrawText(mult,
            (int)(rect.X + (rect.Width - mw) / 2),
            (int)(rect.Y + rect.Height - 26),
            17, new Color(130, 130, 130, 255));
    }

    private void DrawPayTable()
    {
        int tableX = 50, tableY = 310;
        Raylib.DrawText("Uitbetalingen:", tableX, tableY, 17, Color.Gray);
        tableY += 22;

        foreach (var sym in Symbol.All)
        {
            Color c = SymbolColor(sym);
            string line = $"{SymbolText(sym),-5}  3x = x{sym.Multiplier}   2x = x1.5";
            Raylib.DrawText(line, tableX, tableY, 16, c);
            tableY += 20;
        }
    }

    private void DrawButtons()
    {
        bool canSpin = !_spinning && !_gameOver && _balance >= MinBet;
        DrawButton(SpinButtonRect(), "DRAAIEN", canSpin, Color.DarkGreen);
        DrawButton(StopButtonRect(),  "STOP",    _spinning, Color.DarkGray);

        if (_gameOver)
            DrawButton(RestartButtonRect(), "OPNIEUW SPELEN", true, Color.DarkGreen);
    }

    private void DrawStatus()
    {
        Color c = _statusKind switch
        {
            StatusKind.Win  => Color.Yellow,
            StatusKind.Lose => Color.Red,
            _               => new Color(180, 180, 180, 255)
        };

        int fs = 21;
        int tw = Raylib.MeasureText(_statusMsg, fs);
        int x  = Math.Max(30, (W - tw) / 2);
        Raylib.DrawText(_statusMsg, x, 540, fs, c);
    }

    private void DrawGameOverOverlay()
    {
        // Semi-transparante achtergrond
        Raylib.DrawRectangle(0, 0, W, H, new Color(0, 0, 0, 170));

        const string title = "GAME  OVER";
        int tw = Raylib.MeasureText(title, 72);
        Raylib.DrawText(title, (W - tw) / 2, H / 2 - 110, 72, Color.Red);

        const string sub = "U heeft geen munten meer.";
        int sw = Raylib.MeasureText(sub, 26);
        Raylib.DrawText(sub, (W - sw) / 2, H / 2 - 20, 26, Color.White);

        DrawButton(RestartButtonRect(), "OPNIEUW SPELEN", true, Color.DarkGreen);
    }

    // ── Teken-helpers ─────────────────────────────────────────────────────────

    private void DrawTextBox(Rectangle rect, string text, bool focused)
    {
        Raylib.DrawRectangleRec(rect, new Color(28, 28, 50, 255));
        Raylib.DrawRectangleLinesEx(rect, 2, focused ? Color.Yellow : Color.Gray);

        bool showCursor = focused && (int)(Raylib.GetTime() * 2) % 2 == 0;
        string display  = text + (showCursor ? "|" : "");
        Raylib.DrawText(display, (int)rect.X + 8, (int)rect.Y + 8, 20, Color.White);
    }

    private void DrawButton(Rectangle rect, string label, bool enabled, Color normalBg)
    {
        Vector2 m  = Raylib.GetMousePosition();
        bool hover = enabled && Hit(m, rect);

        Color bg = !enabled ? new Color(40, 40, 50, 255)
                 : hover    ? Color.Yellow
                 :            normalBg;

        Color fg = !enabled ? Color.DarkGray
                 : hover    ? Color.Black
                 :            Color.White;

        Color border = enabled ? Color.White : new Color(60, 60, 60, 255);

        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 2, border);

        int tw = Raylib.MeasureText(label, 20);
        Raylib.DrawText(label,
            (int)(rect.X + (rect.Width  - tw) / 2),
            (int)(rect.Y + (rect.Height - 20) / 2),
            20, fg);
    }

    // ── Rechthoeken ───────────────────────────────────────────────────────────

    private static Rectangle WheelRect(int i)
    {
        const float wheelW = 175f, wheelH = 155f, gap = 18f;
        float startX = (W - 3f * wheelW - 2f * gap) / 2f;
        return new Rectangle(startX + i * (wheelW + gap), 128f, wheelW, wheelH);
    }

    private static Rectangle SpinButtonRect()    => new Rectangle(480, 315, 130, 46);
    private static Rectangle StopButtonRect()    => new Rectangle(620, 315, 130, 46);
    private static Rectangle RestartButtonRect() => new Rectangle((W - 200) / 2f, H / 2f + 50, 200, 50);
    private static Rectangle BetFieldRect()      => new Rectangle(W - 185f, 89f, 135f, 36f);

    // ── Symbool-helpers ───────────────────────────────────────────────────────

    private static Color SymbolColor(Symbol sym) => sym.Color switch
    {
        "red"          => new Color(230, 60,  60,  255),
        "yellow"       => new Color(255, 220, 30,  255),
        "gold3_1"      => new Color(215, 160, 0,   255),
        "yellow1"      => new Color(255, 255, 80,  255),
        "deepskyblue1" => new Color(0,   185, 255, 255),
        _              => Color.White
    };

    private static string SymbolText(Symbol sym) => sym.Glyph switch
    {
        "🍒" => "KERS",
        "🍋" => "CITR",
        "🔔" => "BEL",
        "⭐" => "STER",
        "💎" => "DIA",
        _    => "???"
    };

    private static Color DimColor(Color c, float f) =>
        new Color((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f), (byte)255);

    private Symbol RandomSymbol() => Symbol.All[_rng.Next(Symbol.All.Length)];

    private static bool Hit(Vector2 m, Rectangle r) =>
        Raylib.CheckCollisionPointRec(m, r);
}
