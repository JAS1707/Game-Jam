namespace Game_Jam;

public class GlobalState
{
    public const int StartingBalance = 100;
    public int Balance { get; set; } = StartingBalance;

    /// <summary>Gedeelde geluidsbank — wordt ingesteld na <c>Raylib.InitAudioDevice()</c>.</summary>
    public SoundBank? Sounds { get; set; }
}
