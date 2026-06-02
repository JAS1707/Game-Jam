using Game_Jam;
using Raylib_cs;
using System.Numerics;

const int W = 800, H = 600;

var globalState = new GlobalState();
var menu        = new MainMenu(globalState);
SlotMachineGame? slotGame = null;
bool inMenu = true;

Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
Raylib.InitWindow(W, H, "Game Jam");
Raylib.SetTargetFPS(60);

RenderTexture2D canvas = Raylib.LoadRenderTexture(W, H);

while (!Raylib.WindowShouldClose())
{
    int sw   = Raylib.GetScreenWidth();
    int sh   = Raylib.GetScreenHeight();
    float sc = Math.Min((float)sw / W, (float)sh / H);
    int dw   = (int)(W * sc);
    int dh   = (int)(H * sc);
    int ox   = (sw - dw) / 2;
    int oy   = (sh - dh) / 2;

    if (Raylib.IsKeyPressed(KeyboardKey.F11))
        ToggleFullscreen(W, H);

    if (inMenu)
    {
        menu.SetDrawParams(ox, oy, sc);
        menu.Update();
        if (menu.SelectedGame == GameChoice.Slots)
        {
            slotGame ??= new SlotMachineGame(globalState);
            slotGame.Reset();
            slotGame.SetDrawParams(ox, oy, sc);
            inMenu = false;
        }
    }
    else
    {
        slotGame!.SetDrawParams(ox, oy, sc);
        slotGame.Update();
        if (slotGame.WantsToGoBack)
            inMenu = true;
    }

    Raylib.BeginTextureMode(canvas);
    Raylib.ClearBackground(new Color(18, 18, 35, 255));
    if (inMenu) menu.Draw();
    else        slotGame!.Draw();
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
