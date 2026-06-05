using System.Numerics;
using Raylib_cs;

namespace Game_Jam;

public class MainMenu
{
    private const int W = 800, H = 600;

    private readonly GlobalState _state;
    private int   _drawOffsetX;
    private int   _drawOffsetY;
    private float _drawScale = 1f;

    public GameChoice SelectedGame { get; private set; } = GameChoice.None;

    public MainMenu(GlobalState state) => _state = state;

    public void SetDrawParams(int ox, int oy, float scale)
    {
        _drawOffsetX = ox;
        _drawOffsetY = oy;
        _drawScale   = scale;
    }

    public void Update()
    {
        SelectedGame = GameChoice.None;
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        Vector2 m = CanvasMouse();
        if (Hit(m, SlotsButtonRect()))       SelectedGame = GameChoice.Slots;
        if (Hit(m, BlackjackButtonRect()))   SelectedGame = GameChoice.Blackjack;
        if (Hit(m, RouletteButtonRect()))    SelectedGame = GameChoice.Roulette;
        if (Hit(m, HorseRacingButtonRect())) SelectedGame = GameChoice.HorseRacing;
        if (Hit(m, GreedButtonRect()))        SelectedGame = GameChoice.Greed;
    }

    public void Draw()
    {
        // ── Titel ─────────────────────────────────────────────────────────────
        const string title = "GAME  JAM";
        int tw = Raylib.MeasureText(title, 72);
        Raylib.DrawText(title, (W - tw) / 2, 68, 72, AppTheme.AccentGold);
        Raylib.DrawRectangle(60, 152, W - 120, 2, AppTheme.SepAccent);

        // ── Saldo ─────────────────────────────────────────────────────────────
        string balTxt = $"Saldo:  {_state.Balance}  munten";
        int bw = Raylib.MeasureText(balTxt, 22);
        Raylib.DrawText(balTxt, (W - bw) / 2, 168, 22, AppTheme.TextPrimary);

        // ── Subtitel ──────────────────────────────────────────────────────────
        const string sub = "Kies een spel:";
        int sw = Raylib.MeasureText(sub, 18);
        Raylib.DrawText(sub, (W - sw) / 2, 212, 18, AppTheme.TextSecondary);

        DrawMenuButton(SlotsButtonRect(),       "SLOT MACHINE",   new Color(30, 100, 30, 255));
        DrawMenuButton(BlackjackButtonRect(),   "BLACKJACK",      new Color(80, 20,  20, 255));
        DrawMenuButton(RouletteButtonRect(),    "ROULETTE",       new Color(20, 40, 110, 255));
        DrawMenuButton(HorseRacingButtonRect(), "HORSE RACING",   new Color(110, 60, 10, 255));
        DrawMenuButton(GreedButtonRect(),       "GREED",          new Color(120, 82,  8,  255));

        // ── Hints onderaan ────────────────────────────────────────────────────
        const string hint = "F11: volledig scherm  |  Esc: menu";
        int hw = Raylib.MeasureText(hint, 14);
        Raylib.DrawText(hint, (W - hw) / 2, H - 22, 14, AppTheme.TextHint);
    }

    private void DrawMenuButton(Rectangle rect, string label, Color bgColor)
    {
        Vector2 m  = CanvasMouse();
        bool hover = Hit(m, rect);

        Color bg   = hover ? AppTheme.BtnHover : bgColor;
        Color fg   = hover ? Color.Black       : AppTheme.TextPrimary;
        Color bord = hover ? AppTheme.BtnHover : AppTheme.BorderActive;

        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 2, bord);

        int lw = Raylib.MeasureText(label, 22);
        Raylib.DrawText(label,
            (int)(rect.X + (rect.Width  - lw) / 2),
            (int)(rect.Y + (rect.Height - 22) / 2),
            22, fg);
    }

    private static Rectangle SlotsButtonRect()       => new Rectangle((W - 300) / 2f, 250, 300, 55);
    private static Rectangle BlackjackButtonRect()   => new Rectangle((W - 300) / 2f, 315, 300, 55);
    private static Rectangle RouletteButtonRect()    => new Rectangle((W - 300) / 2f, 380, 300, 55);
    private static Rectangle HorseRacingButtonRect() => new Rectangle((W - 300) / 2f, 445, 300, 55);
    private static Rectangle GreedButtonRect()        => new Rectangle((W - 300) / 2f, 510, 300, 55);

    private Vector2 CanvasMouse()
    {
        Vector2 m = Raylib.GetMousePosition();
        return new Vector2((m.X - _drawOffsetX) / _drawScale,
                           (m.Y - _drawOffsetY) / _drawScale);
    }

    private static bool Hit(Vector2 m, Rectangle r) => Raylib.CheckCollisionPointRec(m, r);
}
