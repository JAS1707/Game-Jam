using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Game_Jam;

public class GreedGame
{
    // ── Constanten ────────────────────────────────────────────────────────────
    private const int   W = 800, H = 600;
    private const int   StoneCount  = 20;
    private const int   BombCount   = 5;
    private const int   Cols = 5,   Rows = 4;
    private const float StoneW      = 96f;
    private const float StoneH      = 78f;
    private const float StoneGapX   = 12f;
    private const float StoneGapY   = 10f;
    private const float GridStartX  = 136f;   // (800 − 5×96 − 4×12) / 2
    private const float GridStartY  = 54f;
    private const float FlipDur     = 0.30f;
    private const double MultiplierStep = 1.1;

    // ── Chip-systeem ──────────────────────────────────────────────────────────
    private static readonly int[]   ChipValues = [1, 5, 10, 20, 50, 100, 500, 1000];
    private static readonly Color[] ChipColors =
    [
        new Color(210, 210, 210, 255),
        new Color(210, 50,  50,  255),
        new Color(50,  110, 210, 255),
        new Color(50,  175, 60,  255),
        new Color(220, 140, 30,  255),
        new Color(35,  35,  35,  255),
        new Color(150, 50,  200, 255),
        new Color(215, 175, 0,   255),
    ];
    private const float ChipR    = 26f;
    private const float ChipY    = 415f;
    private const float ChipX0   = 196f;
    private const float ChipStep = 52f;

    private readonly List<int> _betChips = new();
    private int CurrentBet => _betChips.Count > 0 ? _betChips.Sum() : 0;

    // ── Spelstate ─────────────────────────────────────────────────────────────
    private enum GState { Betting, Playing, Result }
    private readonly GlobalState _state;
    private GState _gState = GState.Betting;

    private int   _bet;
    private float _winnings;
    private bool  _hitBomb;
    private int   _safeRevealed;
    private bool  _cashedOut;

    private readonly bool[] _isBomb   = new bool[StoneCount];
    private readonly bool[] _revealed = new bool[StoneCount];

    // ── Flip-animatie ─────────────────────────────────────────────────────────
    private readonly float[] _flipT    = new float[StoneCount];
    private readonly bool[]  _flipping = new bool[StoneCount];

    // ── Multiplier pulse ──────────────────────────────────────────────────────
    private float _multPulse;   // 0..1, decays every frame

    // ── Result overlay ────────────────────────────────────────────────────────
    private float _overlayAlpha;

    // ── Particles (bom-explosie) ──────────────────────────────────────────────
    private struct Particle
    {
        public Vector2 Pos, Vel;
        public float   Life, MaxLife, Radius;
        public Color   Col;
    }
    private readonly List<Particle> _particles = new();

    // ── Sparkles (munt-glitter) ───────────────────────────────────────────────
    private struct Sparkle
    {
        public Vector2 Pos;
        public float   Life, MaxLife, Size, AngVel, Angle;
    }
    private readonly List<Sparkle> _sparkles = new();

    // ── Statuszin ─────────────────────────────────────────────────────────────
    private string     _statusMsg  = "Zet chips in en klik SPELEN.";
    private StatusKind _statusKind = StatusKind.Neutral;
    private enum StatusKind { Neutral, Win, Lose }

    // ── Schaal ────────────────────────────────────────────────────────────────
    private int    _drawOffsetX;
    private int    _drawOffsetY;
    private float  _drawScale = 1f;
    private double _lastTime;

    public bool WantsToGoBack { get; private set; }

    // ── Vaste achtergrondsterren (eenmalig berekend) ──────────────────────────
    private static readonly (int x, int y, byte a)[] BgStars = BuildBgStars();

