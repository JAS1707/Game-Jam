using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Game_Jam;

/// <summary>
/// Europees roulette (37 vakken, één nul).
/// Inzetmogelijkheden: rechte getallen (35:1), rood/zwart, even/oneven,
/// laag/hoog (1:1), dozijnen en kolommen (2:1).
/// </summary>
public class RouletteGame
{
    // ── Layout ────────────────────────────────────────────────────────────────
    private const int W = 800, H = 600;

    // Wiel (links) — kleiner en gecentreerd in de linkerhelft
    private const float WCX = 155f, WCY = 270f, WR = 120f;

    // Tafel (rechts van het wiel) — schuift iets naar rechts voor ruimte
    private const float TX = 330f, TY = 75f;   // linkerbovenhoek nul-cel
    private const float CW = 33f,  CH = 30f;   // cel breedte / hoogte

    // ── Chip-systeem (identiek aan BlackjackGame) ─────────────────────────────
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
    private const float ChipR    = 20f;
    // Chips arranged in two rows (4 columns x 2 rows) in the lower-left corner
    private const float ChipX0    = 20f;   // left margin for chip grid
    private const float ChipY     = 540f;  // bottom Y position for the base row
    private const float ChipStepX = 70f;   // horizontal spacing between chips
    private const float ChipStepY = 44f;   // vertical spacing between rows

    // ── Inzettypen ────────────────────────────────────────────────────────────
    private enum BetType
    {
        Straight,           // enkel getal  35:1
        Red, Black,         //              1:1
        Even, Odd,          //              1:1
        Low, High,          // 1-18/19-36   1:1
        Dozen1, Dozen2, Dozen3,          // 2:1
        Column1, Column2, Column3,       // 2:1
    }

    private record struct BetZone(BetType Type, int Number, Rectangle Rect);

    // ── Rode nummers (Europese standaard) ─────────────────────────────────────
    private static readonly HashSet<int> Reds = new()
        { 1,3,5,7,9,12,14,16,18,19,21,23,25,27,30,32,34,36 };

    // ── Wiel-volgorde (Europees) ──────────────────────────────────────────────
    private static readonly int[] WheelOrder =
    [
        0,32,15,19,4,21,2,25,17,34,6,27,13,36,11,30,
        8,23,10,5,24,16,33,1,20,14,31,9,22,18,29,7,
        28,12,35,3,26
    ];

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly GlobalState _state;

    // Inzetten: zone-sleutel → (zone, chiplijst)
    private readonly Dictionary<string, (BetZone Zone, List<int> Chips)> _bets = new();
    private int _selectedChip = 1;   // index in ChipValues (default = 5)

    // Chip-toren (weergave geselecteerde chip)
    private readonly List<int> _betChips = new();   // voor toren-weergave
    private int TotalBet => _bets.Values.SelectMany(e => e.Chips).Sum();

    // Spin-animatie
    private enum RState { Betting, Spinning, Result }
    private RState _rState = RState.Betting;
    private double _spinStart;
    private float  _wheelAngle;
    private float  _ballAngle;
    private int    _result  = -1;
    private int    _lastResult = -1;

    // Dealer-timing (resultaat tonen na animatie)
    private double _lastTime;
    private double _lastBallTick;

    // Status
    private string     _statusMsg  = "Kies chips, klik een vakje en druk DRAAIEN.";
    private StatusKind _statusKind = StatusKind.Neutral;
    private enum StatusKind { Neutral, Win, Lose }

    // Game-over
    private bool _gameOver;

    // All-in mode
    private bool _allInMode = false;

    // Schaal (zelfde patroon als BJGame)
    private int   _drawOffsetX;
    private int   _drawOffsetY;
    private float _drawScale = 1f;

    public bool WantsToGoBack { get; private set; }

    // Vooraf berekende zones
    private List<BetZone> _zones = new();

    // ── Constructor / Reset ───────────────────────────────────────────────────

    public RouletteGame(GlobalState state)
    {
        _state    = state;
        _lastTime = 0;
        BuildZones();
    }

    public void Reset()
    {
        if (_state.Balance <= 0) _state.Balance = GlobalState.StartingBalance;
        _bets.Clear();
        _betChips.Clear();
        _rState       = RState.Betting;
        _gameOver     = false;
        _allInMode    = false;
        WantsToGoBack = false;
        _result       = -1;
        _wheelAngle   = 0f;
        _ballAngle    = 0f;
        SetStatus("Kies chips, klik een vakje en druk DRAAIEN.", StatusKind.Neutral);
    }

    public void SetDrawParams(int ox, int oy, float scale)
    {
        _drawOffsetX = ox; _drawOffsetY = oy; _drawScale = scale;
    }

    // ── Zones bouwen ─────────────────────────────────────────────────────────

