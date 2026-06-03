using System;
using Raylib_cs;

namespace Game_Jam;

/// <summary>
/// Genereert en beheert alle spelgeluiden procedureel — geen externe bestanden vereist.
/// Initialiseer pas ná <c>Raylib.InitAudioDevice()</c>.
/// </summary>
public sealed class SoundBank : IDisposable
{
    // ── Slot Machine ─────────────────────────────────────────────────────────
    /// <summary>Korte ruis-tik die herhaald wordt tijdens het draaien van de wielen.</summary>
    public Sound SpinTick   { get; }
    /// <summary>Vijf oplopende pling-noten (C5 t/m C6), één per wiel dat tot stilstand komt.</summary>
    public Sound[] ReelStop  { get; } = new Sound[5];
    /// <summary>Feestelijk arpeggio bij een jackpot.</summary>
    public Sound Jackpot    { get; }

    // ── Gedeeld ───────────────────────────────────────────────────────────────
    /// <summary>Kort stijgend klinkend jingle bij een overwinning.</summary>
    public Sound WinChime   { get; }
    /// <summary>Aflopende tonen bij verlies.</summary>
    public Sound LoseTone   { get; }
    /// <summary>Kort klik-geluid bij het plaatsen van een chip.</summary>
    public Sound ChipClick  { get; }

    // ── Blackjack ─────────────────────────────────────────────────────────────
    /// <summary>Schuivend geluid van een kaart die uitgedeeld wordt.</summary>
    public Sound CardDeal   { get; }

    // ── Greed ────────────────────────────────────────────────────────────────
    /// <summary>Doffe klik bij het omdraaien van een steen.</summary>
    public Sound StoneFlip  { get; }
    /// <summary>Opgaande toon bij een gouden munt.</summary>
    public Sound CoinReveal { get; }
    /// <summary>Sub-bass dreun + ruis bij een bom.</summary>
    public Sound BombBlast  { get; }
    /// <summary>Stijgend arpeggio bij uitbetalen.</summary>
    public Sound CashOut    { get; }

    // ── Roulette ─────────────────────────────────────────────────────────────
    /// <summary>Tik van de bal langs de vakjes tijdens het draaien.</summary>
    public Sound BallTick   { get; }
    /// <summary>Dalend signaal wanneer de bal tot stilstand komt.</summary>
    public Sound BallLand   { get; }

    // ─────────────────────────────────────────────────────────────────────────

    public SoundBank()
    {
        const int SR = 44100;

        SpinTick    = MakeNoise(SR, 0.040f, 0.20f);
        ReelStop[0] = MakePling(SR, 523f,  0.24f);   // C5
        ReelStop[1] = MakePling(SR, 659f,  0.24f);   // E5
        ReelStop[2] = MakePling(SR, 784f,  0.24f);   // G5
        ReelStop[3] = MakePling(SR, 988f,  0.24f);   // B5
        ReelStop[4] = MakePling(SR, 1047f, 0.24f);   // C6
        Jackpot     = MakeArpeggio(SR, [523f, 659f, 784f, 1047f, 1319f], 0.095f, 0.52f);

        WinChime    = MakeArpeggio(SR, [523f, 659f, 784f, 1047f], 0.085f, 0.50f);
        LoseTone    = MakeArpeggio(SR, [330f, 277f, 220f],        0.140f, 0.42f);
        ChipClick   = MakeTone(SR, 1100f, 0.028f, 0.28f);

        CardDeal    = MakeNoise(SR, 0.038f, 0.24f, highPass: true);

        StoneFlip   = MakeNoise(SR, 0.045f, 0.20f);
        CoinReveal  = MakeChirp(SR, 440f, 880f, 0.18f, 0.46f);
        BombBlast   = MakeBomb(SR);
        CashOut     = MakeArpeggio(SR, [392f, 523f, 659f, 784f, 1047f], 0.075f, 0.48f);

        BallTick    = MakeNoise(SR, 0.018f, 0.22f, highPass: true);
        BallLand    = MakeChirp(SR, 660f, 330f, 0.14f, 0.38f);
    }

    /// <summary>Geeft de pling-Sound terug die bij het gegeven wiel-index hoort (0-4).</summary>
    public Sound GetReelStop(int i) => ReelStop[Math.Clamp(i, 0, 4)];

    /// <inheritdoc/>
    public void Dispose()
    {
        Raylib.UnloadSound(SpinTick);
        foreach (var s in ReelStop) Raylib.UnloadSound(s);
        Raylib.UnloadSound(Jackpot);
        Raylib.UnloadSound(WinChime);
        Raylib.UnloadSound(LoseTone);
        Raylib.UnloadSound(ChipClick);
        Raylib.UnloadSound(CardDeal);
        Raylib.UnloadSound(StoneFlip);
        Raylib.UnloadSound(CoinReveal);
        Raylib.UnloadSound(BombBlast);
        Raylib.UnloadSound(CashOut);
        Raylib.UnloadSound(BallTick);
        Raylib.UnloadSound(BallLand);
    }