    private static (int x, int y, byte a)[] BuildBgStars()
    {
        var rng  = new Random(1337);
        var list = new (int, int, byte)[70];
        for (int i = 0; i < 70; i++)
            list[i] = (rng.Next(W), rng.Next(H - 80), (byte)rng.Next(18, 65));
        return list;
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public GreedGame(GlobalState state)
    {
        _state    = state;
        _lastTime = Raylib.GetTime();
    }

    public void Reset()
    {
        if (_state.Balance <= 0) _state.Balance = GlobalState.StartingBalance;
        _gState       = GState.Betting;
        _betChips.Clear();
        WantsToGoBack = false;
        _overlayAlpha = 0f;
        _multPulse    = 0f;
        _cashedOut    = false;
        _particles.Clear();
        _sparkles.Clear();
        Array.Clear(_flipT,    0, StoneCount);
        Array.Clear(_flipping, 0, StoneCount);
        Array.Clear(_isBomb,   0, StoneCount);
        Array.Clear(_revealed, 0, StoneCount);
        _lastTime = Raylib.GetTime();
        SetStatus("Zet chips in en klik SPELEN.", StatusKind.Neutral);
    }

    public void SetDrawParams(int ox, int oy, float scale)
    {
        _drawOffsetX = ox;
        _drawOffsetY = oy;
        _drawScale   = scale;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update()
    {
        double now = Raylib.GetTime();
        float  dt  = (float)(now - _lastTime);
        _lastTime  = now;

        UpdateAnimations(dt);
        HandleInput();
    }

    private void UpdateAnimations(float dt)
    {
        // Flip-animaties
        for (int i = 0; i < StoneCount; i++)
        {
            if (!_flipping[i]) continue;
            _flipT[i] = Math.Min(1f, _flipT[i] + dt / FlipDur);
            if (_flipT[i] >= 1f) _flipping[i] = false;
        }

        // Multiplier-puls daalt
        if (_multPulse > 0f)
            _multPulse = Math.Max(0f, _multPulse - dt * 3.5f);

        // Result-overlay fade in
        if (_gState == GState.Result)
            _overlayAlpha = Math.Min(1f, _overlayAlpha + dt * 2.8f);

        // Bomparticles: zwaartekracht + demping
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p   = _particles[i];
            p.Life -= dt;
            p.Vel   = new Vector2(p.Vel.X * 0.91f, p.Vel.Y * 0.91f + 130f * dt);
            p.Pos  += p.Vel * dt;
            _particles[i] = p;
            if (p.Life <= 0) _particles.RemoveAt(i);
        }

        // Munten-sparkles: roteren + slinken
        for (int i = _sparkles.Count - 1; i >= 0; i--)
        {
            var s  = _sparkles[i];
            s.Life -= dt;
            s.Angle += s.AngVel * dt;
            _sparkles[i] = s;
            if (s.Life <= 0) _sparkles.RemoveAt(i);
        }
    }

    private void HandleInput()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { WantsToGoBack = true; return; }
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

        Vector2 m = CanvasMouse();
        if (Hit(m, MenuButtonRect())) { WantsToGoBack = true; return; }

        switch (_gState)
        {
            case GState.Betting: HandleBettingClick(m); break;
            case GState.Playing: HandlePlayingClick(m); break;
            case GState.Result:
                if (Hit(m, NewGameButtonRect())) StartNewRound();
                break;
        }
    }

    private void HandleBettingClick(Vector2 m)
    {
        // Chip knoppen
        for (int i = 0; i < ChipValues.Length; i++)
        {
            int v = ChipValues[i];
            if (_state.Balance <= v) continue;
            float cx = ChipX0 + i * ChipStep;
            if (!Raylib.CheckCollisionPointCircle(m, new Vector2(cx, ChipY), ChipR)) continue;
            if (_state.Balance - CurrentBet >= v) _betChips.Add(v);
            return;
        }

        if (Hit(m, AllInButtonRect()))                         { AllIn();           return; }
        if (Hit(m, ClearButtonRect()) && _betChips.Count > 0) { _betChips.Clear(); return; }
        if (Hit(m, TowerRect())       && _betChips.Count > 0)
        {
            _betChips.RemoveAt(_betChips.Count - 1);
            return;
        }
        if (Hit(m, PlayButtonRect()) && CurrentBet >= 1) StartGame();
    }

    private void HandlePlayingClick(Vector2 m)
    {
        if (Hit(m, CashOutButtonRect())) { CashOut(); return; }

        for (int i = 0; i < StoneCount; i++)
        {
            if (_revealed[i] || _flipping[i]) continue;
            if (!Hit(m, StoneRect(i))) continue;
            RevealStone(i);
            return;
        }
    }

    // ── Spellogica ────────────────────────────────────────────────────────────

    private void StartGame()
    {
        _bet            = CurrentBet;
        _state.Balance -= _bet;
        _betChips.Clear();

        Array.Clear(_isBomb,   0, StoneCount);
        Array.Clear(_revealed, 0, StoneCount);
        Array.Clear(_flipT,    0, StoneCount);
        Array.Clear(_flipping, 0, StoneCount);
        _particles.Clear();
        _sparkles.Clear();

        // Verdeel bommen willekeurig
        var rng    = Random.Shared;
        int placed = 0;
        while (placed < BombCount)
        {
            int idx = rng.Next(StoneCount);
            if (!_isBomb[idx]) { _isBomb[idx] = true; placed++; }
        }

        _winnings     = _bet;
        _safeRevealed = 0;
        _hitBomb      = false;
        _cashedOut    = false;
        _gState       = GState.Playing;
        _overlayAlpha = 0f;

        SetStatus($"Klik een steen!  |  Inzet: {_bet}  |  {BombCount} bommen verstopt", StatusKind.Neutral);
    }

