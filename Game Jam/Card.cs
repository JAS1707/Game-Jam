namespace Game_Jam;

public enum Suit { Hearts, Diamonds, Clubs, Spades }

public enum Rank
{
    Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten,
    Jack, Queen, King, Ace
}

public sealed class Card
{
    public Suit Suit     { get; }
    public Rank Rank     { get; }
    public bool FaceDown { get; set; }

    public bool IsRed => Suit is Suit.Hearts or Suit.Diamonds;

    public int PointValue => Rank switch
    {
        Rank.Jack or Rank.Queen or Rank.King => 10,
        Rank.Ace => 11,
        _        => (int)Rank
    };

    public string RankText => Rank switch
    {
        Rank.Ace   => "A",
        Rank.Jack  => "J",
        Rank.Queen => "Q",
        Rank.King  => "K",
        _          => ((int)Rank).ToString()
    };

    public string SuitChar => Suit switch
    {
        Suit.Hearts   => "♥",
        Suit.Diamonds => "♦",
        Suit.Clubs    => "♣",
        Suit.Spades   => "♠",
        _             => "?"
    };

    public string SuitFallback => Suit switch
    {
        Suit.Hearts   => "H",
        Suit.Diamonds => "D",
        Suit.Clubs    => "C",
        Suit.Spades   => "S",
        _             => "?"
    };

    public Card(Suit suit, Rank rank, bool faceDown = false)
    {
        Suit     = suit;
        Rank     = rank;
        FaceDown = faceDown;
    }
}
