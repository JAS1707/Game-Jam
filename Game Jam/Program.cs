using Game_Jam;
using Raylib_cs;
using System.Numerics;

const int W = 800, H = 600;

var globalState  = new GlobalState();
var menu         = new MainMenu(globalState);
SlotMachineGame? slotGame      = null;
BlackjackGame?   bjGame        = null;
RouletteGame?    rouletteGame  = null;
GameChoice       active        = GameChoice.None;

Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
Raylib.InitWindow(W, H, "Game Jam");
Raylib.SetTargetFPS(60);

RenderTexture2D canvas = Raylib.LoadRenderTexture(W, H);

while (!Raylib.WindowShouldClose())
{
    int sw   = Raylib.GetScreenWidth();
    int sh   = Raylib.GetScreenHeight();
    float sc = Math.Min((float)sw / W, (float)sh / H);
    int dw   = (int)(W * sc), dh = (int)(H * sc);
    int ox   = (sw - dw) / 2,  oy = (sh - dh) / 2;

    if (Raylib.IsKeyPressed(KeyboardKey.F11)) ToggleFullscreen(W, H);

    if (active == GameChoice.None)
    {
        menu.SetDrawParams(ox, oy, sc);
        menu.Update();

        if (menu.SelectedGame == GameChoice.Slots)
        {
            slotGame ??= new SlotMachineGame(globalState);
            slotGame.Reset();
            slotGame.SetDrawParams(ox, oy, sc);
            active = GameChoice.Slots;
        }
        else if (menu.SelectedGame == GameChoice.Blackjack)
        {
            bjGame ??= new BlackjackGame(globalState);
            bjGame.Reset();
            bjGame.SetDrawParams(ox, oy, sc);
            active = GameChoice.Blackjack;
        }
        else if (menu.SelectedGame == GameChoice.Roulette)
        {
            rouletteGame ??= new RouletteGame(globalState);
            rouletteGame.Reset();
            rouletteGame.SetDrawParams(ox, oy, sc);
            active = GameChoice.Roulette;
        }
    }
    else if (active == GameChoice.Slots)
    {
        slotGame!.SetDrawParams(ox, oy, sc);
        slotGame.Update();
        if (slotGame.WantsToGoBack) active = GameChoice.None;
    }
    else if (active == GameChoice.Blackjack)
    {
        bjGame!.SetDrawParams(ox, oy, sc);
        bjGame.Update();
        if (bjGame.WantsToGoBack) active = GameChoice.None;
    }
    else if (active == GameChoice.Roulette)
    {
        rouletteGame!.SetDrawParams(ox, oy, sc);
        rouletteGame.Update();
        if (rouletteGame.WantsToGoBack) active = GameChoice.None;
    }

    Raylib.BeginTextureMode(canvas);
    Raylib.ClearBackground(new Color(18, 18, 35, 255));
    if      (active == GameChoice.None)      menu.Draw();
    else if (active == GameChoice.Slots)     slotGame!.Draw();
    else if (active == GameChoice.Blackjack) bjGame!.Draw();
    else if (active == GameChoice.Roulette)  rouletteGame!.Draw();
    Raylib.EndTextureMode();

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.Black);
    Raylib.DrawTexturePro(
        canvas.Texture,
        new Rectangle(0, 0, W, -H),
        new Rectangle(ox, oy, dw, dh),
        Vector2.Zero, 0f, Color.White);
    Raylib.EndDrawing();
}

Raylib.UnloadRenderTexture(canvas);
Raylib.CloseWindow();

static void ToggleFullscreen(int w, int h)
{
    if (Raylib.IsWindowFullscreen())
    {
        Raylib.ToggleFullscreen();
        Raylib.SetWindowSize(w, h);
    }
    else
    {
        int mon = Raylib.GetCurrentMonitor();
        Raylib.SetWindowSize(Raylib.GetMonitorWidth(mon), Raylib.GetMonitorHeight(mon));
        Raylib.ToggleFullscreen();
    }
}