    private void RevealStone(int i)
    {
        _revealed[i] = true;
        _flipping[i] = true;
        _flipT[i]    = 0f;

        if (_isBomb[i])
        {
            _hitBomb = true;
            SpawnBombParticles(i);

            // Onthul alle andere bommen ook
            for (int j = 0; j < StoneCount; j++)
                if (_isBomb[j] && j != i && !_revealed[j])
                {
                    _revealed[j] = true;
                    _flipping[j] = true;
                    _flipT[j]    = 0.05f * (j - i); // licht gespreid
                }

            SetStatus($"BOM!  Je verliest je inzet van {_bet} munten.", StatusKind.Lose);
            _gState = GState.Result;
        }
        else
        {
            _safeRevealed++;
            _winnings  = (float)Math.Ceiling(_winnings * MultiplierStep);
            _multPulse = 1f;
            SpawnCoinSparkles(i);

            int safeTotal = StoneCount - BombCount;
            if (_safeRevealed >= safeTotal)
            {
                _state.Balance += (int)_winnings;
                SetStatus($"Alle {safeTotal} veilige stenen gevonden!  +{(int)_winnings} munten!", StatusKind.Win);
                _gState = GState.Result;
            }
            else
            {
                double mult = Math.Pow(MultiplierStep, _safeRevealed);
                SetStatus(
                    $"Veilig!  x{mult:F2}  →  {(int)_winnings} munten  |  " +
                    $"{safeTotal - _safeRevealed} veilige stenen over",
                    StatusKind.Neutral);
            }
        }
    }

    private void CashOut()
    {
        _state.Balance += (int)_winnings;
        _cashedOut      = true;
        int profit      = (int)_winnings - _bet;
        SetStatus($"Uitbetaald!  +{(int)_winnings} munten  (winst: {profit:+#;-#;0})", StatusKind.Win);
        // Toon de rest van de stenen
        for (int i = 0; i < StoneCount; i++)
            if (!_revealed[i]) { _revealed[i] = true; _flipping[i] = true; _flipT[i] = 0f; }
        _gState = GState.Result;
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

    private void StartNewRound()
    {
        if (_state.Balance <= 0) _state.Balance = GlobalState.StartingBalance;
        _gState       = GState.Betting;
        _betChips.Clear();
        _overlayAlpha = 0f;
        _particles.Clear();
        _sparkles.Clear();
        Array.Clear(_flipT,    0, StoneCount);
        Array.Clear(_flipping, 0, StoneCount);
        Array.Clear(_isBomb,   0, StoneCount);
        Array.Clear(_revealed, 0, StoneCount);
        SetStatus("Zet chips in en klik SPELEN.", StatusKind.Neutral);
    }

    // ── Particles ─────────────────────────────────────────────────────────────

    private void SpawnBombParticles(int i)
    {
        var   rng  = Random.Shared;
        var   rect = StoneRect(i);
        float cx   = rect.X + rect.Width  / 2f;
        float cy   = rect.Y + rect.Height / 2f;

        for (int p = 0; p < 52; p++)
        {
            float angle = rng.NextSingle() * MathF.PI * 2f;
            float speed = rng.NextSingle() * 260f + 70f;
            int   pick  = p % 3;
            Color col   = pick == 0 ? new Color(255, 60,  20,  255)
                        : pick == 1 ? new Color(255, 180, 0,   255)
                                    : new Color(255, 255, 100, 255);
            _particles.Add(new Particle
            {
                Pos     = new Vector2(cx, cy),
                Vel     = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed - 120f),
                Life    = rng.NextSingle() * 0.65f + 0.4f,
                MaxLife = 1.05f,
                Radius  = rng.NextSingle() * 5f + 2.5f,
                Col     = col,
            });
        }
    }