    private void BuildZones()
    {
        _zones.Clear();

        // Nul (linkerkolom, hoogte = 3 cellen)
        _zones.Add(new BetZone(BetType.Straight, 0, new Rectangle(TX, TY, CW, CH * 3)));

        // Nummers 1-36:  kolom 0 = nrs 1,4,7,…  kolom 1 = 2,5,8,…  kolom 2 = 3,6,9,…
        for (int n = 1; n <= 36; n++)
        {
            int row = (n - 1) / 3;   // rij 0-11
            int col = (n - 1) % 3;   // kolom 0-2
            float x = TX + CW + col * CW;
            float y = TY + row * CH;
            _zones.Add(new BetZone(BetType.Straight, n, new Rectangle(x, y, CW, CH)));
        }

        // Dozijnen (onder de getallen)
        float dY = TY + 12 * CH + 5f;
        _zones.Add(new BetZone(BetType.Dozen1,  0, new Rectangle(TX + CW,          dY, CW, CH)));
        _zones.Add(new BetZone(BetType.Dozen2,  0, new Rectangle(TX + CW * 2,      dY, CW, CH)));
        _zones.Add(new BetZone(BetType.Dozen3,  0, new Rectangle(TX + CW * 3,      dY, CW, CH)));

        // Even-kansen (onderste balk)
        float eY = dY + CH + 4f;
        float ew = CW;
        _zones.Add(new BetZone(BetType.Low,   0, new Rectangle(TX + CW,          eY, ew, CH)));
        _zones.Add(new BetZone(BetType.Even,  0, new Rectangle(TX + CW * 2f,     eY, ew, CH)));
        _zones.Add(new BetZone(BetType.Red,   0, new Rectangle(TX + CW * 3f,     eY, ew, CH)));
        _zones.Add(new BetZone(BetType.Black, 0, new Rectangle(TX + CW * 4f,     eY, ew, CH)));
        _zones.Add(new BetZone(BetType.Odd,   0, new Rectangle(TX + CW * 5f,     eY, ew, CH)));
        _zones.Add(new BetZone(BetType.High,  0, new Rectangle(TX + CW * 6f,     eY, ew, CH)));

        // Kolom-knoppen (rechts van de nummers, één per rij → samengevat als 3 brede cellen rechts)
        float colX = TX + CW * 4f;
        _zones.Add(new BetZone(BetType.Column1, 0, new Rectangle(colX,        dY, CW, CH)));
        _zones.Add(new BetZone(BetType.Column2, 0, new Rectangle(colX + CW,   dY, CW, CH)));
        _zones.Add(new BetZone(BetType.Column3, 0, new Rectangle(colX + CW*2, dY, CW, CH)));
    }

    private static string ZoneKey(BetZone z) =>
        z.Type == BetType.Straight ? $"n{z.Number}" : z.Type.ToString();

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update()
    {
        double now = Raylib.GetTime();
        if (_lastTime == 0) _lastTime = now;
        float dt   = (float)(now - _lastTime);
        _lastTime  = now;

        if (_rState == RState.Spinning) UpdateAnimation(now, dt);

        HandleKeyboard();
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            HandleClick(CanvasMouse());
    }

    private void UpdateAnimation(double now, float dt)
    {
        double elapsed = now - _spinStart;
        const double dur = 4.0;
        float t     = (float)Math.Min(elapsed / dur, 1.0);
        float ease  = 1f - (float)Math.Pow(1.0 - t, 3);

        _wheelAngle += (45f + 20f * (1f - ease)) * dt;
        if (_wheelAngle >= 360f) _wheelAngle -= 360f;

        float ballSpeed = 720f * (1f - ease * 0.90f);
        _ballAngle -= ballSpeed * dt;
        if (_ballAngle < 0f) _ballAngle += 360f;

        // Balletje-tik: snel aan het begin, langzamer naarmate de bal vertraagt
        double tickInterval = 0.025 + 0.18 * Math.Min(1.0, elapsed / dur);
        if (now - _lastBallTick >= tickInterval)
        {
            _lastBallTick = now;
            if (_state.Sounds != null) Raylib.PlaySound(_state.Sounds.BallTick);
        }

        if (t >= 1.0f)
        {
            _rState = RState.Result;
            LandBall();
        }
    }

    private void LandBall()
    {
        int n     = WheelOrder.Length;
        float seg = 360f / n;
        int idx   = (int)((_ballAngle % 360f + 360f) / seg) % n;
        _result   = WheelOrder[idx];
        if (_state.Sounds != null) Raylib.PlaySound(_state.Sounds.BallLand);
        ResolveBets();
    }

