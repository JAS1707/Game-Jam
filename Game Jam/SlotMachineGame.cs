using System;
using System.Numerics;
using Raylib_cs;

namespace Game_Jam;

public class SlotMachineGame
{
    private const int W = 800, H = 600;
    private const int StartingBalance = 100;
    private const int MinBet = 1;
    private const float SlotH = 65f;          // hoogte van één symboolslot in pixels
    private const float SpinSpeed = 16f;       // symbolen per seconde op volle snelheid
    private const float BrakeDuration = 0.5f;  // seconden om te vertragen
    private const float MinBrakeTravel = 1.5f; // minimale symbolen bij afremmen

    // Spelerstatus
    private int _balance = StartingBalance;
    private string _betInput = "10";
    private bool _betFocused;
    private int _currentBet;
    private int _pendingPayout;
    private Symbol[]? _finalSymbols;
    private string _statusMsg = "Welkom! Voer een inzet in en druk op DRAAIEN.";
    private StatusKind _statusKind = StatusKind.Neutral;
    private bool _gameOver;

    // Reel scroll-animatie (één continue positie per wiel in symbool-index-eenheden)
    private float[] _reelPos       = [0f, 0f, 0f];
    private bool[]  _wheelLanded   = [false, false, false];
    private bool[]  _braking       = [false, false, false];
    private float[] _brakeStartPos = [0f, 0f, 0f];
    private float[] _brakeDist     = [0f, 0f, 0f];
    private double[] _brakeTime    = [0.0, 0.0, 0.0];

    private bool _spinning;
    private bool _rigged;
    private double _spinStart;
    private double _lastTime;

    private readonly SlotMachine _machine = new();

    // Emoji-lettertype
    private Font _emojiFont;
    private bool _fontLoaded;

    private enum StatusKind { Neutral, Win, Lose }

    // ── Run ───────────────────────────────────────────────────────────────────

    public void Run()
    {
        Raylib.InitWindow(W, H, "Slot Machine");
        Raylib.SetTargetFPS(60);

        TryLoadEmojiFont();

        var init = _machine.CurrentSymbols;
        for (int i = 0; i < 3; i++)
            _reelPos[i] = Array.IndexOf(Symbol.All, init[i]);

        _lastTime = Raylib.GetTime();

        while (!Raylib.WindowShouldClose())
        {
            Update();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(18, 18, 35, 255));
            Draw();
            Raylib.EndDrawing();
        }