    private void SpawnCoinSparkles(int i)
    {
        var   rng  = Random.Shared;
        var   rect = StoneRect(i);
        float cx   = rect.X + rect.Width  / 2f;
        float cy   = rect.Y + rect.Height / 2f;

        for (int s = 0; s < 16; s++)
        {
            float angle = rng.NextSingle() * MathF.PI * 2f;
            float dist  = rng.NextSingle() * 36f + 10f;
            _sparkles.Add(new Sparkle
            {
                Pos     = new Vector2(cx + MathF.Cos(angle) * dist, cy + MathF.Sin(angle) * dist),
                Life    = rng.NextSingle() * 0.45f + 0.25f,
                MaxLife = 0.7f,
                Size    = rng.NextSingle() * 7f + 3f,
                Angle   = rng.NextSingle() * 360f,
                AngVel  = (rng.NextSingle() - 0.5f) * 450f,
            });
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public void Draw()
    {
        DrawBackground();
        DrawHeader();
        DrawGrid();
        DrawParticles();
        DrawSparkles();
        DrawBottomArea();
        DrawStatus();
        if (_gState == GState.Result) DrawResultOverlay();
    }

    private static void DrawBackground()
    {
        Raylib.DrawRectangle(0, 0, W, H, new Color(12, 16, 30, 255));
        foreach (var (sx, sy, a) in BgStars)
            Raylib.DrawCircleV(new Vector2(sx, sy), 1f, new Color(255, 255, 255, (int)a));
    }

    private void DrawHeader()
    {
        // Donkere achtergrondstrook
        Raylib.DrawRectangle(0, 0, W, AppTheme.HeaderH, AppTheme.BgHeader);
        Raylib.DrawRectangleGradientV(0, 0, W, AppTheme.HeaderH, new Color(80, 60, 0, 22), new Color(0, 0, 0, 0));
        Raylib.DrawRectangle(0, AppTheme.HeaderH, W, 2, AppTheme.Separator);

        // Spelnaam — links
        const string name = "GREED";
        Raylib.DrawText(name, 14, (AppTheme.HeaderH - 26) / 2, 26, AppTheme.AccentGold);

        // Saldo — midden
        string balTxt = $"Saldo:  {_state.Balance}  munten";
        int bw = Raylib.MeasureText(balTxt, 18);
        Raylib.DrawText(balTxt, (W - bw) / 2, (AppTheme.HeaderH - 18) / 2, 18, AppTheme.TextPrimary);

        // Terugknop — rechts (alleen in bettingmodus)
        DrawButton(MenuButtonRect(), "← TERUG", _gState == GState.Betting, AppTheme.BtnBack);
    }

    private void DrawGrid()
    {
        for (int i = 0; i < StoneCount; i++) DrawStone(i);
    }

    private void DrawStone(int i)
    {
        var   rect     = StoneRect(i);
        float cx       = rect.X + rect.Width  / 2f;
        float cy       = rect.Y + rect.Height / 2f;
        bool  revealed = _revealed[i];
        bool  flipping = _flipping[i];
        float t        = _flipT[i];

        // scaleX: 1→0 in eerste helft, 0→1 in tweede helft
        float scaleX;
        bool  showContent;
        if (!revealed && !flipping)
        {
            scaleX = 1f; showContent = false;
        }
        else if (flipping && t < 0.5f)
        {
            scaleX = 1f - t * 2f; showContent = false;
        }
        else if (flipping)
        {
            scaleX = (t - 0.5f) * 2f; showContent = true;
        }
        else
        {
            scaleX = 1f; showContent = true;
        }

        float drawW = rect.Width * scaleX;
        if (drawW < 2f) return;

        var dr = new Rectangle(cx - drawW / 2f, rect.Y, drawW, rect.Height);

        if (!showContent)
            DrawStoneFace(dr, cx, cy);
        else if (_isBomb[i])
            DrawBombFace(dr, cx, cy);
        else
            DrawCoinFace(dr, cx, cy);

        // Hover-gloed op klikbare stenen
        if (_gState == GState.Playing && !revealed && !flipping)
        {
            Vector2 m = CanvasMouse();
            if (Hit(m, rect))
            {
                var gr = new Rectangle(rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6);
                Raylib.DrawRectangleRounded(gr, 0.22f, 6, new Color(255, 220, 0, 28));
                Raylib.DrawRectangleRoundedLinesEx(gr, 0.22f, 6, 2.5f, new Color(255, 220, 0, 200));
            }
        }
    }

    private static void DrawStoneFace(Rectangle r, float cx, float cy)
    {
        // Basis steen
        Raylib.DrawRectangleRounded(r, 0.22f, 6, new Color(50, 55, 75, 255));
        // Top-glans
        var top = new Rectangle(r.X, r.Y, r.Width, r.Height * 0.42f);
        Raylib.DrawRectangleRounded(top, 0.22f, 6, new Color(70, 76, 100, 255));
        // Rand
        Raylib.DrawRectangleRoundedLinesEx(r, 0.22f, 6, 1.5f, new Color(95, 102, 130, 255));
        // Vraagteken
        const int fs = 24;
        int qw = Raylib.MeasureText("?", fs);
        Raylib.DrawText("?", (int)(cx - qw / 2) + 1, (int)(cy - fs / 2) + 1, fs, new Color(0, 0, 0, 60));
        Raylib.DrawText("?", (int)(cx - qw / 2),     (int)(cy - fs / 2),     fs, new Color(115, 122, 158, 210));
    }

    private static void DrawBombFace(Rectangle r, float cx, float cy)
    {
        // Achtergrond
        Raylib.DrawRectangleRounded(r, 0.22f, 6, new Color(50, 10, 10, 255));
        // Gloedrand
        Raylib.DrawRectangleRoundedLinesEx(r, 0.22f, 6, 2.5f, new Color(220, 40, 40, 255));
        // Binnenste gloed
        Raylib.DrawRectangleRounded(
            new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8),
            0.20f, 6, new Color(120, 20, 20, 40));

        // Bomb body (schaduw + hoofdvlak)
        Raylib.DrawCircleV(new Vector2(cx + 1, cy + 5), 19f, new Color(0, 0, 0, 180));
        Raylib.DrawCircleV(new Vector2(cx,     cy + 4), 19f, new Color(18, 18, 18, 255));
        Raylib.DrawCircleV(new Vector2(cx,     cy + 4), 17f, new Color(38, 38, 38, 255));
        // Lontje
        Raylib.DrawLineEx(new Vector2(cx,      cy - 14), new Vector2(cx + 9,  cy - 25), 2.5f, new Color(160, 115, 50, 255));
        Raylib.DrawLineEx(new Vector2(cx + 9,  cy - 25), new Vector2(cx + 5,  cy - 31), 2f,   new Color(160, 115, 50, 255));
        // Vonk
        Raylib.DrawCircleV(new Vector2(cx + 5,  cy - 32), 4f, new Color(255, 195, 40, 255));
        Raylib.DrawCircleV(new Vector2(cx + 5,  cy - 32), 2f, new Color(255, 255, 180, 255));
        // Glans op bom
        Raylib.DrawCircleV(new Vector2(cx - 6, cy - 2),  5f, new Color(255, 255, 255, 45));
    }

    private static void DrawCoinFace(Rectangle r, float cx, float cy)
    {
        // Achtergrond
        Raylib.DrawRectangleRounded(r, 0.22f, 6, new Color(20, 40, 14, 255));
        // Goudkleurige rand
        Raylib.DrawRectangleRoundedLinesEx(r, 0.22f, 6, 2.5f, new Color(215, 175, 0, 255));
        // Subtiele binnengloed
        Raylib.DrawRectangleRounded(
            new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8),
            0.20f, 6, new Color(80, 120, 20, 25));

        // Munt (schaduw + lagen)
        Raylib.DrawCircleV(new Vector2(cx + 1, cy + 2), 22f, new Color(90, 65, 0, 220));
        Raylib.DrawCircleV(new Vector2(cx,     cy),     22f, new Color(215, 175, 0, 255));
        Raylib.DrawCircleV(new Vector2(cx,     cy),     19f, new Color(245, 210, 45, 255));
        // Sierring
        Raylib.DrawRing(new Vector2(cx, cy), 13.5f, 16f, 0f, 360f, 24, new Color(175, 135, 0, 110));
        // Dollar-teken
        const int fs = 18;
        int sw = Raylib.MeasureText("$", fs);
        Raylib.DrawText("$", (int)(cx - sw / 2) + 1, (int)(cy - fs / 2) + 1, fs, new Color(100, 65, 0, 160));
        Raylib.DrawText("$", (int)(cx - sw / 2),     (int)(cy - fs / 2),     fs, new Color(155, 95, 0, 255));
        // Glans
        Raylib.DrawCircleV(new Vector2(cx - 7, cy - 7), 5.5f, new Color(255, 255, 200, 55));
    }

