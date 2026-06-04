using System;
using System.Numerics;
using Raylib_cs;

namespace Game_Jam;

public class SlotMachineGame
{
    // ── Pas hier het aantal wielen aan (2 t/m 7) ─────────────────────────────
    private const int SlotCount = 5;
    // ─────────────────────────────────────────────────────────────────────────

    private const int W = 800, H = 600;
    private const int MinBet = 1;
    private const float SlotH = 65f;
    private const float SpinSpeed = 25f;
    private const float BrakeDuration = 0.5f;
    private const float MinBrakeTravel = 1.5f;

    // ── Chips ─────────────────────────────────────────────────────────────────
    private static readonly int[] ChipValues = [1, 5, 10, 20, 50, 100, 500, 1000];
    private static readonly Color[] ChipColors =
    [
        new Color(210, 210, 210, 255),  // 1    – zilver
        new Color(210, 50,  50,  255),  // 5    – rood
        new Color(50,  110, 210, 255),  // 10   – blauw
        new Color(50,  175, 60,  255),  // 20   – groen
        new Color(220, 140, 30,  255),  // 50   – oranje
        new Color(35,  35,  35,  255),  // 100  – zwart
        new Color(150, 50,  200, 255),  // 500  – paars
        new Color(215, 175, 0,   255),  // 1000 – goud
    ];
    private const float ChipRadius = 34f;
    private static float ChipCX(int col) => 415 + col * 92;
    private static float ChipCY(int row) => 370 + row * 82;

    private readonly System.Collections.Generic.List<int> _betChips = new();
    private int CurrentBet => _betChips.Count > 0 ? _betChips.Sum() : 0;

    // ── Spelerstatus ─────────────────────────────────────────────────────────
    private readonly GlobalState _state;
    private int _currentBet;
    private int _pendingPayout;
    private Symbol[]? _finalSymbols;
    private string _statusMsg = "Kies chips en druk op DRAAIEN.";
    private StatusKind _statusKind = StatusKind.Neutral;
    private bool _gameOver;

    // Reel scroll-animatie
    private readonly float[]  _reelPos       = new float[SlotCount];
    private bool[]            _wheelLanded   = new bool[SlotCount];
    private bool[]            _braking       = new bool[SlotCount];
    private readonly float[]  _brakeStartPos = new float[SlotCount];
    private readonly float[]  _brakeDist     = new float[SlotCount];
    private readonly double[] _brakeTime     = new double[SlotCount];

    private bool _spinning;
    private bool _rigged;
    private double _riggedUntil;
    private double _spinStart;
    private double _lastTime;

    private readonly SlotMachine _machine = new(SlotCount);

    private Font _emojiFont;
    private bool _fontLoaded;

    private int   _drawOffsetX;
    private int   _drawOffsetY;
    private float _drawScale = 1f;

    public bool WantsToGoBack { get; private set; }

    private enum StatusKind { Neutral, Win, Lose }

    // ── Constructor / reset ───────────────────────────────────────────────────

    public SlotMachineGame(GlobalState state)
    {
        _state = state;
        TryLoadEmojiFont();
        var init = _machine.CurrentSymbols;
        for (int i = 0; i < SlotCount; i++)
            _reelPos[i] = Array.IndexOf(Symbol.All, init[i]);
        _lastTime = Raylib.GetTime();
    }

    public void Reset()
    {
        _betChips.Clear();
        _gameOver     = false;
        _spinning     = false;
        _rigged       = false;
        _riggedUntil  = 0;
        WantsToGoBack = false;
        _wheelLanded  = new bool[SlotCount];
        _braking      = new bool[SlotCount];
        _lastTime     = Raylib.GetTime();

        var init = _machine.CurrentSymbols;
        for (int i = 0; i < SlotCount; i++)
            _reelPos[i] = Array.IndexOf(Symbol.All, init[i]);

        SetStatus("Kies chips en druk op DRAAIEN.", StatusKind.Neutral);
    }

    public void SetDrawParams(int ox, int oy, float scale)
    {
        _drawOffsetX = ox;
        _drawOffsetY = oy;
        _drawScale   = scale;
    }

