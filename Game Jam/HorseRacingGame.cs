using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Game_Jam;

public class HorseRacingGame
{
    private const int W = 800, H = 600;

    private static readonly int[] ChipValues = [1, 5, 10, 20, 50, 100, 500, 1000];
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
    private const float ChipX0   = 730f;
    private const float ChipY    = 160f;
    private const float ChipStepX = 0f;
    private const float ChipStepY = 55f;

    private const float TrackX = 90f;
    private const float TrackY = 110f;
    private const float TrackW = 600f;
    private const float TrackH = 44f;
    private const int HorseCount = 6;

    private enum BetType { Win, Place, Show }
    private enum StatusKind { Neutral, Win, Lose }

    private readonly GlobalState _state;
    private readonly List<Horse> _horses = new();
    private readonly List<int> _finishOrder = new();
    private readonly float[] _positions = new float[HorseCount];
    private readonly float[] _speeds = new float[HorseCount];
    private readonly float[] _baseSpeeds = new float[HorseCount];
    private readonly float[] _speedPhases = new float[HorseCount];
    private readonly List<int> _betChips = new();

    private int _selectedChip = 1;
    private int _selectedHorse = 0;
    private BetType _selectedBetType = BetType.Win;
    private int _betAmount;
    private bool _raceStarted;
    private bool _raceFinished;
    private string _statusMsg = "Kies een paard, kies een inzet en klik RACE.";
    private StatusKind _statusKind = StatusKind.Neutral;
    private double _lastTime;

    public bool WantsToGoBack { get; private set; }

    public HorseRacingGame(GlobalState state)
    {
        _state = state;
        _lastTime = Raylib.GetTime();
        BuildHorses();
    }

    public void Reset()
    {
        if (_state.Balance <= 0) _state.Balance = GlobalState.StartingBalance;
        _betChips.Clear();
        _betAmount      = 0;
        _selectedChip   = 1;
        _selectedHorse  = 0;
        _selectedBetType = BetType.Win;
        _raceStarted    = false;
        _raceFinished   = false;
        _finishOrder.Clear();
        for (int i = 0; i < HorseCount; i++) _positions[i] = 0f;
        _statusMsg      = "Kies een paard, kies een inzet en klik RACE.";
        _statusKind     = StatusKind.Neutral;
        WantsToGoBack   = false;
        BuildHorses();
        _lastTime = Raylib.GetTime();
    }

    public void SetDrawParams(int ox, int oy, float scale)
    {
        _drawOffsetX = ox;
        _drawOffsetY = oy;
        _drawScale = scale;
    }

    public void Update()
    {
        double now = Raylib.GetTime();
        float dt = (float)(now - _lastTime);
        _lastTime = now;

        if (_raceStarted) UpdateRaceAnimation(dt);
        HandleInput();
    }