    // ── Particles & Sparkles tekenen ─────────────────────────────────────────

    private void DrawParticles()
    {
        foreach (var p in _particles)
        {
            float a     = Math.Max(0f, p.Life / p.MaxLife);
            byte  alpha = (byte)(a * 255);
            Raylib.DrawCircleV(p.Pos, Math.Max(0.5f, p.Radius * a),
                new Color(p.Col.R, p.Col.G, p.Col.B, alpha));
        }
    }

    private void DrawSparkles()
    {
        foreach (var s in _sparkles)
        {
            float a    = Math.Max(0f, s.Life / s.MaxLife);
            byte  alph = (byte)(a * 255);
            var   c    = new Color((byte)255, (byte)215, (byte)0, alph);
            float sz   = s.Size * a;
            var   pos  = s.Pos;
            // Ster: 4 lijnen
            Raylib.DrawLineEx(new Vector2(pos.X - sz,      pos.Y),         new Vector2(pos.X + sz,      pos.Y),         1.5f, c);
            Raylib.DrawLineEx(new Vector2(pos.X,           pos.Y - sz),    new Vector2(pos.X,           pos.Y + sz),    1.5f, c);
            float d = sz * 0.65f;
            Raylib.DrawLineEx(new Vector2(pos.X - d, pos.Y - d), new Vector2(pos.X + d, pos.Y + d), 1f, c);
            Raylib.DrawLineEx(new Vector2(pos.X + d, pos.Y - d), new Vector2(pos.X - d, pos.Y + d), 1f, c);
        }
    }