    private void TryLoadEmojiFont()
    {
        const string path = @"C:\Windows\Fonts\seguiemj.ttf";
        if (!System.IO.File.Exists(path)) return;
        int[] codepoints = [0x1F352, 0x1F34B, 0x1F514, 0x2B50, 0x1F48E];
        _emojiFont = Raylib.LoadFontEx(path, 64, codepoints, codepoints.Length);
        _fontLoaded = _emojiFont.GlyphCount > 0;
    }

    private Vector2 CanvasMouse()
    {
        Vector2 m = Raylib.GetMousePosition();
        return new Vector2((m.X - _drawOffsetX) / _drawScale,
                           (m.Y - _drawOffsetY) / _drawScale);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update()
    {
        double now = Raylib.GetTime();
        float dt   = (float)(now - _lastTime);
        _lastTime  = now;

        HandleAnimation(now, dt);
        HandleClicks();
        HandleKeyboardShortcuts(now);
    }

    private void HandleAnimation(double now, float dt)
    {
        if (!_spinning) return;

        double elapsed     = now - _spinStart;
        double[] stopTimes = new double[SlotCount];
        for (int i = 0; i < SlotCount; i++)
            stopTimes[i] = 1.4 + i * 0.5;

        for (int i = 0; i < SlotCount; i++)
        {
            if (_wheelLanded[i]) continue;

            if (_braking[i])
            {
                // Cubic ease-out: snel starten, langzaam stoppen op exact doelsymbool
                float t     = Math.Min((float)((now - _brakeTime[i]) / BrakeDuration), 1f);
                float eased = 1f - (float)Math.Pow(1.0 - t, 3);
                _reelPos[i] = _brakeStartPos[i] + _brakeDist[i] * eased;

                if (t >= 1f)
                {
                    _wheelLanded[i] = true;
                    if (_wheelLanded.All(l => l)) { _spinning = false; FinishSpin(); }
                }
            }
            else if (elapsed >= stopTimes[i])
            {
                StartBraking(i, now);
            }
            else
            {
                float speed  = elapsed < 0.4 ? (float)(elapsed / 0.4 * SpinSpeed) : SpinSpeed;
                _reelPos[i] += speed * dt;
                float len    = Symbol.All.Length;
                while (_reelPos[i] >= len) _reelPos[i] -= len;
            }
        }
    }

    private void StartBraking(int i, double now)
    {
        int targetIdx    = Array.IndexOf(Symbol.All, _finalSymbols![i]);
        float remaining  = targetIdx - _reelPos[i];
        if (remaining < 0) remaining += Symbol.All.Length;
        if (remaining < MinBrakeTravel) remaining += Symbol.All.Length;

        _braking[i]       = true;
        _brakeStartPos[i] = _reelPos[i];
        _brakeDist[i]     = remaining;
        _brakeTime[i]     = now;
    }

    private void HandleKeyboardShortcuts(double now)
    {
        if (!_spinning && !_gameOver)
        {
            // Spatie: activeer cheat-modus voor 2 seconden
            if (Raylib.IsKeyPressed(KeyboardKey.Space))
            {
                _rigged      = true;
                _riggedUntil = now + 2.0;
            }
            if (_riggedUntil > 0 && now > _riggedUntil)
            {
                _rigged      = false;
                _riggedUntil = 0;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) && CurrentBet >= MinBet)
                TrySpin();
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                WantsToGoBack = true;
        }
    }

    private void HandleClicks()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        Vector2 m = CanvasMouse();

        if (_spinning  && Hit(m, StopButtonRect()))    { ForceStop();         return; }
        if (_gameOver  && Hit(m, RestartButtonRect())) { Restart();           return; }
        if (!_spinning && Hit(m, MenuButtonRect()))    { WantsToGoBack = true; return; }

