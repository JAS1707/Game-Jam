namespace Game_Jam;

/// <summary>
/// Kern van de slotmachine: beheert de drie wielen en de uitbetalingslogica.
/// Visuele animatie wordt afgehandeld door SlotMachineUI.
/// </summary>
public class SlotMachine
{
    private const double TwoOfAKindMultiplier = 1.5;

    private readonly Wheel[] _wheels = [new Wheel(), new Wheel(), new Wheel()];

    /// <summary>De huidige symbolen op de drie wielen (na laatste spin).</summary>
    public Symbol[] CurrentSymbols => _wheels.Select(w => w.CurrentSymbol).ToArray();

    /// <summary>
    /// Draait alle drie de wielen en retourneert de uitbetaling.
    /// </summary>
    /// <param name="bet">De ingezette hoeveelheid munten.</param>
    /// <returns>Het gewonnen bedrag (0 als verlies).</returns>
    public int SpinAndCalculate(int bet)
    {
        foreach (var wheel in _wheels)
            wheel.Spin();
        return CalculatePayout(bet);
    }

    /// <summary>
    /// Berekent de uitbetaling:
    /// 3× hetzelfde → inzet × symbool-multiplier;
    /// 2× hetzelfde → inzet × 1,5; geen match → 0.
    /// </summary>
    private int CalculatePayout(int bet)
    {
        var symbols = CurrentSymbols;

        if (symbols[0] == symbols[1] && symbols[1] == symbols[2])
            return bet * symbols[0].Multiplier;

        bool twoMatch = symbols[0] == symbols[1]
                     || symbols[1] == symbols[2]
                     || symbols[0] == symbols[2];

        return twoMatch ? (int)(bet * TwoOfAKindMultiplier) : 0;
    }
}