    // ── Onderste paneel ───────────────────────────────────────────────────────

    private void DrawBottomArea()
    {
        Raylib.DrawRectangle(0, 406, W, 1, new Color(255, 255, 255, 22));

        switch (_gState)
        {
            case GState.Betting: DrawBettingPanel(); break;
            case GState.Playing: DrawPlayingPanel(); break;
        }
    }

    private void DrawBettingPanel()
    {
        DrawChipButtons();
        DrawBetTower();
        DrawButton(PlayButtonRect(),  "SPELEN",  CurrentBet >= 1,     Color.DarkGreen);
        DrawButton(AllInButtonRect(), "ALL IN",  _state.Balance > 0,  new Color(140, 20, 20, 255));
        DrawButton(ClearButtonRect(), "WISSEN",  _betChips.Count > 0, new Color(110, 50, 15, 255));

        string betStr = CurrentBet > 0 ? $"Inzet: {CurrentBet} munten" : "Selecteer je inzet";
        Color  betCol = CurrentBet > 0 ? AppTheme.AccentWarn : AppTheme.TextMuted;
        int btw = Raylib.MeasureText(betStr, 16);
        Raylib.DrawText(betStr, (W - btw) / 2, 502, 16, betCol);

        const string hint = "Elk veilig steen × 1,1 afgerond naar boven  |  5 bommen verborgen";
        int hw = Raylib.MeasureText(hint, 14);
        Raylib.DrawText(hint, Math.Max(6, (W - hw) / 2), 522, 14, AppTheme.TextMuted);
    }

    private void DrawPlayingPanel()
    {
        // Multiplier (links)
        double mult  = Math.Pow(MultiplierStep, _safeRevealed);
        float  pulse = 1f + _multPulse * 0.32f;
        int    mfs   = (int)(24 * pulse);
        string multStr = $"x{mult:F3}";
        Raylib.DrawText("VERMENIGV.", 28, 414, 11, new Color(120, 120, 130, 255));
        Raylib.DrawText(multStr, 28, 427, mfs, new Color(255, 215, 0, 255));

        // Huidig bedrag (midden)
        string wStr = $"{(int)_winnings}";
        int wfs = 34;
        int ww  = Raylib.MeasureText(wStr, wfs);
        Raylib.DrawText("HUIDIG", (W - ww) / 2 - 8, 414, 11, new Color(120, 120, 130, 255));
        Raylib.DrawText(wStr, (W - ww) / 2, 425, wfs, Color.White);
        Raylib.DrawText("munten", (W - ww) / 2 + ww + 5, 440, 12, new Color(155, 155, 155, 255));

        // Veilig gevonden (rechts)
        int    safeTotal = StoneCount - BombCount;
        string safeStr   = $"{_safeRevealed}/{safeTotal}";
        int    sw        = Raylib.MeasureText(safeStr, 22);
        Raylib.DrawText("VEILIG", W - 110, 414, 11, new Color(120, 120, 130, 255));
        Raylib.DrawText(safeStr, W - 110, 428, 22, new Color(90, 210, 90, 255));

        // Uitbetalingsknop
        DrawButton(CashOutButtonRect(), $"UITBETALEN  +{(int)_winnings}", true, new Color(20, 120, 45, 255));

        string infoStr = $"Inzet: {_bet} munten  |  {BombCount} bommen verborgen";
        Raylib.DrawText(infoStr, 28, 506, 14, AppTheme.TextMuted);
    }

    // ── Chip-weergave ─────────────────────────────────────────────────────────

    private void DrawChipButtons()
    {
        for (int i = 0; i < ChipValues.Length; i++)
        {
            int v = ChipValues[i];
            if (_state.Balance <= v) continue;
            bool canAdd = (_state.Balance - CurrentBet) >= v;
            DrawSingleChip(i, canAdd);
        }
    }