        if (!_spinning && !_gameOver)
        {
            if (Hit(m, SpinButtonRect()) && CurrentBet >= MinBet)  { TrySpin();  return; }
            if (Hit(m, AllInButtonRect()))                          { AllIn();    return; }
            if (Hit(m, ClearButtonRect()) && _betChips.Count > 0)  { _betChips.Clear(); return; }

            // Klik op de toren verwijdert de bovenste chip
            if (Hit(m, TowerRect()) && _betChips.Count > 0)
            {
                _betChips.RemoveAt(_betChips.Count - 1);
                return;
            }

            // Chip-knoppen
            for (int i = 0; i < ChipValues.Length; i++)
            {
                int value = ChipValues[i];
                if (_state.Balance <= value) continue;  // verborgen: saldo moet groter zijn dan chipwaarde
                float cx = ChipCX(i % 4), cy = ChipCY(i / 4);
                if (!Raylib.CheckCollisionPointCircle(m, new Vector2(cx, cy), ChipRadius)) continue;
                if (_state.Balance - CurrentBet >= value)
                    _betChips.Add(value);
                break;
            }
        }
    }

    // ── Spellogica ────────────────────────────────────────────────────────────

    private void TrySpin()
    {
        if (_spinning || _gameOver) return;
        int bet = CurrentBet;
        if (bet < MinBet || bet > _state.Balance)
        {
            SetStatus($"Ongeldige inzet! (min {MinBet}, max {_state.Balance})", StatusKind.Lose);
            return;
        }

        _currentBet     = bet;
        _state.Balance -= bet;
        _pendingPayout  = _rigged
            ? _machine.SpinRiggedAndCalculate(bet)
            : _machine.SpinAndCalculate(bet);
        _rigged       = false;
        _riggedUntil  = 0;
        _finalSymbols = _machine.CurrentSymbols;
        _spinning     = true;
        _spinStart    = Raylib.GetTime();
        _wheelLanded  = new bool[SlotCount];
        _braking      = new bool[SlotCount];

        SetStatus("Draaien...", StatusKind.Neutral);
    }

    private void AllIn()
    {
        _betChips.Clear();
        int rem = _state.Balance;
        for (int i = ChipValues.Length - 1; i >= 0; i--)
        {
            int v = ChipValues[i];
            while (rem >= v) { _betChips.Add(v); rem -= v; }
        }
    }

    private void ForceStop()
    {
        double now = Raylib.GetTime();
        for (int i = 0; i < SlotCount; i++)
            if (!_wheelLanded[i] && !_braking[i]) StartBraking(i, now);

        for (int i = 0; i < SlotCount; i++)
            if (!_wheelLanded[i])
            {
                _reelPos[i]     = _brakeStartPos[i] + _brakeDist[i];
                _wheelLanded[i] = true;
            }

        _spinning = false;
        FinishSpin();
    }

    private void FinishSpin()
    {
        _betChips.Clear();
        _state.Balance += _pendingPayout;

        if (_state.Balance <= 0)
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

        int profit  = _pendingPayout - _currentBet;
        string sign = profit >= 0 ? $"+{profit}" : $"{profit}";

        bool jackpot = _finalSymbols!.All(s => s == _finalSymbols[0]);
        string tag;
        if (jackpot)
        {
            int mult = _currentBet > 0 ? _pendingPayout / _currentBet : 0;
            tag = $"JACKPOT!  Alle {SlotCount}x hetzelfde!  (x{mult})";
        }
        else
        {
            int mult = _currentBet > 0 ? _pendingPayout / _currentBet : 0;
            tag = $"Kleine win!  {SlotCount - 1}x hetzelfde.  (x{mult})";
        }

        SetStatus($"{tag}  Uitbetaling: {_pendingPayout} ({sign})", StatusKind.Win);
    }

    private void Restart()
    {
        _state.Balance = GlobalState.StartingBalance;
        _betChips.Clear();
        _gameOver      = false;
        _spinning      = false;
        WantsToGoBack  = false;
        _wheelLanded   = new bool[SlotCount];
        _braking       = new bool[SlotCount];

        var init = _machine.CurrentSymbols;
        for (int i = 0; i < SlotCount; i++)
            _reelPos[i] = Array.IndexOf(Symbol.All, init[i]);

        SetStatus("Welkom terug! Kies chips en druk op DRAAIEN.", StatusKind.Neutral);
    }

    private void SetStatus(string msg, StatusKind kind) { _statusMsg = msg; _statusKind = kind; }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public void Draw()
    {
        DrawTitle();
        DrawScoreBar();
        DrawWheels();
        DrawPayTable();
        DrawBetTower();
        DrawChipButtons();
        DrawButtons();
        DrawStatus();
        DrawHint();
        if (_gameOver) DrawGameOverOverlay();
    }

    private static void DrawTitle()
    {
        const string title = "SLOT  MACHINE";
        int tw = Raylib.MeasureText(title, 52);
        Raylib.DrawText(title, (W - tw) / 2, 16, 52, Color.Gold);
        Raylib.DrawRectangle(40, 78, W - 80, 2, new Color(80, 70, 20, 255));
    }

    private void DrawScoreBar()
    {
        Raylib.DrawText($"Saldo:  {_state.Balance}", 50, 94, 22, Color.White);
    }

    private void DrawWheels()
    {
        for (int i = 0; i < SlotCount; i++)
            DrawWheel(i);
    }

    private void DrawWheel(int index)
    {
        Rectangle rect     = WheelRect(index);
        bool wheelSpinning = _spinning && !_wheelLanded[index];
        Color border       = wheelSpinning ? new Color(130, 130, 130, 255) : Color.Gold;

        Raylib.DrawRectangleRec(rect, new Color(10, 10, 25, 255));
        Raylib.BeginScissorMode((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);

        int symCount  = Symbol.All.Length;
        float pos     = _reelPos[index];
        int baseIdx   = (int)pos;
        float frac    = pos - baseIdx;
        float centerY = rect.Y + rect.Height / 2f;

        for (int d = -2; d <= 3; d++)
        {
            int symIdx  = ((baseIdx + d) % symCount + symCount) % symCount;
            Symbol sym  = Symbol.All[symIdx];
            float symCY = centerY + (d - frac) * SlotH;
            DrawSymbolAt(sym, rect.X, symCY, rect.Width, wheelSpinning);
        }

        Raylib.EndScissorMode();

        int wy = (int)(rect.Y + rect.Height / 2f - SlotH / 2f);
        Raylib.DrawRectangle((int)rect.X, wy, (int)rect.Width, (int)SlotH, new Color(255, 255, 255, 12));
        Raylib.DrawLine((int)rect.X, wy,              (int)(rect.X + rect.Width), wy,              new Color(255, 215, 0, 120));
        Raylib.DrawLine((int)rect.X, wy + (int)SlotH, (int)(rect.X + rect.Width), wy + (int)SlotH, new Color(255, 215, 0, 120));

        int fadeH = 45;
        Raylib.DrawRectangleGradientV((int)rect.X, (int)rect.Y, (int)rect.Width, fadeH,
            new Color(10, 10, 25, 230), new Color(10, 10, 25, 0));
        Raylib.DrawRectangleGradientV((int)rect.X, (int)(rect.Y + rect.Height - fadeH), (int)rect.Width, fadeH,
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
            float fs     = Math.Min(46f, width * 0.45f);
            Vector2 size = Raylib.MeasureTextEx(_emojiFont, sym.Glyph, fs, 1f);
            var pos      = new Vector2(x + (width - size.X) / 2f, centerY - size.Y / 2f);
            Raylib.DrawTextEx(_emojiFont, sym.Glyph, pos, fs, 1f, c);
        }
        else
        {
            int fs     = (int)Math.Min(36f, width * 0.38f);
            string txt = SymbolFallbackText(sym);
            int tw     = Raylib.MeasureText(txt, fs);
            Raylib.DrawText(txt, (int)(x + (width - tw) / 2), (int)(centerY - fs / 2f), fs, c);
        }
    }

    private void DrawPayTable()
    {
        int tx = 30, ty = 345;
        Raylib.DrawText("Uitbetalingen:", tx, ty, 16, Color.Gray);
        ty += 22;

        foreach (var sym in Symbol.All)
        {
            Color c    = SymbolColor(sym);
            int mult   = SlotMachine.JackpotMultiplier(sym, SlotCount);
            int swMult = SlotMachine.SmallWinMultiplier(sym, SlotCount);

            if (_fontLoaded)
            {
                Raylib.DrawTextEx(_emojiFont, sym.Glyph, new Vector2(tx, ty), 18f, 1f, c);
                Raylib.DrawText($"  {SlotCount}x=x{mult} {SlotCount - 1}x=x{swMult}", tx + 24, ty + 2, 14, c);
            }
            else
            {
                Raylib.DrawText($"{SymbolFallbackText(sym),-4} {SlotCount}x=x{mult} {SlotCount-1}x=x{swMult}", tx, ty, 14, c);
            }
            ty += 20;
        }
    }

    // ── Chip-toren ────────────────────────────────────────────────────────────

    private void DrawBetTower()
    {
        const float cx      = 305f;
        const float baseY   = 490f;
        const float rH      = 30f;   // horizontale radius chip-ellips
        const float rV      = 8f;    // verticale radius chip-ellips
        const float step    = 13f;   // pixels tussen chips
        const int   maxVis  = 10;    // maximaal zichtbare chips in toren

        // Achtergrond voor toren-slot
        var bgRect = new Rectangle(cx - rH - 6, 338f, (rH + 6) * 2, baseY - 338 + 28);
        Raylib.DrawRectangleRec(bgRect, new Color(6, 6, 16, 200));
        Raylib.DrawRectangleLinesEx(bgRect, 1, new Color(40, 40, 60, 255));

        // Label boven toren
        Raylib.DrawText("INZET", (int)(cx - Raylib.MeasureText("INZET", 13) / 2), 342, 13, new Color(100, 100, 120, 255));

        int count    = _betChips.Count;
        int startIdx = Math.Max(0, count - maxVis);

        for (int i = startIdx; i < count; i++)
        {
            int chipVal = _betChips[i];
            int cIdx    = Array.IndexOf(ChipValues, chipVal);
            Color col   = cIdx >= 0 ? ChipColors[cIdx] : Color.Gray;

            float y = baseY - (i - startIdx + 1) * step;

            // Schaduw
            Raylib.DrawEllipse((int)cx + 2, (int)y + 3, (int)rH, (int)rV, new Color(0, 0, 0, 70));
            // Donkere onderkant (diepte-effect)
            Raylib.DrawEllipse((int)cx, (int)y + 2, (int)rH, (int)rV,
                new Color((byte)(col.R / 4), (byte)(col.G / 4), (byte)(col.B / 4), (byte)255));
            // Hoofd-chip
            Raylib.DrawEllipse((int)cx, (int)y, (int)rH, (int)rV, col);
            // Lichte highlight bovenaan
            Raylib.DrawEllipse((int)cx, (int)y - 2, (int)(rH - 7), (int)Math.Max(1, rV - 4),
                new Color(
                    (byte)Math.Min(255, col.R + 80),
                    (byte)Math.Min(255, col.G + 80),
                    (byte)Math.Min(255, col.B + 80), (byte)130));
        }

        // Overflow-indicator
        if (count > maxVis)
        {
            string ovf = $"+{count - maxVis}";
            int ow = Raylib.MeasureText(ovf, 11);
            Raylib.DrawText(ovf, (int)cx - ow / 2, (int)(baseY - maxVis * step - 14), 11, Color.Gray);
        }

        // Totaal bedrag onder de toren
        string total    = CurrentBet > 0 ? $"{CurrentBet}" : "-";
        Color totalCol  = CurrentBet > 0 ? Color.Yellow : new Color(55, 55, 55, 255);
        int tw          = Raylib.MeasureText(total, 18);
        Raylib.DrawText(total, (int)cx - tw / 2, (int)baseY + 6, 18, totalCol);

        // Hover-effect op toren (hint dat je kunt klikken om te verwijderen)
        if (!_spinning && _betChips.Count > 0)
        {
            Vector2 m = CanvasMouse();
            if (Hit(m, TowerRect()))
            {
                Raylib.DrawRectangleLinesEx(bgRect, 1, new Color(180, 60, 60, 180));
                int hw = Raylib.MeasureText("↩", 11);
                Raylib.DrawText("↩", (int)cx - hw / 2, (int)baseY + 24, 11, new Color(180, 60, 60, 200));
            }
        }
    }

    // ── Chip-knoppen ──────────────────────────────────────────────────────────

    private void DrawChipButtons()
    {
        for (int i = 0; i < ChipValues.Length; i++)
        {
            int value = ChipValues[i];
            if (_state.Balance <= value) continue;  // verborgen: saldo moet strikt groter zijn

            bool canAdd = !_spinning && !_gameOver && (_state.Balance - CurrentBet) >= value;
            DrawSingleChip(i, canAdd);
        }
    }

    private void DrawSingleChip(int index, bool available)
    {
        int   value  = ChipValues[index];
        Color col    = ChipColors[index];
        float cx     = ChipCX(index % 4);
        float cy     = ChipCY(index / 4);
        const float r = ChipRadius;

        var center = new Vector2(cx, cy);
        Vector2 m  = CanvasMouse();
        bool hover = available && Raylib.CheckCollisionPointCircle(m, center, r);

        Color main  = available ? col : new Color(52, 52, 58, 255);
        Color dark  = ChipScale(main, 0.42f);
        Color light = ChipAdd(main, 65);
        if (hover) main = light;

        // ── 1. Slagschaduw ────────────────────────────────────────────────────
        Raylib.DrawCircleV(new Vector2(cx + 2, cy + 4), r, new Color(0, 0, 0, 95));

        // ── 2. Buitenste stalen rand (metallic rim) ───────────────────────────
        Raylib.DrawCircleV(center, r, dark);

        // ── 3. Hoofd-chip-body ────────────────────────────────────────────────
        Raylib.DrawCircleV(center, r - 2f, main);

        // ── 4. Casino-segmenten: 6 witte vlakken afgewisseld in de buitenrand ─
        Color segCol = available ? new Color(255, 255, 255, 108) : new Color(130, 130, 130, 44);
        for (int s = 0; s < 12; s++)
        {
            if (s % 2 != 0) continue;
            float a0 = s * 30f - 90f;
            Raylib.DrawRing(center, r - 9f, r - 2.5f, a0, a0 + 21f, 5, segCol);
        }

        // ── 5. Donkere scheidingsring tussen buitenrand en binnenvlak ─────────
        Raylib.DrawRing(center, r - 11f, r - 9.5f, 0f, 360f, 24, new Color(0, 0, 0, 65));

        // ── 6. Binnenste chip-vlak (iets donkerder voor diepte) ───────────────
        Raylib.DrawCircleV(center, r - 11f, ChipScale(main, 0.80f));

        // ── 7. Witte decoratieve binnenring ───────────────────────────────────
        Color ringCol = available ? new Color(255, 255, 255, 78) : new Color(110, 110, 110, 35);
        Raylib.DrawRing(center, r - 14f, r - 11f, 0f, 360f, 24, ringCol);

        // ── 8. Center-vlak ────────────────────────────────────────────────────
        Raylib.DrawCircleV(center, r - 14f, main);

        // ── 9. Glans-highlight (lichte boog linksboven) ───────────────────────
        Raylib.DrawRing(center, r - 5f, r - 1f, 210f, 290f, 8, new Color(255, 255, 255, 32));
        Raylib.DrawCircleV(new Vector2(cx - r * 0.25f, cy - r * 0.30f), r * 0.20f,
            new Color(255, 255, 255, 20));

        // ── 10. Waarde-tekst ──────────────────────────────────────────────────
        string label = value >= 1000 ? "1K" : value.ToString();
        int    fs    = value >= 100 ? 14 : 16;
        int    lw    = Raylib.MeasureText(label, fs);
        int    lx    = (int)cx - lw / 2;
        int    ly    = (int)cy - fs / 2;

        bool lightBg  = main.R > 155 && main.G > 155 && main.B > 155;
        Color textCol = available
            ? (lightBg ? new Color(25, 25, 25, 255) : Color.White)
            : new Color(82, 82, 82, 255);

        if (available)
            Raylib.DrawText(label, lx + 1, ly + 1, fs, new Color(0, 0, 0, lightBg ? 70 : 180));
        Raylib.DrawText(label, lx, ly, fs, textCol);
    }

    private static Color ChipScale(Color c, float f) =>
        new Color((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f), c.A);

    private static Color ChipAdd(Color c, int v) =>
        new Color(
            (byte)Math.Min(255, c.R + v),
            (byte)Math.Min(255, c.G + v),
            (byte)Math.Min(255, c.B + v),
            c.A);

    // ── Knoppen / UI ─────────────────────────────────────────────────────────

    private void DrawButtons()
    {
        bool canSpin  = !_spinning && !_gameOver && CurrentBet >= MinBet;
        bool canClear = !_spinning && !_gameOver && _betChips.Count > 0;

        bool canAllIn = !_spinning && !_gameOver && _state.Balance > 0;
        DrawButton(SpinButtonRect(),  "DRAAIEN",  canSpin,   Color.DarkGreen);
        DrawButton(StopButtonRect(),  "STOP",     _spinning, Color.DarkGray);
        DrawButton(AllInButtonRect(), "ALL IN",   canAllIn,  new Color(140, 20, 20, 255));
        DrawButton(ClearButtonRect(), "WISSEN",   canClear,  new Color(110, 50, 15, 255));
        DrawButton(MenuButtonRect(),  "← MENU",   !_spinning, new Color(50, 50, 90, 255));

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
        int fs = 20;
        int tw = Raylib.MeasureText(_statusMsg, fs);
        Raylib.DrawText(_statusMsg, Math.Max(30, (W - tw) / 2), 548, fs, c);
    }

    private static void DrawHint()
    {
        const string hint = "F11: volledig scherm  |  Esc: menu  |  klik toren: verwijder chip";
        int hw = Raylib.MeasureText(hint, 11);
        Raylib.DrawText(hint, W - hw - 6, H - 16, 11, new Color(50, 50, 50, 255));
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

    private void DrawButton(Rectangle rect, string label, bool enabled, Color normalBg)
    {
        Vector2 m  = CanvasMouse();
        bool hover = enabled && Hit(m, rect);

        Color bg     = !enabled ? new Color(40, 40, 50, 255)  : hover ? Color.Yellow : normalBg;
        Color fg     = !enabled ? Color.DarkGray               : hover ? Color.Black  : Color.White;
        Color border = enabled  ? Color.White : new Color(60, 60, 60, 255);

        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 2, border);

        int tw = Raylib.MeasureText(label, 18);
        Raylib.DrawText(label,
            (int)(rect.X + (rect.Width  - tw) / 2),
            (int)(rect.Y + (rect.Height - 18) / 2),
            18, fg);
    }

    // ── Rechthoeken ───────────────────────────────────────────────────────────

    private static Rectangle WheelRect(int i)
    {
        const float maxWheelW = 175f, gap = 12f;
        float totalGap = (SlotCount - 1) * gap;
        float wheelW   = Math.Min(maxWheelW, (W - 80f - totalGap) / SlotCount);
        float totalW   = SlotCount * wheelW + totalGap;
        float startX   = (W - totalW) / 2f;
        return new Rectangle(startX + i * (wheelW + gap), 128f, wheelW, 195f);
    }

    private static Rectangle SpinButtonRect()    => new Rectangle(270f, 510f, 120f, 34f);
    private static Rectangle StopButtonRect()    => new Rectangle(398f, 510f,  90f, 34f);
    private static Rectangle AllInButtonRect()   => new Rectangle(496f, 510f,  90f, 34f);
    private static Rectangle ClearButtonRect()   => new Rectangle(594f, 510f, 100f, 34f);
    private static Rectangle RestartButtonRect() => new Rectangle((W - 200) / 2f, H / 2f + 50, 200, 50);
    private static Rectangle MenuButtonRect()    => new Rectangle(10f, 10f, 90f, 36f);
    private static Rectangle TowerRect()         => new Rectangle(272f, 338f, 66f, 160f);

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