    private void HandleKeyboard()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (_allInMode)
            {
                _allInMode = false;
                SetStatus("ALL IN geannuleerd.", StatusKind.Neutral);
            }
            else
            {
                WantsToGoBack = true;
            }
            return;
        }
        if (!_gameOver && _rState == RState.Betting
            && TotalBet >= 1 && Raylib.IsKeyPressed(KeyboardKey.Enter))
            StartSpin();
        if (_rState == RState.Result && Raylib.IsKeyPressed(KeyboardKey.Enter))
            NewRound();
    }

    private void HandleClick(Vector2 m)
    {
        if (_gameOver)
        {
            if (Hit(m, RestartButtonRect())) Restart();
            return;
        }

        if (Hit(m, MenuButtonRect()))  { WantsToGoBack = true; return; }

        if (_rState == RState.Result)
        {
            if (Hit(m, NewRoundButtonRect())) NewRound();
            return;
        }

        if (_rState != RState.Betting) return;

        // If in all-in mode, only accept zone clicks to place the all-in bet
        if (_allInMode)
        {
            foreach (var zone in _zones)
            {
                if (!Hit(m, zone.Rect)) continue;
                PlaceAllInBet(zone);
                _allInMode = false;
                return;
            }
            // Click outside zones: cancel all-in mode
            _allInMode = false;
            SetStatus("ALL IN geannuleerd.", StatusKind.Neutral);
            return;
        }

        if (Hit(m, SpinButtonRect()) && TotalBet >= 1) { StartSpin(); return; }
        if (Hit(m, ClearButtonRect()) && _bets.Count > 0) { _bets.Clear(); _betChips.Clear(); return; }
        if (Hit(m, AllInButtonRect()) && _state.Balance > 0) { DoAllIn(); return; }

        // Chip-selectie (toren)
        if (Hit(m, TowerRect()) && _betChips.Count > 0)
        {
            // klik op toren = verwijder laatste chip-groep
            _betChips.RemoveAt(_betChips.Count - 1);
            // verwijder ook uit bets (simpel: wis alles en herbouw uit toren — niet van toepassing)
            // Toren wordt alleen gebruikt voor visualisatie van geselecteerde chip, bets staan los
            return;
        }

        // Chip-knoppen (two rows: 4 columns x 2 rows, centered under wheel)
        int perRow = 4;
        float startX = WCX - ((perRow - 1) * ChipStepX) / 2f;
        for (int i = 0; i < ChipValues.Length; i++)
        {
            int col = i % perRow;
            int row = i / perRow; // 0 or 1
            float cx = startX + col * ChipStepX;
            float cy = ChipY - row * ChipStepY;
            if (Raylib.CheckCollisionPointCircle(m, new Vector2(cx, cy), ChipR))
            {
                _selectedChip = i;
                return;
            }
        }

        // Klik op inzetzone
        foreach (var zone in _zones)
        {
            if (!Hit(m, zone.Rect)) continue;
            PlaceBet(zone);
            return;
        }
    }

    private void PlaceBet(BetZone zone)
    {
        int val = ChipValues[_selectedChip];
        if (_state.Balance - TotalBet < val)
        {
            SetStatus("Niet genoeg saldo!", StatusKind.Lose);
            return;
        }
        string key = ZoneKey(zone);
        if (!_bets.TryGetValue(key, out var entry))
        {
            entry = (zone, new List<int>());
            _bets[key] = entry;
        }
        entry.Chips.Add(val);
        _betChips.Add(val);   // voor toren-weergave
        if (_state.Sounds != null) Raylib.PlaySound(_state.Sounds.ChipClick);
    }

    private void DoAllIn()
    {
        // Enter all-in mode: wait for zone selection
        _allInMode = true;
        SetStatus("ALL IN modus: Klik op een vakje om je inzet te plaatsen.", StatusKind.Neutral);
    }

    private void PlaceAllInBet(BetZone zone)
    {
        // Place all remaining balance on the selected zone
        _bets.Clear();
        _betChips.Clear();
        int all = _state.Balance;
        string key = ZoneKey(zone);
        _bets[key] = (zone, new List<int> { all });
        _betChips.Add(all);
        SetStatus($"ALL IN geplaatst op {GetZoneName(zone)} voor {all} munten!", StatusKind.Neutral);
    }

    private static string GetZoneName(BetZone zone) => zone.Type switch
    {
        BetType.Straight => $"getal {zone.Number}",
        BetType.Red => "ROOD",
        BetType.Black => "ZWART",
        BetType.Even => "EVEN",
        BetType.Odd => "ONEVEN",
        BetType.Low => "1-18",
        BetType.High => "19-36",
        BetType.Dozen1 => "Dozijn 1",
        BetType.Dozen2 => "Dozijn 2",
        BetType.Dozen3 => "Dozijn 3",
        BetType.Column1 => "Kolom 1",
        BetType.Column2 => "Kolom 2",
        BetType.Column3 => "Kolom 3",
        _ => "onbekend"
    };

    private void StartSpin()
    {
        if (TotalBet < 1 || TotalBet > _state.Balance) return;
        _state.Balance -= TotalBet;
        _rState    = RState.Spinning;
        _spinStart = Raylib.GetTime();
        _result    = -1;
        _ballAngle = Random.Shared.NextSingle() * 360f;
        SetStatus("De bal rolt...", StatusKind.Neutral);
    }

    private void ResolveBets()
    {
        int n    = _result;
        int paid = 0;

        foreach (var (_, entry) in _bets)
        {
            int stake = entry.Chips.Sum();
            bool wins = entry.Zone.Type switch
            {
                BetType.Straight => entry.Zone.Number == n,
                BetType.Red      => Reds.Contains(n),
                BetType.Black    => n != 0 && !Reds.Contains(n),
                BetType.Even     => n != 0 && n % 2 == 0,
                BetType.Odd      => n % 2 == 1,
                BetType.Low      => n >= 1 && n <= 18,
                BetType.High     => n >= 19 && n <= 36,
                BetType.Dozen1   => n >= 1  && n <= 12,
                BetType.Dozen2   => n >= 13 && n <= 24,
                BetType.Dozen3   => n >= 25 && n <= 36,
                BetType.Column1  => n != 0 && n % 3 == 1,
                BetType.Column2  => n != 0 && n % 3 == 2,
                BetType.Column3  => n != 0 && n % 3 == 0,
                _                => false
            };
            if (wins)
            {
                int mult = entry.Zone.Type switch
                {
                    BetType.Straight => 36,
                    BetType.Dozen1 or BetType.Dozen2 or BetType.Dozen3
                        or BetType.Column1 or BetType.Column2 or BetType.Column3 => 3,
                    _ => 2
                };
                paid += stake * mult;
            }
        }

        _lastResult    = n;
        _state.Balance += paid;
        int spent  = TotalBet;
        int profit = paid - spent;
        string kleur = n == 0 ? "GROEN" : Reds.Contains(n) ? "ROOD" : "ZWART";

        if (_state.Balance <= 0)
        {
            _gameOver = true;
            SetStatus($"Getal: {n} ({kleur}) — GAME OVER!", StatusKind.Lose);
            return;
        }

        if (paid == 0)
        {
            if (_state.Sounds != null) Raylib.PlaySound(_state.Sounds.LoseTone);
            SetStatus($"Getal: {n} ({kleur}) — Helaas, geen winst. -{spent} munten.", StatusKind.Lose);
        }
        else
        {
            if (_state.Sounds != null) Raylib.PlaySound(_state.Sounds.WinChime);
            string sign = profit >= 0 ? $"+{profit}" : $"{profit}";
            SetStatus($"Getal: {n} ({kleur}) — Uitbetaling: {paid} ({sign} munten).", StatusKind.Win);
        }
    }

    private void NewRound()
    {
        _bets.Clear();
        _betChips.Clear();
        _rState = RState.Betting;
        _result = -1;
        if (_state.Balance <= 0) { _gameOver = true; return; }
        SetStatus("Kies chips, klik een vakje en druk DRAAIEN.", StatusKind.Neutral);
    }

    private void Restart()
    {
        _state.Balance = GlobalState.StartingBalance;
        _gameOver  = false;
        NewRound();
    }

    private void SetStatus(string msg, StatusKind kind) { _statusMsg = msg; _statusKind = kind; }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public void Draw()
    {
        DrawHeader();
        DrawWheel();
        DrawTable();
        DrawChipBar();
        DrawBetTower();
        DrawActionButtons();
        DrawStatus();
        DrawHint();
        if (_gameOver) DrawGameOverOverlay();
    }

    private void DrawHeader()
    {
        // Donkere achtergrondstrook
        Raylib.DrawRectangle(0, 0, W, AppTheme.HeaderH, AppTheme.BgHeader);
        Raylib.DrawRectangle(0, AppTheme.HeaderH, W, 2, AppTheme.Separator);

        // Spelnaam — links
        const string name = "ROULETTE";
        Raylib.DrawText(name, 14, (AppTheme.HeaderH - 26) / 2, 26, AppTheme.AccentGold);

        // Saldo & totale inzet — midden
        string balTxt = TotalBet > 0
            ? $"Saldo:  {_state.Balance}  munten  |  Inzet: {TotalBet}"
            : $"Saldo:  {_state.Balance}  munten";
        int bw = Raylib.MeasureText(balTxt, 16);
        Raylib.DrawText(balTxt, (W - bw) / 2, (AppTheme.HeaderH - 16) / 2, 16, AppTheme.TextPrimary);

        // Terugknop — rechts
        DrawButton(MenuButtonRect(), "← TERUG", _rState != RState.Spinning, AppTheme.BtnBack);
    }

    // ── Roulettewiel ─────────────────────────────────────────────────────────

    private void DrawWheel()
    {
        float cx = WCX, cy = WCY, r = WR;
        int n    = WheelOrder.Length;
        float sa = 360f / n;

        // Buitenste decoratieve rand (donker hout)
        Raylib.DrawCircleV(new Vector2(cx, cy), r + 14f, new Color(25, 15, 5, 255));
        Raylib.DrawCircleV(new Vector2(cx, cy), r + 10f, new Color(60, 42, 12, 255));

        // Gekleurde buitenring: gebruik correcte roulette-kleuren
        Color segRed   = new Color(200, 30, 30, 255);
        Color segBlack = new Color(20, 20, 20, 255);
        Color segGreen = new Color(0, 150, 60, 255);
        // Gekleurde buitenring: afwisselend rood/zwart/groen (weergeeft kleurverdeling)
        for (int i = 0; i < n; i++)
        {
            int num  = WheelOrder[i];
            float a0 = _wheelAngle + i * sa - sa / 2f;
            float a1 = a0 + sa;
            Color ringCol = num == 0 ? segGreen : Reds.Contains(num) ? segRed : segBlack;
            DrawPie(cx, cy, r + 10f, a0, a1, ringCol, 6);
        }

        // Gouden scheidingsring
        Raylib.DrawRing(new Vector2(cx, cy), r - 1f, r + 1f, 0f, 360f, 60, new Color(180, 150, 30, 200));

        // Segmenten (het speelvlak) - kleur alleen de buitenring / segmenten
        for (int i = 0; i < n; i++)
        {
            int num  = WheelOrder[i];
            float a0 = _wheelAngle + i * sa - sa / 2f;
            float a1 = a0 + sa;
            Color col = num == 0 ? segGreen : Reds.Contains(num) ? segRed : segBlack;

            // Highlight gevallen getal
            if (_result == num)
                col = new Color(
                    (byte)Math.Min(255, col.R + 90),
                    (byte)Math.Min(255, col.G + 90),
                    (byte)Math.Min(255, col.B + 90), (byte)255);

            DrawPie(cx, cy, r, a0, a1, col, 8);
        }

        // Bedek het binnenste gedeelte zodat de kleur als rand zichtbaar blijft
        float innerCover = r * 0.70f; // grootte van het donkere binnenveld
        Raylib.DrawCircleV(new Vector2(cx, cy), innerCover, new Color(22, 17, 6, 255));

        // Nu de labels op de gekleurde rand tekenen
        for (int i = 0; i < n; i++)
        {
            int num  = WheelOrder[i];
            float a0 = _wheelAngle + i * sa - sa / 2f;
            float a1 = a0 + sa;
            float midA = (a0 + a1) / 2f * MathF.PI / 180f;
            float tr   = r * 0.86f; // iets verder naar buiten op de gekleurde rand
            float lx   = cx + tr * MathF.Cos(midA);
            float ly   = cy + tr * MathF.Sin(midA);
            string lbl = num.ToString();
            int    lfs = 10;
            int    lw  = Raylib.MeasureText(lbl, lfs);
            // bepaal kleur van tekst op basis van segmentkleur
            Color segCol = num == 0 ? segGreen : Reds.Contains(num) ? segRed : segBlack;
            bool   lightFg = segCol.R > 140 || segCol.G > 140 || segCol.B > 140;
            Color  fg = lightFg ? Color.Black : Color.White;
            Raylib.DrawText(lbl, (int)(lx - lw / 2f), (int)(ly - lfs / 2f), lfs, fg);
        }
        // Segment-scheidingslijnen
        for (int i = 0; i < n; i++)
        {
            float a = (_wheelAngle + i * sa) * MathF.PI / 180f;
            Raylib.DrawLine(
                (int)(cx + r * 0.30f * MathF.Cos(a)), (int)(cy + r * 0.30f * MathF.Sin(a)),
                (int)(cx + r * MathF.Cos(a)),          (int)(cy + r * MathF.Sin(a)),
                new Color(55, 45, 10, 180));
        }

        // Binnenste naaf (accent bovenop de cover)
        Raylib.DrawCircleV(new Vector2(cx, cy), r * 0.29f, new Color(22, 17, 6, 255));
        Raylib.DrawCircleLines((int)cx, (int)cy, (int)(r * 0.29f), new Color(90, 72, 18, 255));

        // Buitenringen
        Raylib.DrawCircleLines((int)cx, (int)cy, (int)r,       new Color(160, 130, 28, 255));
        Raylib.DrawCircleLines((int)cx, (int)cy, (int)(r + 6), new Color(90,  72,  18, 255));

        // Bal
        float ballDist = r + 3f;
        float bx = cx + ballDist * MathF.Cos(_ballAngle * MathF.PI / 180f);
        float by = cy + ballDist * MathF.Sin(_ballAngle * MathF.PI / 180f);
        Raylib.DrawCircleV(new Vector2(bx, by), 7f, new Color(235, 235, 235, 255));
        Raylib.DrawCircleV(new Vector2(bx - 2f, by - 2f), 2.5f, new Color(255, 255, 255, 180));

        // Resultaat in midden
        if (_result >= 0)
        {
            Color rc = _result == 0 ? new Color(0, 210, 55, 255)
                     : Reds.Contains(_result) ? new Color(255, 65, 65, 255)
                     : new Color(210, 210, 210, 255);
            string rs = _result.ToString();
            int    rw = Raylib.MeasureText(rs, 32);
            Raylib.DrawText(rs, (int)(cx - rw / 2f), (int)(cy - 16f), 32, rc);
        }
        else if (_rState == RState.Spinning)
        {
            int dw = Raylib.MeasureText("...", 20);
            Raylib.DrawText("...", (int)(cx - dw / 2f), (int)(cy - 10f), 20, Color.Gray);
        }

        // Vorig resultaat (onder wiel)
        if (_lastResult >= 0 && _rState == RState.Betting)
        {
            Color pc = _lastResult == 0 ? Color.Green
                     : Reds.Contains(_lastResult) ? new Color(220, 65, 65, 255)
                     : Color.LightGray;
            string prev = $"Vorig: {_lastResult}";
            int pw = Raylib.MeasureText(prev, 12);
            Raylib.DrawText(prev, (int)(cx - pw / 2f), (int)(cy + r + 16f), 12, pc);
        }
    }

    private static void DrawPie(float cx, float cy, float r, float a0, float a1, Color col, int steps)
    {
        float px = cx + r * MathF.Cos(a0 * MathF.PI / 180f);
        float py = cy + r * MathF.Sin(a0 * MathF.PI / 180f);
        for (int s = 1; s <= steps; s++)
        {
            float t = s / (float)steps;
            float a = (a0 + (a1 - a0) * t) * MathF.PI / 180f;
            float nx = cx + r * MathF.Cos(a);
            float ny = cy + r * MathF.Sin(a);
            Raylib.DrawTriangle(new Vector2(cx, cy), new Vector2(px, py), new Vector2(nx, ny), col);
            px = nx; py = ny;
        }
    }

    // ── Tafel ─────────────────────────────────────────────────────────────────

    private void DrawTable()
    {
        Vector2 mouse = CanvasMouse();

        // Achtergrond tafelkleed
        float tW = CW * 7 + 6f, tH = CH * 14 + 14f;
        Raylib.DrawRectangle((int)TX - 4, (int)TY - 4, (int)tW, (int)tH, new Color(0, 70, 5, 255));

        // Nul
        DrawCell(0, TX, TY, CW, CH * 3, mouse, isNumber: true);

        // Nummers 1-36
        for (int num = 1; num <= 36; num++)
        {
            int row = (num - 1) / 3;
            int col = (num - 1) % 3;
            DrawCell(num, TX + CW + col * CW, TY + row * CH, CW, CH, mouse, isNumber: true);
        }

        float dY = TY + 12 * CH + 5f;

        // Dozijnen
        DrawLabelCell("1-12",  BetType.Dozen1,  TX + CW,        dY, CW, CH, mouse);
        DrawLabelCell("13-24", BetType.Dozen2,  TX + CW * 2,    dY, CW, CH, mouse);
        DrawLabelCell("25-36", BetType.Dozen3,  TX + CW * 3,    dY, CW, CH, mouse);
        DrawLabelCell("2:1",   BetType.Column1, TX + CW * 4,    dY, CW, CH, mouse);
        DrawLabelCell("2:1",   BetType.Column2, TX + CW * 5,    dY, CW, CH, mouse);
        DrawLabelCell("2:1",   BetType.Column3, TX + CW * 6,    dY, CW, CH, mouse);

        // Even-kansen
        float eY = dY + CH + 4f;
        DrawLabelCell("1-18",   BetType.Low,   TX + CW,        eY, CW, CH, mouse);
        DrawLabelCell("EVEN",   BetType.Even,  TX + CW * 2,    eY, CW, CH, mouse);
        DrawColorCell(true,                    TX + CW * 3,    eY, CW, CH, mouse);   // Rood
        DrawColorCell(false,                   TX + CW * 4,    eY, CW, CH, mouse);   // Zwart
        DrawLabelCell("ONEVEN", BetType.Odd,   TX + CW * 5,    eY, CW, CH, mouse);
        DrawLabelCell("19-36",  BetType.High,  TX + CW * 6,    eY, CW, CH, mouse);

        // Geplaatste chips
        foreach (var (_, entry) in _bets)
        {
            if (entry.Chips.Count == 0) continue;
            var r = entry.Zone.Rect;
            float ccx = r.X + r.Width  / 2f;
            float ccy = r.Y + r.Height / 2f;
            int total = entry.Chips.Sum();
            int ci    = ChipValues.Select((v, i) => (v, i)).Where(t => t.v <= total)
                            .Select(t => t.i).DefaultIfEmpty(0).Last();
            Color col = ChipColors[ci];
            Raylib.DrawCircleV(new Vector2(ccx, ccy), 9f, col);
            string lbl = total >= 1000 ? "1K" : total.ToString();
            int fs2 = 7, tw2 = Raylib.MeasureText(lbl, fs2);
            bool light = col.R > 155 && col.G > 155 && col.B > 155;
            Raylib.DrawText(lbl, (int)(ccx - tw2 / 2), (int)(ccy - fs2 / 2), fs2,
                light ? Color.Black : Color.White);
        }
    }

    private void DrawCell(int num, float x, float y, float w, float h, Vector2 mouse, bool isNumber)
    {
        Color bg = num == 0 ? new Color(0, 140, 35, 255)
                 : Reds.Contains(num) ? new Color(158, 18, 18, 255)
                 : new Color(15, 15, 15, 255);

        bool hover = _rState == RState.Betting && Hit(mouse, new Rectangle(x, y, w, h));
        if (hover) bg = Brighten(bg, 60);
        if (_result == num)
            bg = new Color(220, 200, 0, 255);

        Raylib.DrawRectangle((int)x, (int)y, (int)w, (int)h, bg);
        Raylib.DrawRectangleLines((int)x, (int)y, (int)w, (int)h, new Color(60, 60, 60, 255));

        string lbl = num.ToString();
        int fs = 11, tw = Raylib.MeasureText(lbl, fs);
        Color fg = (_result == num) ? Color.Black : Color.White;
        Raylib.DrawText(lbl, (int)(x + (w - tw) / 2), (int)(y + (h - fs) / 2), fs, fg);
    }

    private void DrawLabelCell(string label, BetType type, float x, float y, float w, float h, Vector2 mouse)
    {
        Color bg   = new Color(0, 85, 10, 255);
        bool hover = _rState == RState.Betting && Hit(mouse, new Rectangle(x, y, w, h));
        if (hover) bg = new Color(0, 140, 20, 255);

        // Highlight als dit de winnende zone is
        if (_result >= 0 && ZoneWins(type, 0, _result))
            bg = new Color(180, 160, 0, 255);

        Raylib.DrawRectangle((int)x, (int)y, (int)w, (int)h, bg);
        Raylib.DrawRectangleLines((int)x, (int)y, (int)w, (int)h, new Color(60, 60, 60, 255));
        int fs = 9, tw = Raylib.MeasureText(label, fs);
        bool lit = bg.R > 120 && bg.G > 120;
        Raylib.DrawText(label, (int)(x + (w - tw) / 2), (int)(y + (h - fs) / 2), fs,
            lit ? Color.Black : Color.White);
    }

    private void DrawColorCell(bool red, float x, float y, float w, float h, Vector2 mouse)
    {
        Color bg   = red ? new Color(158, 18, 18, 255) : new Color(15, 15, 15, 255);
        bool hover = _rState == RState.Betting && Hit(mouse, new Rectangle(x, y, w, h));
        if (hover) bg = Brighten(bg, 55);

        BetType bt = red ? BetType.Red : BetType.Black;
        if (_result >= 0 && ZoneWins(bt, 0, _result))
            bg = new Color(220, 200, 0, 255);

        Raylib.DrawRectangle((int)x, (int)y, (int)w, (int)h, bg);
        Raylib.DrawRectangleLines((int)x, (int)y, (int)w, (int)h, new Color(60, 60, 60, 255));
        string lbl = red ? "ROOD" : "ZWART";
        int fs = 9, tw = Raylib.MeasureText(lbl, fs);
        bool lit = bg.R > 150 && bg.G > 150;
        Raylib.DrawText(lbl, (int)(x + (w - tw) / 2), (int)(y + (h - fs) / 2), fs,
            lit ? Color.Black : Color.White);
    }

    private static bool ZoneWins(BetType type, int zoneNum, int result) => type switch
    {
        BetType.Red     => Reds.Contains(result),
        BetType.Black   => result != 0 && !Reds.Contains(result),
        BetType.Even    => result != 0 && result % 2 == 0,
        BetType.Odd     => result % 2 == 1,
        BetType.Low     => result >= 1 && result <= 18,
        BetType.High    => result >= 19 && result <= 36,
        BetType.Dozen1  => result >= 1  && result <= 12,
        BetType.Dozen2  => result >= 13 && result <= 24,
        BetType.Dozen3  => result >= 25 && result <= 36,
        BetType.Column1 => result != 0 && result % 3 == 1,
        BetType.Column2 => result != 0 && result % 3 == 2,
        BetType.Column3 => result != 0 && result % 3 == 0,
        _ => false
    };

    private static Color Brighten(Color c, int v) =>
        new Color((byte)Math.Min(255, c.R + v),
                  (byte)Math.Min(255, c.G + v),
                  (byte)Math.Min(255, c.B + v), c.A);

    // ── Chip-balk (identiek patroon als BJGame) ────────────────────────────────

    private void DrawChipBar()
    {
        Vector2 m = CanvasMouse();
        // Draw label above the chip grid (centered beneath the wheel)
        int perRow = 4;
        int rows = (ChipValues.Length + perRow - 1) / perRow;
        float startX = WCX - ((perRow - 1) * ChipStepX) / 2f;
        float topY = ChipY - (rows - 1) * ChipStepY;
        Raylib.DrawText("Chipwaarde:", (int)startX, (int)(topY - 22f), 14, AppTheme.TextLabel);

        for (int i = 0; i < ChipValues.Length; i++)
        {
            int   val  = ChipValues[i];
            int col = i % perRow;
            int row = i / perRow;
            float cx   = startX + col * ChipStepX;
            float cy   = ChipY - row * ChipStepY;
            bool  sel  = i == _selectedChip;
            bool  avail = _state.Balance - TotalBet >= val;
            bool  hover = avail && Raylib.CheckCollisionPointCircle(m, new Vector2(cx, cy), ChipR);

            Color main = avail ? ChipColors[i] : new Color(50, 50, 55, 255);
            if (sel)   Raylib.DrawCircleV(new Vector2(cx, cy), ChipR + 5f, Color.Yellow);
            if (hover && !sel) main = Brighten(main, 50);

            Color dark  = ScaleColor(main, 0.42f);
            Color light = Brighten(main, 55);

            Raylib.DrawCircleV(new Vector2(cx + 2, cy + 4), ChipR, new Color(0, 0, 0, 80));
            Raylib.DrawCircleV(new Vector2(cx, cy), ChipR, dark);
            Raylib.DrawCircleV(new Vector2(cx, cy), ChipR - 2f, main);

            Color segCol = avail ? new Color(255, 255, 255, 100) : new Color(120, 120, 120, 40);
            for (int s = 0; s < 12; s += 2)
            {
                float a0 = s * 30f - 90f;
                Raylib.DrawRing(new Vector2(cx, cy), ChipR - 7f, ChipR - 2f, a0, a0 + 21f, 4, segCol);
            }
            Raylib.DrawRing(new Vector2(cx, cy), ChipR - 9f, ChipR - 7.5f, 0, 360, 20, new Color(0,0,0,55));
            Raylib.DrawCircleV(new Vector2(cx, cy), ChipR - 9f, ScaleColor(main, 0.82f));
            Raylib.DrawRing(new Vector2(cx, cy), ChipR - 11f, ChipR - 9f, 0, 360, 20,
                avail ? new Color(255,255,255,65) : new Color(100,100,100,30));
            Raylib.DrawCircleV(new Vector2(cx, cy), ChipR - 11f, main);

            string lbl2 = val >= 1000 ? "1K" : val.ToString();
            int fs2 = val >= 100 ? 12 : 13;
            int lw2 = Raylib.MeasureText(lbl2, fs2);
            bool lit2 = main.R > 155 && main.G > 155 && main.B > 155;
            Color tc = avail ? (lit2 ? new Color(25,25,25,255) : Color.White) : new Color(80,80,80,255);
            if (avail)
                Raylib.DrawText(lbl2, (int)cx - lw2/2 + 1, (int)cy - fs2/2 + 1, fs2,
                    new Color(0, 0, 0, lit2 ? 70 : 170));
            Raylib.DrawText(lbl2, (int)cx - lw2/2, (int)cy - fs2/2, fs2, tc);
        }
    }

    // ── Bet-toren (zelfde stijl als BJGame) ───────────────────────────────────

    private void DrawBetTower()
    {
        float cx     = WCX;   // gecentreerd onder het wiel
        const float baseY  = 430f;
        const float rH     = 26f, rV = 7f, step = 11f;
        const int   maxVis = 6;

        var bgRect = new Rectangle(cx - rH - 4, 358f, (rH + 4) * 2, baseY - 358 + 24);
        Raylib.DrawRectangleRec(bgRect, new Color(6, 6, 16, 200));
        Raylib.DrawRectangleLinesEx(bgRect, 1, new Color(40, 40, 60, 255));
        Raylib.DrawText("INZET",
            (int)(cx - Raylib.MeasureText("INZET", 12) / 2), 362, 12, new Color(100, 100, 120, 255));

        int count    = _betChips.Count;
        int startIdx = Math.Max(0, count - maxVis);
        for (int i = startIdx; i < count; i++)
        {
            int val = _betChips[i];
            int ci2 = ChipValues.Select((v, idx) => (v, idx)).Where(t => t.v <= val)
                          .Select(t => t.idx).DefaultIfEmpty(0).Last();
            Color col = ChipColors[ci2];
            float y   = baseY - (i - startIdx + 1) * step;

            Raylib.DrawEllipse((int)cx + 2, (int)y + 3, (int)rH, (int)rV, new Color(0,0,0,60));
            Raylib.DrawEllipse((int)cx, (int)y + 2, (int)rH, (int)rV, ScaleColor(col, 0.25f));
            Raylib.DrawEllipse((int)cx, (int)y, (int)rH, (int)rV, col);
            Raylib.DrawEllipse((int)cx, (int)y - 2, (int)(rH - 6), (int)Math.Max(1, rV - 4),
                new Color((byte)Math.Min(255, col.R + 70), (byte)Math.Min(255, col.G + 70), (byte)Math.Min(255, col.B + 70), (byte)120));
        }

        if (count > maxVis)
        {
            string ovf = $"+{count - maxVis}";
            int ow = Raylib.MeasureText(ovf, 10);
            Raylib.DrawText(ovf, (int)cx - ow / 2, (int)(baseY - maxVis * step - 12), 10, Color.Gray);
        }

        string tot   = TotalBet > 0 ? $"{TotalBet}" : "-";
        Color totCol = TotalBet > 0 ? Color.Yellow : new Color(55, 55, 55, 255);
        int tw3      = Raylib.MeasureText(tot, 17);
        Raylib.DrawText(tot, (int)cx - tw3 / 2, (int)baseY + 4, 17, totCol);
    }

    // ── Knoppen ───────────────────────────────────────────────────────────────

    private void DrawActionButtons()
    {
        if (_gameOver) return;

        switch (_rState)
        {
            case RState.Betting:
                DrawButton(SpinButtonRect(),  "DRAAIEN",  TotalBet >= 1,         Color.DarkGreen);
                DrawButton(ClearButtonRect(), "WISSEN",   _bets.Count > 0,       new Color(110, 50, 15, 255));
                DrawButton(AllInButtonRect(), "ALL IN",   _state.Balance > 0,    new Color(140, 20, 20, 255));
                break;
            case RState.Result:
                DrawButton(NewRoundButtonRect(), "NIEUW SPEL", true, Color.DarkGreen);
                break;
        }
        DrawButton(MenuButtonRect(), "← MENU", _rState != RState.Spinning, new Color(50, 50, 90, 255));
    }

    private void DrawStatus()
    {
        Color c = _statusKind switch
        {
            StatusKind.Win  => AppTheme.StatusWin,
            StatusKind.Lose => AppTheme.StatusLose,
            _               => AppTheme.StatusNeutral
        };
        const int fs = 16;
        int sw2 = Raylib.MeasureText(_statusMsg, fs);
        Raylib.DrawText(_statusMsg, Math.Max(10, (W - sw2) / 2), 542, fs, c);
    }

    private static void DrawHint()
    {
        const string h = "F11: volledig scherm  |  Esc: menu  |  Enter: draaien / nieuw spel";
        int hw = Raylib.MeasureText(h, 14);
        Raylib.DrawText(h, Math.Max(4, (W - hw) / 2), H - 20, 14, AppTheme.TextHint);
    }

    private void DrawGameOverOverlay()
    {
        Raylib.DrawRectangle(0, 0, W, H, AppTheme.BgOverlay);
        const string t = "GAME  OVER";
        int tw = Raylib.MeasureText(t, 72);
        Raylib.DrawText(t, (W - tw) / 2, H / 2 - 110, 72, AppTheme.AccentDanger);
        const string sub = "U heeft geen munten meer.";
        int sw = Raylib.MeasureText(sub, 26);
        Raylib.DrawText(sub, (W - sw) / 2, H / 2 - 20, 26, AppTheme.TextPrimary);
        DrawButton(RestartButtonRect(), "OPNIEUW SPELEN", true, AppTheme.BtnPrimary);
    }

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

    private static Rectangle MenuButtonRect()     => new Rectangle(W - 106f, 8f, 96f, 32f);
    private static Rectangle SpinButtonRect()     => new Rectangle(560f, 460f, 110f, 34f);
    private static Rectangle ClearButtonRect()    => new Rectangle(560f, 500f, 110f, 34f);
    private static Rectangle AllInButtonRect()    => new Rectangle(678f, 460f,  90f, 74f);
    private static Rectangle NewRoundButtonRect() => new Rectangle(310f, 460f, 130f, 34f);
    private static Rectangle RestartButtonRect()  => new Rectangle((W - 200) / 2f, H / 2f + 50, 200, 50);
    private static Rectangle TowerRect()          => new Rectangle(129f, 358f, 54f, 72f);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Vector2 CanvasMouse()
    {
        Vector2 m = Raylib.GetMousePosition();
        return new Vector2((m.X - _drawOffsetX) / _drawScale,
                           (m.Y - _drawOffsetY) / _drawScale);
    }

    private static bool Hit(Vector2 m, Rectangle r) => Raylib.CheckCollisionPointRec(m, r);

    private static Color ScaleColor(Color c, float f) =>
        new Color((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f), c.A);
}