    private void DrawSingleChip(int index, bool available)
    {
        int   value  = ChipValues[index];
        Color col    = ChipColors[index];
        float cx     = ChipX0 + index * ChipStep;
        const float r = ChipR;

        var    center = new Vector2(cx, ChipY);
        Vector2 m     = CanvasMouse();
        bool hover    = available && Raylib.CheckCollisionPointCircle(m, center, r);

        Color main  = available ? col : new Color(52, 52, 58, 255);
        Color dark  = ColorScale(main, 0.42f);
        Color light = ColorAdd(main, 55);
        if (hover) main = light;

        Raylib.DrawCircleV(new Vector2(cx + 1, ChipY + 3), r, new Color(0, 0, 0, 90));
        Raylib.DrawCircleV(center, r, dark);
        Raylib.DrawCircleV(center, r - 2f, main);

        Color seg = available ? new Color(255, 255, 255, 100) : new Color(120, 120, 120, 40);
        for (int s = 0; s < 12; s += 2)
        {
            float a0 = s * 30f - 90f;
            Raylib.DrawRing(center, r - 7f, r - 2f, a0, a0 + 21f, 4, seg);
        }
        Raylib.DrawRing(center, r - 9f,  r - 7.5f, 0f, 360f, 20, new Color(0, 0, 0, 55));
        Raylib.DrawCircleV(center, r - 9f, ColorScale(main, 0.82f));
        Raylib.DrawRing(center, r - 11f, r - 9f, 0f, 360f, 20,
            available ? new Color(255, 255, 255, 65) : new Color(100, 100, 100, 30));
        Raylib.DrawCircleV(center, r - 11f, main);

        string label = value >= 1000 ? "1K" : value.ToString();
        int    fs    = value >= 100 ? 12 : 13;
        int    lw    = Raylib.MeasureText(label, fs);
        bool   lb    = main.R > 155 && main.G > 155 && main.B > 155;
        Color  tc    = available ? (lb ? new Color(25, 25, 25, 255) : Color.White)
                                 : new Color(80, 80, 80, 255);
        if (available)
            Raylib.DrawText(label, (int)cx - lw / 2 + 1, (int)ChipY - fs / 2 + 1, fs,
                new Color(0, 0, 0, lb ? 70 : 170));
        Raylib.DrawText(label, (int)cx - lw / 2, (int)ChipY - fs / 2, fs, tc);
    }

    private void DrawBetTower()
    {
        const float cx    = 68f;
        const float baseY = 452f;
        const float rH = 22f, rV = 6f, step = 10f;
        const int   maxVis = 7;

        Raylib.DrawText("INZET", (int)(cx - Raylib.MeasureText("INZET", 14) / 2), 380, 14,
            AppTheme.TextLabel);

        int count    = _betChips.Count;
        int startIdx = Math.Max(0, count - maxVis);

        for (int i = startIdx; i < count; i++)
        {
            int   cv  = _betChips[i];
            int   ci  = Array.IndexOf(ChipValues, cv);
            Color col = ci >= 0 ? ChipColors[ci] : Color.Gray;
            float y   = baseY - (i - startIdx + 1) * step;

            Raylib.DrawEllipse((int)cx + 1, (int)y + 2, (int)rH, (int)rV, new Color(0, 0, 0, 60));
            Raylib.DrawEllipse((int)cx, (int)y + 2, (int)rH, (int)rV,
                new Color((byte)(col.R / 4), (byte)(col.G / 4), (byte)(col.B / 4), (byte)255));
            Raylib.DrawEllipse((int)cx, (int)y, (int)rH, (int)rV, col);
            Raylib.DrawEllipse((int)cx, (int)y - 1, (int)(rH - 5), (int)Math.Max(1, rV - 2.5f),
                new Color(
                    (byte)Math.Min(255, col.R + 70),
                    (byte)Math.Min(255, col.G + 70),
                    (byte)Math.Min(255, col.B + 70), (byte)115));
        }

        string total   = CurrentBet > 0 ? $"{CurrentBet}" : "-";
        Color  totCol  = CurrentBet > 0 ? Color.Yellow : new Color(55, 55, 55, 255);
        int tw = Raylib.MeasureText(total, 15);
        Raylib.DrawText(total, (int)cx - tw / 2, (int)baseY + 5, 15, totCol);

        if (_betChips.Count > 0)
        {
            Vector2 m = CanvasMouse();
            if (Hit(m, TowerRect()))
                Raylib.DrawRectangleLinesEx(new Rectangle(cx - rH - 4, 378, (rH + 4) * 2, 82),
                    1, new Color(200, 60, 60, 150));
        }
    }

    // ── Result overlay ────────────────────────────────────────────────────────