    private void HandleInput()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) WantsToGoBack = true;
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

        Vector2 m = CanvasMouse();

        if (Hit(m, MenuButtonRect())) { WantsToGoBack = true; return; }
        if (_raceStarted)
        {
            if (_raceFinished && Hit(m, NewRaceButtonRect())) NewRace();
            return;
        }

        if (Hit(m, RaceButtonRect()) && _betAmount > 0)
        {
            StartRace();
            return;
        }

        if (Hit(m, ClearButtonRect()) && _betAmount > 0)
        {
            ClearBet();
            return;
        }

        if (Hit(m, PlaceBetButtonRect()))
        {
            PlaceChip();
            return;
        }

        if (Hit(m, WinButtonRect())) { _selectedBetType = BetType.Win; return; }
        if (Hit(m, PlaceButtonRect())) { _selectedBetType = BetType.Place; return; }
        if (Hit(m, ShowButtonRect())) { _selectedBetType = BetType.Show; return; }

        for (int i = 0; i < HorseCount; i++)
        {
            if (Hit(m, HorseRect(i)))
            {
                _selectedHorse = i;
                SetStatus($"Paard {_horses[i].Name} geselecteerd.", StatusKind.Neutral);
                return;
            }
        }

        float startX = ChipX0;
        float startY = ChipY;
        for (int i = 0; i < ChipValues.Length; i++)
        {
            float cx = startX;
            float cy = startY + i * ChipStepY;
            if (Raylib.CheckCollisionPointCircle(m, new Vector2(cx, cy), ChipR))
            {
                _selectedChip = i;
                return;
            }
        }
    }

    private void PlaceChip()
    {
        int val = ChipValues[_selectedChip];
        if (_state.Balance - _betAmount < val)
        {
            SetStatus("Niet genoeg saldo voor deze chip.", StatusKind.Lose);
            return;
        }
        _betAmount += val;
        _betChips.Add(val);
        SetStatus($"Inzet verhoogd tot {_betAmount}.", StatusKind.Neutral);
    }

    private void ClearBet()
    {
        _betAmount = 0;
        _betChips.Clear();
        SetStatus("Inzet gewist.", StatusKind.Neutral);
    }

    private void StartRace()
    {
        if (_betAmount <= 0)
        {
            SetStatus("Plaats eerst een inzet.", StatusKind.Lose);
            return;
        }

        _state.Balance -= _betAmount;
        InitializeRace();
        _raceStarted = true;
        _raceFinished = false;
        SetStatus("De race is begonnen!", StatusKind.Neutral);
    }

    private void NewRace()
    {
        _betAmount = 0;
        _betChips.Clear();
        _raceStarted = false;
        _raceFinished = false;
        _finishOrder.Clear();
        for (int i = 0; i < HorseCount; i++) _positions[i] = 0f;
        SetStatus("Kies een paard, kies een inzet en klik RACE.", StatusKind.Neutral);
    }

    private void InitializeRace()
    {
        _finishOrder.Clear();
        for (int i = 0; i < HorseCount; i++)
        {
            _positions[i] = 0f;
            _baseSpeeds[i] = 55f + _horses[i].Weight * 2f + Random.Shared.NextSingle() * 8f;
            _speedPhases[i] = Random.Shared.NextSingle() * MathF.PI * 2f;
            _speeds[i] = _baseSpeeds[i];
        }
    }

    private void UpdateRaceAnimation(float dt)
    {
        if (_raceFinished) return;

        float finishX = TrackW - 120f;

        for (int i = 0; i < HorseCount; i++)
        {
            if (_positions[i] >= finishX)
            {
                if (!_finishOrder.Contains(i)) _finishOrder.Add(i);
                continue;
            }

            float time = (float)Raylib.GetTime();
            float pace = MathF.Sin(time * 2.5f + _speedPhases[i]) * 14f;
            float burst = (Random.Shared.NextSingle() - 0.5f) * 5f;
            _speeds[i] = MathF.Max(40f, _baseSpeeds[i] + pace + burst);

            _positions[i] += _speeds[i] * dt;
            if (_positions[i] >= finishX)
            {
                _positions[i] = finishX;
                _finishOrder.Add(i);
            }
        }

        if (_finishOrder.Count == HorseCount)
        {
            _raceFinished = true;
            ResolveRace();
        }
    }

    private void ResolveRace()
    {
        int rank = _finishOrder.IndexOf(_selectedHorse);
        bool won = _selectedBetType == BetType.Win && rank == 0;
        bool placed = _selectedBetType == BetType.Place && rank <= 1;
        bool showed = _selectedBetType == BetType.Show && rank <= 2;
        int payout = 0;

        if (won)
        {
            payout = (int)MathF.Round(_betAmount * _horses[_selectedHorse].Odds);
        }
        else if (placed)
        {
            payout = (int)MathF.Round(_betAmount * Math.Max(1.2f, _horses[_selectedHorse].Odds * 0.55f));
        }
        else if (showed)
        {
            payout = (int)MathF.Round(_betAmount * Math.Max(1.1f, _horses[_selectedHorse].Odds * 0.35f));
        }

        if (payout > 0)
        {
            _state.Balance += payout;
            _statusKind = StatusKind.Win;
            _statusMsg = $"Paard {_horses[_selectedHorse].Name} eindigde als {rank + 1}. Uitbetaling: {payout}.";
        }
        else
        {
            _statusKind = StatusKind.Lose;
            _statusMsg = $"Paard {_horses[_selectedHorse].Name} eindigde als {rank + 1}. Je verloor {_betAmount}.";
        }
    }

    private void SetStatus(string msg, StatusKind kind)
    {
        _statusMsg = msg;
        _statusKind = kind;
    }

    private void BuildHorses()
    {
        _horses.Clear();
        _horses.Add(new Horse("Apollo", new Color(230, 75,  75, 255), 18f));
        _horses.Add(new Horse("Blaze",  new Color(75,  190, 75, 255), 24f));
        _horses.Add(new Horse("Comet",  new Color(75,  125, 230, 255), 16f));
        _horses.Add(new Horse("Dynamo",new Color(230, 190, 70, 255), 28f));
        _horses.Add(new Horse("Storm",  new Color(140, 50, 215, 255), 22f));
        _horses.Add(new Horse("Venus",  new Color(240, 110, 40, 255), 12f));

        float total = _horses.Sum(h => h.Weight);
        foreach (var horse in _horses)
        {
            horse.Odds = MathF.Max(1.8f, total / horse.Weight);
        }
    }

    public void Draw()
    {
        DrawTitle();
        DrawBalance();
        DrawTrack();
        DrawBetSection();
        DrawControls();
        DrawStatus();
        if (_raceFinished) DrawResultOverlay();
    }

    private void DrawTitle()
    {
        const string title = "HORSE RACING";
        int tw = Raylib.MeasureText(title, 48);
        Raylib.DrawText(title, (W - tw) / 2, 10, 48, Color.Gold);
        Raylib.DrawRectangle(40, 62, W - 80, 2, new Color(80, 70, 20, 255));
    }

    private void DrawBalance()
    {
        Raylib.DrawText($"Saldo: {_state.Balance}", 100, 72, 20, Color.White);
        Raylib.DrawText($"Inzet: {_betAmount}", 100, 96, 18, Color.Yellow);
        Raylib.DrawText($"Paard: {_horses[_selectedHorse].Name}", 320, 72, 18, Color.White);
        Raylib.DrawText($"Type: {_selectedBetType}", 320, 96, 18, Color.White);
    }

    private void DrawTrack()
    {
        for (int i = 0; i < HorseCount; i++)
        {
            float y = TrackY + i * (TrackH + 10f);
            Raylib.DrawRectangle((int)TrackX, (int)y, (int)TrackW, (int)TrackH, new Color(40, 120, 40, 255));
            Raylib.DrawRectangleLinesEx(new Rectangle(TrackX, y, TrackW, TrackH), 2, Color.Black);

            float markerX = TrackX + TrackW - 120f;
            Raylib.DrawRectangleLines((int)markerX, (int)y, 4, (int)TrackH, Color.Gold);
            Raylib.DrawRectangleLines((int)markerX + 50, (int)y, 4, (int)TrackH, Color.Gold);

            var horse = _horses[i];
            float horseX = TrackX + 14 + _positions[i];
            bool moving = _raceStarted && _positions[i] < TrackW - 120f;
            DrawHorseSprite(horseX, y + 8, TrackH - 16, horse.Color, moving);
            string label = $"{horse.Name} ({horse.Odds:0.0})";
            int lw = Raylib.MeasureText(label, 16);
            Raylib.DrawText(label, (int)(TrackX + 8), (int)(y + 10), 16, Color.White);

            if (_selectedHorse == i)
            {
                Raylib.DrawRectangleLinesEx(new Rectangle(TrackX - 6, y - 4, TrackW + 12, TrackH + 8), 3, Color.Yellow);
            }

            if (Hit(CanvasMouse(), HorseRect(i)))
            {
                Raylib.DrawRectangleLinesEx(new Rectangle(TrackX - 6, y - 4, TrackW + 12, TrackH + 8), 3, new Color(255, 255, 255, 120));
            }
        }
    }

    private void DrawHorseSprite(float x, float y, float height, Color color, bool moving)
    {
        float time = (float)Raylib.GetTime();
        float bob = moving ? MathF.Sin(time * 4.2f + x * 0.08f) * 3.5f : 0f;
        float step = moving ? MathF.Sin(time * 8.5f + x * 0.15f) * 3.2f : 0f;
        float headTilt = moving ? MathF.Sin(time * 5.2f + x * 0.12f) * 1.8f : 0f;
        float baseY = y + bob;
        float bodyW = 78f;
        float bodyH = MathF.Max(18f, height * 0.48f);
        float legH = MathF.Max(10f, height * 0.38f);
        float legW = 7f;
        float headR = MathF.Max(10f, height * 0.30f);
        float bodyY = baseY + (height - bodyH - legH) * 0.6f;
        float legY = bodyY + bodyH - 4f;
        float neckX = x + bodyW * 0.56f;
        float neckY = bodyY + bodyH * 0.14f;
        float headX = neckX + headR * 1.05f;
        float headY = neckY - headR * 0.2f + headTilt;
        Color shade = Darken(color, 40);
        Color shadow = Darken(color, 70);

        Raylib.DrawRectangle((int)(x + 16f), (int)bodyY, (int)(bodyW * 0.72f), (int)bodyH, color);
        Raylib.DrawEllipse((int)(x + bodyW * 0.47f), (int)(bodyY + bodyH * 0.64f), (int)(bodyW * 0.43f), (int)(bodyH * 0.7f), color);
        Raylib.DrawTriangle(
            new Vector2(neckX, neckY),
            new Vector2(neckX + 20f, neckY - 16f),
            new Vector2(neckX + 20f, neckY + 16f),
            color);
        Raylib.DrawCircle((int)headX, (int)headY, (int)headR, color);
        Raylib.DrawCircle((int)(headX + headR * 0.25f), (int)(headY - headR * 0.12f), (int)(headR * 0.2f), Color.Black);
        Raylib.DrawLine((int)(headX + headR * 0.8f), (int)headY, (int)(headX + headR * 1.08f), (int)(headY + headR * 0.08f), shadow);

        Raylib.DrawTriangle(
            new Vector2(x + 8f, bodyY + bodyH * 0.25f),
            new Vector2(x - 4f, bodyY + bodyH * 0.18f),
            new Vector2(x + 12f, bodyY + bodyH * 0.62f),
            shade);
        Raylib.DrawTriangle(
            new Vector2(x + 10f, bodyY + bodyH * 0.22f),
            new Vector2(x + 2f, bodyY + bodyH * 0.18f),
            new Vector2(x + 12f, bodyY + bodyH * 0.52f),
            Darken(shade, 20));

        Raylib.DrawTriangle(
            new Vector2(x + 6f, bodyY + bodyH * 0.14f),
            new Vector2(x - 10f, bodyY + bodyH * 0.06f),
            new Vector2(x + 6f, bodyY + bodyH * 0.45f),
            Darken(color, 55));

        Raylib.DrawRectangle((int)(x + 18f), (int)(legY + step), (int)legW, (int)legH, color);
        Raylib.DrawRectangle((int)(x + 32f), (int)(legY - step * 0.4f), (int)legW, (int)(legH * 0.92f), color);
        Raylib.DrawRectangle((int)(x + 48f), (int)(legY - step * 0.3f), (int)legW, (int)(legH * 0.94f), color);
        Raylib.DrawRectangle((int)(x + 62f), (int)(legY + step * 0.8f), (int)legW, (int)(legH * 0.88f), color);

        Raylib.DrawRectangle((int)(x + 16f), (int)(legY + step + legH - 4f), (int)(legW + 2f), 4, shadow);
        Raylib.DrawRectangle((int)(x + 30f), (int)(legY - step * 0.4f + legH - 4f), (int)(legW + 2f), 4, shadow);
        Raylib.DrawRectangle((int)(x + 46f), (int)(legY - step * 0.3f + legH - 4f), (int)(legW + 2f), 4, shadow);
        Raylib.DrawRectangle((int)(x + 60f), (int)(legY + step * 0.8f + legH - 4f), (int)(legW + 2f), 4, shadow);

        Raylib.DrawLine((int)(neckX - 2f), (int)(neckY + 2f), (int)(neckX + 14f), (int)(neckY - 20f), shadow);
        Raylib.DrawLine((int)(neckX + 4f), (int)(neckY), (int)(neckX + 18f), (int)(neckY - 16f), Color.Black);
        Raylib.DrawLine((int)(neckX + 4f), (int)(neckY + 6f), (int)(neckX + 16f), (int)(neckY - 10f), Color.Black);
    }

    private void DrawBetSection()
    {
        Raylib.DrawText("Kies inzet:", 690, 120, 18, Color.White);
        float startX = ChipX0;
        float startY = ChipY;
        for (int i = 0; i < ChipValues.Length; i++)
        {
            float cx = startX;
            float cy = startY + i * ChipStepY;
            bool selected = i == _selectedChip;
            bool hover = Raylib.CheckCollisionPointCircle(CanvasMouse(), new Vector2(cx, cy), ChipR);
            Color main = ChipColors[i];
            if (selected) Raylib.DrawCircleV(new Vector2(cx, cy), ChipR + 5f, Color.Yellow);
            if (hover && !selected) main = Brighten(main, 40);
            Raylib.DrawCircleV(new Vector2(cx, cy), ChipR, main);
            string lbl = ChipValues[i].ToString();
            int fs = 12;
            int lw = Raylib.MeasureText(lbl, fs);
            Raylib.DrawText(lbl, (int)(cx - lw / 2f), (int)(cy - fs / 2f), fs, Color.White);
        }
    }

    private void DrawControls()
    {
        DrawButton(RaceButtonRect(), "RACE", _betAmount > 0, new Color(160, 40, 40, 255));
        DrawButton(ClearButtonRect(), "WISSEN", _betAmount > 0, new Color(80, 80, 80, 255));
        DrawButton(PlaceBetButtonRect(), "PLAATS CHIP", _state.Balance - _betAmount >= ChipValues[_selectedChip], new Color(30, 120, 180, 255));
        DrawButton(MenuButtonRect(), "← MENU", true, new Color(50, 50, 90, 255));

        DrawBetTypeButton(WinButtonRect(), "WIN", _selectedBetType == BetType.Win);
        DrawBetTypeButton(PlaceButtonRect(), "PLACE", _selectedBetType == BetType.Place);
        DrawBetTypeButton(ShowButtonRect(), "SHOW", _selectedBetType == BetType.Show);
    }

    private void DrawStatus()
    {
        Color c = _statusKind switch
        {
            StatusKind.Win => Color.Lime,
            StatusKind.Lose => Color.Red,
            _ => new Color(220, 220, 220, 255)
        };
        int sw = Raylib.MeasureText(_statusMsg, 16);
        Raylib.DrawText(_statusMsg, Math.Max(10, (W - sw) / 2), H - 40, 16, c);
    }

    private void DrawResultOverlay()
    {
        string title = "RESULTAAT";
        int tw = Raylib.MeasureText(title, 48);
        Raylib.DrawText(title, (W - tw) / 2, 150, 48, Color.Gold);

        for (int rank = 0; rank < HorseCount; rank++)
        {
            int idx = _finishOrder[rank];
            var horse = _horses[idx];
            string line = $"{rank + 1}. {horse.Name} ({horse.Odds:0.0})";
            Raylib.DrawText(line, 240, 220 + rank * 28, 22, Color.White);
        }

        DrawButton(NewRaceButtonRect(), "NIEUWE RACE", true, new Color(30, 100, 30, 255));
    }

    private void DrawButton(Rectangle rect, string label, bool enabled, Color bgColor)
    {
        Vector2 m = CanvasMouse();
        bool hover = enabled && Hit(m, rect);
        Color bg = !enabled ? new Color(65, 65, 75, 255) : hover ? Color.Yellow : bgColor;
        Color fg = !enabled ? Color.DarkGray : hover ? Color.Black : Color.White;
        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 2, Color.White);
        int tw = Raylib.MeasureText(label, 18);
        Raylib.DrawText(label, (int)(rect.X + (rect.Width - tw) / 2), (int)(rect.Y + (rect.Height - 18) / 2), 18, fg);
    }

    private void DrawBetTypeButton(Rectangle rect, string label, bool active)
    {
        Color bg = active ? new Color(220, 180, 30, 255) : new Color(40, 80, 140, 255);
        DrawButton(rect, label, true, bg);
    }

    private Rectangle HorseRect(int index)
    {
        float y = TrackY + index * (TrackH + 10f);
        return new Rectangle(TrackX, y, TrackW, TrackH);
    }

    private static Rectangle MenuButtonRect() => new Rectangle(10f, 10f, 90f, 30f);
    private static Rectangle RaceButtonRect() => new Rectangle(480f, 520f, 110f, 34f);
    private static Rectangle ClearButtonRect() => new Rectangle(600f, 520f, 90f, 34f);
    private static Rectangle PlaceBetButtonRect() => new Rectangle(340f, 520f, 120f, 34f);
    private static Rectangle WinButtonRect() => new Rectangle(100f, 460f, 130f, 34f);
    private static Rectangle PlaceButtonRect() => new Rectangle(240f, 460f, 130f, 34f);
    private static Rectangle ShowButtonRect() => new Rectangle(380f, 460f, 130f, 34f);
    private static Rectangle NewRaceButtonRect() => new Rectangle(320f, 520f, 160f, 40f);

    private Vector2 CanvasMouse()
    {
        Vector2 m = Raylib.GetMousePosition();
        return new Vector2((m.X - _drawOffsetX) / _drawScale, (m.Y - _drawOffsetY) / _drawScale);
    }

    private static bool Hit(Vector2 m, Rectangle r) => Raylib.CheckCollisionPointRec(m, r);

    private static Color Brighten(Color c, int amount) =>
        new Color((byte)Math.Min(255, c.R + amount), (byte)Math.Min(255, c.G + amount), (byte)Math.Min(255, c.B + amount), c.A);

    private static Color Darken(Color c, int amount) =>
        new Color((byte)Math.Max(0, c.R - amount), (byte)Math.Max(0, c.G - amount), (byte)Math.Max(0, c.B - amount), c.A);

    private int _drawOffsetX;
    private int _drawOffsetY;
    private float _drawScale = 1f;

    private sealed class Horse
    {
        public string Name { get; }
        public Color Color { get; }
        public float Weight { get; }
        public float Odds { get; set; }

        public Horse(string name, Color color, float weight)
        {
            Name = name;
            Color = color;
            Weight = weight;
            Odds = 1f;
        }
    }

    private sealed class HorseEntry
    {
        public int Index { get; }
        public float Weight { get; }

        public HorseEntry(int index, float weight)
        {
            Index = index;
            Weight = weight;
        }
    }
}