    // ── Geluid-generatoren ────────────────────────────────────────────────────

    /// <summary>Sinus met harmonischen en exponentieel vervaag — helder klokgeluid.</summary>
    private static unsafe Sound MakePling(int sr, float freq, float dur)
    {
        int n = (int)(sr * dur);
        short[] data = new short[n];
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / sr;
            float env = MathF.Exp(-9f * t);
            float s   = MathF.Sin(2f * MathF.PI * freq * t)
                      + 0.38f * MathF.Sin(2f * MathF.PI * freq * 2f * t)
                      + 0.12f * MathF.Sin(2f * MathF.PI * freq * 3f * t);
            data[i] = (short)(0.44f * 32767f * s * env);
        }
        return Load(sr, data);
    }

    /// <summary>Zuivere sinus met lineair vervaag — geschikt voor korte UI-klikken.</summary>
    private static unsafe Sound MakeTone(int sr, float freq, float dur, float vol)
    {
        int n = (int)(sr * dur);
        short[] data = new short[n];
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / sr;
            float env = 1f - (float)i / n;
            data[i] = (short)(vol * 32767f * MathF.Sin(2f * MathF.PI * freq * t) * env);
        }
        return Load(sr, data);
    }

    /// <summary>
    /// Wit ruis met lineair vervaag.
    /// <paramref name="highPass"/> knipt lage frequenties weg voor een scherper, knapperiger geluid.
    /// </summary>
    private static unsafe Sound MakeNoise(int sr, float dur, float vol, bool highPass = false)
    {
        var rng  = new Random(31415);
        int n    = (int)(sr * dur);
        short[] data = new short[n];
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float env = 1f - (float)i / n;
            float s   = (float)(rng.NextDouble() * 2.0 - 1.0);
            if (highPass) { float hp = s - prev * 0.85f; prev = s; s = hp * 0.65f; }
            data[i] = (short)(vol * 32767f * s * env);
        }
        return Load(sr, data);
    }

    /// <summary>Frequentie-sweep van <paramref name="f0"/> naar <paramref name="f1"/> — rijzend of dalend pieptoon.</summary>
    private static unsafe Sound MakeChirp(int sr, float f0, float f1, float dur, float vol)
    {
        int n = (int)(sr * dur);
        short[] data = new short[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t    = (float)i / n;
            float freq = f0 + (f1 - f0) * t;
            float env  = MathF.Sin(MathF.PI * t);   // driehoek-envelop
            phase += 2f * MathF.PI * freq / sr;
            data[i] = (short)(vol * 32767f * MathF.Sin(phase) * env);
        }
        return Load(sr, data);
    }

    /// <summary>Snelle opeenvolging van afzonderlijke tonen (muzikaal arpeggio).</summary>
    private static unsafe Sound MakeArpeggio(int sr, float[] freqs, float noteLen, float vol)
    {
        int noteN  = (int)(sr * noteLen);
        int totalN = noteN * freqs.Length;
        short[] data = new short[totalN];
        for (int ni = 0; ni < freqs.Length; ni++)
        {
            float freq = freqs[ni];
            for (int i = 0; i < noteN; i++)
            {
                float t   = (float)i / sr;
                float env = 1f - (float)i / noteN;
                data[ni * noteN + i] = (short)(vol * 32767f
                    * MathF.Sin(2f * MathF.PI * freq * t) * env);
            }
        }
        return Load(sr, data);
    }

    /// <summary>Sub-bass dreun gecombineerd met wit ruis — simuleert een explosie.</summary>
    private static unsafe Sound MakeBomb(int sr)
    {
        var rng = new Random(27182);
        int n   = (int)(sr * 0.55f);
        short[] data = new short[n];
        for (int i = 0; i < n; i++)
        {
            float t      = (float)i / sr;
            float env    = MathF.Exp(-4.5f * t);
            float rumble = MathF.Sin(2f * MathF.PI * 58f * t) * 0.55f;
            float noise  = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.42f;
            data[i] = (short)(0.68f * 32767f * (rumble + noise) * env);
        }
        return Load(sr, data);
    }

    /// <summary>Laadt een short[]-buffer als Raylib <see cref="Sound"/> (mono, 16-bit).</summary>
    private static unsafe Sound Load(int sr, short[] data)
    {
        fixed (short* p = data)
        {
            var w = new Wave
            {
                SampleCount = (uint)data.Length,
                SampleRate  = (uint)sr,
                SampleSize  = 16,
                Channels    = 1,
                Data        = p,
            };
            return Raylib.LoadSoundFromWave(w);
        }
    }
}
