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
        if (Hit(m, SlotsButtonRect()))     SelectedGame = GameChoice.Slots;
        if (Hit(m, BlackjackButtonRect())) SelectedGame = GameChoice.Blackjack;
        if (Hit(m, RouletteButtonRect()))  SelectedGame = GameChoice.Roulette;
        if (Hit(m, GreedButtonRect()))     SelectedGame = GameChoice.Greed;
    }

    public void Draw()
    {
        const string title = "GAME  JAM";
        int tw = Raylib.MeasureText(title, 72);
        Raylib.DrawText(title, (W - tw) / 2, 80, 72, Color.Gold);
        Raylib.DrawRectangle(80, 170, W - 160, 2, new Color(80, 70, 20, 255));

        string balTxt = $"Saldo:  {_state.Balance} munten";
        int bw = Raylib.MeasureText(balTxt, 22);
        Raylib.DrawText(balTxt, (W - bw) / 2, 196, 22, Color.White);

        const string sub = "Kies een spel:";
        int cw = Raylib.MeasureText(sub, 22);
        Raylib.DrawText(sub, (W - cw) / 2, 248, 22, new Color(160, 160, 160, 255));

        DrawMenuButton(SlotsButtonRect(),     "SLOT MACHINE", new Color(30, 100, 30,  255));
        DrawMenuButton(BlackjackButtonRect(), "BLACKJACK",    new Color(80, 20,  20,  255));
        DrawMenuButton(RouletteButtonRect(),  "ROULETTE",     new Color(20, 40,  110, 255));
        DrawMenuButton(GreedButtonRect(),     "GREED",        new Color(130, 90, 10,  255));

        const string hint = "F11: volledig scherm";
        int hw = Raylib.MeasureText(hint, 12);
        Raylib.DrawText(hint, W - hw - 8, H - 18, 12, new Color(55, 55, 55, 255));
    }

    private void DrawMenuButton(Rectangle rect, string label, Color bgColor)
    {
        Vector2 m  = CanvasMouse();
        bool hover = Hit(m, rect);

        Color bg = hover ? Color.Yellow : bgColor;
        Color fg = hover ? Color.Black  : Color.White;

        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 2, Color.White);

        int lw = Raylib.MeasureText(label, 24);
        Raylib.DrawText(label,
            (int)(rect.X + (rect.Width  - lw) / 2),
            (int)(rect.Y + (rect.Height - 24) / 2),
            24, fg);
    }

    private static Rectangle SlotsButtonRect()     => new Rectangle((W - 300) / 2f, 270, 300, 52);
    private static Rectangle BlackjackButtonRect() => new Rectangle((W - 300) / 2f, 332, 300, 52);
    private static Rectangle RouletteButtonRect()  => new Rectangle((W - 300) / 2f, 394, 300, 52);
    private static Rectangle GreedButtonRect()     => new Rectangle((W - 300) / 2f, 456, 300, 52);

    private Vector2 CanvasMouse()
    {
        Vector2 m = Raylib.GetMousePosition();
        return new Vector2((m.X - _drawOffsetX) / _drawScale,
                           (m.Y - _drawOffsetY) / _drawScale);
    }

    private static bool Hit(Vector2 m, Rectangle r) => Raylib.CheckCollisionPointRec(m, r);
}