    private void DrawResultOverlay()
    {
        byte bgA = (byte)(_overlayAlpha * 190);
        Raylib.DrawRectangle(0, 0, W, H, new Color(0, 0, 0, (int)bgA));

        float vis = Math.Max(0f, (_overlayAlpha - 0.28f) / 0.72f);
        if (vis <= 0f) return;

        // Titel
        string titleTxt = _hitBomb ? "BOM GERAAKT!" : _cashedOut ? "UITBETAALD!" : "GEWONNEN!";
        Color  titleCol = _hitBomb ? new Color(255, 55, 35, 255) : new Color(255, 215, 0, 255);
        int    tfs      = (int)(56 * EaseOut(vis));
        if (tfs >= 8)
        {
            int tw = Raylib.MeasureText(titleTxt, tfs);
            Raylib.DrawText(titleTxt, (W - tw) / 2, H / 2 - 105, tfs, titleCol);
        }

        if (vis < 0.4f) return;

        // Sub-tekst
        string subTxt = _hitBomb
            ? $"Je verliest je inzet van {_bet} munten."
            : $"Je ontvangt {(int)_winnings} munten!";
        int sw = Raylib.MeasureText(subTxt, 22);
        Raylib.DrawText(subTxt, (W - sw) / 2, H / 2 - 20, 22, Color.White);

        // Winstbedrag
        if (!_hitBomb)
        {
            int profit = (int)_winnings - _bet;
            string profTxt = $"Winst: {profit:+#;-#;0} munten  (x{(double)_winnings / _bet:F2})";
            int pw = Raylib.MeasureText(profTxt, 17);
            Raylib.DrawText(profTxt, (W - pw) / 2, H / 2 + 14, 17, new Color(180, 230, 120, 255));
        }

        // Saldo
        string balTxt = $"Nieuw saldo: {_state.Balance}";
        int bw = Raylib.MeasureText(balTxt, 17);
        Raylib.DrawText(balTxt, (W - bw) / 2, H / 2 + (_hitBomb ? 14 : 38), 17,
            new Color(175, 175, 175, 255));

        DrawButton(NewGameButtonRect(), "NIEUW SPEL", true, Color.DarkGreen);
    }

    // ── Statusbalk ────────────────────────────────────────────────────────────

    private void DrawStatus()
    {
        if (_gState == GState.Result) return; // overlay toont de status
        Color c = _statusKind switch
        {
            StatusKind.Win  => Color.Yellow,
            StatusKind.Lose => Color.Red,
            _               => new Color(185, 185, 185, 255),
        };
        int sw = Raylib.MeasureText(_statusMsg, 14);
        Raylib.DrawText(_statusMsg, Math.Max(8, (W - sw) / 2), 578, 14, c);
    }

    // ── Knop-helper ───────────────────────────────────────────────────────────

    private void DrawButton(Rectangle rect, string label, bool enabled, Color normalBg)
    {
        Vector2 m  = CanvasMouse();
        bool hover = enabled && Hit(m, rect);
        Color bg   = !enabled ? new Color(35, 35, 42, 255) : hover ? Color.Yellow : normalBg;
        Color fg   = !enabled ? Color.DarkGray : hover ? Color.Black : Color.White;
        Color bord = enabled  ? Color.White : new Color(55, 55, 55, 255);

        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 2, bord);
        int tw = Raylib.MeasureText(label, 17);
        Raylib.DrawText(label,
            (int)(rect.X + (rect.Width  - tw) / 2),
            (int)(rect.Y + (rect.Height - 17) / 2),
            17, fg);
    }

    // ── Rechthoeken ───────────────────────────────────────────────────────────

    private static Rectangle StoneRect(int i)
    {
        int   col = i % Cols;
        int   row = i / Cols;
        float x   = GridStartX + col * (StoneW + StoneGapX);
        float y   = GridStartY + row * (StoneH + StoneGapY);
        return new Rectangle(x, y, StoneW, StoneH);
    }

    private static Rectangle MenuButtonRect()    => new Rectangle(W - 106f, 8f, 96f, 32f);
    private static Rectangle PlayButtonRect()    => new Rectangle(310f, 462f, 120f, 36f);
    private static Rectangle AllInButtonRect()   => new Rectangle(438f, 462f,  90f, 36f);
    private static Rectangle ClearButtonRect()   => new Rectangle(536f, 462f, 100f, 36f);
    private static Rectangle CashOutButtonRect() => new Rectangle(300f, 462f, 200f, 36f);
    private static Rectangle NewGameButtonRect() => new Rectangle((W - 200) / 2f, H / 2f + 65f, 200f, 46f);
    private static Rectangle TowerRect()         => new Rectangle(44f, 378f, 50f, 82f);

    // ── Kleur-helpers ─────────────────────────────────────────────────────────

    private void SetStatus(string msg, StatusKind kind) { _statusMsg = msg; _statusKind = kind; }

    private Vector2 CanvasMouse()
    {
        Vector2 m = Raylib.GetMousePosition();
        return new Vector2((m.X - _drawOffsetX) / _drawScale,
                           (m.Y - _drawOffsetY) / _drawScale);
    }

    private static bool  Hit(Vector2 m, Rectangle r)  => Raylib.CheckCollisionPointRec(m, r);
    private static float EaseOut(float t) => 1f - (float)Math.Pow(1.0 - t, 3);

    private static Color ColorScale(Color c, float f) =>
        new Color((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f), c.A);

    private static Color ColorAdd(Color c, int v) =>
        new Color(
            (byte)Math.Min(255, c.R + v),
            (byte)Math.Min(255, c.G + v),
            (byte)Math.Min(255, c.B + v), c.A);
}