        if (_fontLoaded) Raylib.UnloadFont(_emojiFont);
        Raylib.CloseWindow();
    }

    private void TryLoadEmojiFont()
    {
        const string path = @"C:\Windows\Fonts\seguiemj.ttf";
        if (!System.IO.File.Exists(path)) return;
        int[] codepoints = [0x1F352, 0x1F34B, 0x1F514, 0x2B50, 0x1F48E];
        _emojiFont = Raylib.LoadFontEx(path, 64, codepoints, codepoints.Length);
        _fontLoaded = _emojiFont.GlyphCount > 0;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        double now = Raylib.GetTime();
        float dt = (float)(now - _lastTime);
        _lastTime = now;

        HandleBetInput();
        HandleAnimation(now, dt);
        HandleClicks();
        HandleSpaceCheat();
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

    private void HandleAnimation(double now, float dt)
    {
        if (!_spinning) return;

        double elapsed = now - _spinStart;
        double[] stopTimes = [1.4, 1.9, 2.4];

        for (int i = 0; i < 3; i++)
        {
            if (_wheelLanded[i]) continue;

            if (_braking[i])
            {
                // Cubic ease-out: snel starten, langzaam stoppen op exact doelsymbool
                float t = Math.Min((float)((now - _brakeTime[i]) / BrakeDuration), 1f);
                float eased = 1f - (float)Math.Pow(1.0 - t, 3);
                _reelPos[i] = _brakeStartPos[i] + _brakeDist[i] * eased;

                if (t >= 1f)
                {
                    _wheelLanded[i] = true;
                    if (_wheelLanded[0] && _wheelLanded[1] && _wheelLanded[2])
                    {
                        _spinning = false;
                        FinishSpin();
                    }
                }
            }
            else if (elapsed >= stopTimes[i])
            {
                StartBraking(i, now);
            }
            else
            {
                // Vrij draaien: aanloop + volle snelheid
                float speed = elapsed < 0.4 ? (float)(elapsed / 0.4 * SpinSpeed) : SpinSpeed;
                _reelPos[i] += speed * dt;
                float len = Symbol.All.Length;
                while (_reelPos[i] >= len) _reelPos[i] -= len;
            }
        }
    }

    private void StartBraking(int i, double now)
    {
        int targetIdx = Array.IndexOf(Symbol.All, _finalSymbols![i]);
        float remaining = targetIdx - _reelPos[i];
        if (remaining < 0) remaining += Symbol.All.Length;
        if (remaining < MinBrakeTravel) remaining += Symbol.All.Length;

        _braking[i] = true;
        _brakeStartPos[i] = _reelPos[i];
        _brakeDist[i] = remaining;
        _brakeTime[i] = now;
    }

    private void HandleSpaceCheat()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Space) && !_spinning && !_gameOver)
        {
            _rigged = true;
            TrySpin();
        }
    }

    private void HandleClicks()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        Vector2 m = Raylib.GetMousePosition();

        _betFocused = Hit(m, BetFieldRect());

        if (!_spinning && !_gameOver && Hit(m, SpinButtonRect())) TrySpin();
        if (!_spinning && !_gameOver && Hit(m, AllInButtonRect())) AllIn();
        if (_spinning && Hit(m, StopButtonRect()))                ForceStop();
        if (_gameOver && Hit(m, RestartButtonRect()))             Restart();
    }

    // ── Spellogica ────────────────────────────────────────────────────────────

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
        _pendingPayout = _rigged
            ? _machine.SpinRiggedAndCalculate(bet)
            : _machine.SpinAndCalculate(bet);
        _rigged        = false;
        _finalSymbols  = _machine.CurrentSymbols;
        _spinning      = true;
        _spinStart     = Raylib.GetTime();
        _wheelLanded   = [false, false, false];
        _braking       = [false, false, false];

        SetStatus("Draaien...", StatusKind.Neutral);
    }

    private void AllIn()
    {
        _betInput = _balance.ToString();
    }

    private void ForceStop()
    {
        double now = Raylib.GetTime();
        for (int i = 0; i < 3; i++)
            if (!_wheelLanded[i] && !_braking[i]) StartBraking(i, now);

        for (int i = 0; i < 3; i++)
            if (!_wheelLanded[i])
            {
                _reelPos[i] = _brakeStartPos[i] + _brakeDist[i];
                _wheelLanded[i] = true;
            }

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
        _balance     = StartingBalance;
        _betInput    = "10";
        _gameOver    = false;
        _spinning    = false;
        _wheelLanded = [false, false, false];
        _braking     = [false, false, false];

        var init = _machine.CurrentSymbols;
        for (int i = 0; i < 3; i++)
            _reelPos[i] = Array.IndexOf(Symbol.All, init[i]);

        SetStatus("Welkom terug! Druk op DRAAIEN.", StatusKind.Neutral);
    }

    private void SetStatus(string msg, StatusKind kind) { _statusMsg = msg; _statusKind = kind; }

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
        Raylib.DrawText($"Saldo:  {_balance} munten", 50, 94, 22, Color.White);
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
        bool wheelSpinning = _spinning && !_wheelLanded[index];
        Color border = wheelSpinning ? new Color(130, 130, 130, 255) : Color.Gold;

        Raylib.DrawRectangleRec(rect, new Color(10, 10, 25, 255));

        // Scrollende symbolen, bijgesneden tot het wielgebied
        Raylib.BeginScissorMode((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);

        int symCount = Symbol.All.Length;
        float pos    = _reelPos[index];
        int baseIdx  = (int)pos;
        float frac   = pos - baseIdx;                  // 0..1, voortgang naar volgend symbool
        float centerY = rect.Y + rect.Height / 2f;

        // Teken genoeg symbolen om het wiel te vullen (inclusief overloop)
        for (int d = -2; d <= 3; d++)
        {
            int symIdx  = ((baseIdx + d) % symCount + symCount) % symCount;
            Symbol sym  = Symbol.All[symIdx];
            float symCY = centerY + (d - frac) * SlotH; // beweegt omhoog naarmate frac toeneemt
            DrawSymbolAt(sym, rect.X, symCY, rect.Width, wheelSpinning);
        }

        Raylib.EndScissorMode();

        // Winlijn highlight (middelste slot)
        int wy = (int)(rect.Y + rect.Height / 2f - SlotH / 2f);
        Raylib.DrawRectangle((int)rect.X, wy, (int)rect.Width, (int)SlotH, new Color(255, 255, 255, 12));
        Raylib.DrawLine((int)rect.X, wy,             (int)(rect.X + rect.Width), wy,             new Color(255, 215, 0, 120));
        Raylib.DrawLine((int)rect.X, wy + (int)SlotH,(int)(rect.X + rect.Width), wy + (int)SlotH,new Color(255, 215, 0, 120));

        // Fade boven en onder zodat symbolen vloeiend verdwijnen
        int fadeH = 45;
        Raylib.DrawRectangleGradientV(
            (int)rect.X, (int)rect.Y, (int)rect.Width, fadeH,
            new Color(10, 10, 25, 230), new Color(10, 10, 25, 0));
        Raylib.DrawRectangleGradientV(
            (int)rect.X, (int)(rect.Y + rect.Height - fadeH), (int)rect.Width, fadeH,
            new Color(10, 10, 25, 0), new Color(10, 10, 25, 230));

        Raylib.DrawRectangleLinesEx(rect, 3, border);

        string lbl = $"Wiel {index + 1}";
        int lw = Raylib.MeasureText(lbl, 14);
        Raylib.DrawText(lbl, (int)(rect.X + (rect.Width - lw) / 2), (int)rect.Y + 4, 14, Color.DarkGray);
    }

    private void DrawSymbolAt(Symbol sym, float x, float centerY, float width, bool dim)
    {
        Color c = SymbolColor(sym);
        if (dim) c = new Color((byte)(c.R / 2), (byte)(c.G / 2), (byte)(c.B / 2), c.A);

        if (_fontLoaded)
        {
            const float fs = 46f;
            Vector2 size = Raylib.MeasureTextEx(_emojiFont, sym.Glyph, fs, 1f);
            var pos = new Vector2(x + (width - size.X) / 2f, centerY - size.Y / 2f);
            Raylib.DrawTextEx(_emojiFont, sym.Glyph, pos, fs, 1f, c);
        }
        else
        {
            string txt = SymbolFallbackText(sym);
            int fs = 36;
            int tw = Raylib.MeasureText(txt, fs);
            Raylib.DrawText(txt, (int)(x + (width - tw) / 2), (int)(centerY - fs / 2f), fs, c);
        }
    }

    private void DrawPayTable()
    {
        int tx = 50, ty = 345;
        Raylib.DrawText("Uitbetalingen:", tx, ty, 17, Color.Gray);
        ty += 24;

        foreach (var sym in Symbol.All)
        {
            Color c = SymbolColor(sym);
            if (_fontLoaded)
            {
                Raylib.DrawTextEx(_emojiFont, sym.Glyph, new Vector2(tx, ty), 20f, 1f, c);
                Raylib.DrawText($"  3x = x{sym.Multiplier}  |  2x = x1.5", tx + 26, ty + 2, 16, c);
            }
            else
            {
                Raylib.DrawText($"{SymbolFallbackText(sym),-5}  3x = x{sym.Multiplier}  |  2x = x1.5", tx, ty, 16, c);
            }
            ty += 22;
        }
    }

    private void DrawButtons()
    {
        bool canSpin = !_spinning && !_gameOver && _balance >= MinBet;
        DrawButton(SpinButtonRect(),  "DRAAIEN", canSpin,   Color.DarkGreen);
        DrawButton(AllInButtonRect(), "ALL IN",  canSpin,   new Color(160, 30, 30, 255));
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
        Raylib.DrawText(_statusMsg, Math.Max(30, (W - tw) / 2), 550, fs, c);
    }

    private void DrawGameOverOverlay()
    {
        Raylib.DrawRectangle(0, 0, W, H, new Color(0, 0, 0, 170));

        const string title = "GAME  OVER";
        int tw = Raylib.MeasureText(title, 72);
        Raylib.DrawText(title, (W - tw) / 2, H / 2 - 110, 72, Color.Red);

        const string sub = "U heeft geen munten meer.";
        int sw = Raylib.MeasureText(sub, 26);
        Raylib.DrawText(sub, (W - sw) / 2, H / 2 - 20, 26, Color.White);

        DrawButton(RestartButtonRect(), "OPNIEUW SPELEN", true, Color.DarkGreen);
    }

    private void DrawTextBox(Rectangle rect, string text, bool focused)
    {
        Raylib.DrawRectangleRec(rect, new Color(28, 28, 50, 255));
        Raylib.DrawRectangleLinesEx(rect, 2, focused ? Color.Yellow : Color.Gray);
        bool cur = focused && (int)(Raylib.GetTime() * 2) % 2 == 0;
        Raylib.DrawText(text + (cur ? "|" : ""), (int)rect.X + 8, (int)rect.Y + 8, 20, Color.White);
    }

    private void DrawButton(Rectangle rect, string label, bool enabled, Color normalBg)
    {
        Vector2 m  = Raylib.GetMousePosition();
        bool hover = enabled && Hit(m, rect);

        Color bg = !enabled ? new Color(40, 40, 50, 255)
                 : hover    ? Color.Yellow : normalBg;
        Color fg = !enabled ? Color.DarkGray
                 : hover    ? Color.Black  : Color.White;
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

    // Wiel: y=128, hoogte=195 (3 slots × 65 px)
    private static Rectangle WheelRect(int i)
    {
        const float wheelW = 175f, wheelH = 195f, gap = 18f;
        float startX = (W - 3f * wheelW - 2f * gap) / 2f;
        return new Rectangle(startX + i * (wheelW + gap), 128f, wheelW, wheelH);
    }

    private static Rectangle SpinButtonRect()    => new Rectangle(390, 337, 140, 46);
    private static Rectangle AllInButtonRect()   => new Rectangle(540, 337, 110, 46);
    private static Rectangle StopButtonRect()    => new Rectangle(660, 337, 120, 46);
    private static Rectangle RestartButtonRect() => new Rectangle((W - 200) / 2f, H / 2f + 50, 200, 50);
    private static Rectangle BetFieldRect()      => new Rectangle(W - 185f, 89f, 135f, 36f);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Color SymbolColor(Symbol sym) => sym.Color switch
    {
        "red"          => new Color(230, 60,  60,  255),
        "yellow"       => new Color(255, 220, 30,  255),
        "gold3_1"      => new Color(215, 160, 0,   255),
        "yellow1"      => new Color(255, 255, 80,  255),
        "deepskyblue1" => new Color(0,   185, 255, 255),
        _              => Color.White
    };

    private static string SymbolFallbackText(Symbol sym) => sym.Glyph switch
    {
        "🍒" => "KERS",
        "🍋" => "CITR",
        "🔔" => "BEL",
        "⭐" => "STER",
        "💎" => "DIA",
        _    => "???"
    };

    private static bool Hit(Vector2 m, Rectangle r) =>
        Raylib.CheckCollisionPointRec(m, r);
}
