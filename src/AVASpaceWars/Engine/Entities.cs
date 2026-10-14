namespace AVASpaceWars.Engine;

/// <summary>One ship's controls: turn (−1 anticlockwise, +1 clockwise), thrust, fire, hyperspace.</summary>
public readonly record struct Controls(int Turn, bool Thrust = false, bool Fire = false, bool Hyper = false);

/// <summary>The arena: the whole 1024×768 screen below the score bar, with the sun in the middle.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The bottom of the score bar: the arena starts here.</summary>
    public const double Top = 64;
    public const double CX = Width / 2, CY = (Top + Height) / 2;
}

/// <summary>The rules of space for a round: how hard the sun pulls, whether it can be seen, and what the edges do.</summary>
public sealed record Arena(string Name, string Note, double Gravity, bool SunShows, bool Wrap)
{
    public bool HasSun => Gravity > 0;

    /// <summary>The arenas, round by round; after the last they come round again.</summary>
    public static readonly Arena[] All =
    [
        new("THE SUN", "MIND ITS PULL", 3.6e6, true, true),
        new("OPEN SPACE", "NO SUN AT ALL", 0, false, true),
        new("THE BOX", "THE EDGES BOUNCE", 3.6e6, true, false),
        new("HEAVY STAR", "IT PULLS TWICE AS HARD", 7.2e6, true, true),
        new("BLACK HOLE", "YOU CAN'T SEE IT, BUT IT'S THERE", 4.4e6, false, true),
    ];

    public static Arena ForRound(int round) => All[(round - 1) % All.Length];
}

public sealed class Ship
{
    public const double Radius = 13;
    /// <summary>0 is the needle (the player, on the left), 1 the wedge.</summary>
    public int Player;
    public double X, Y, Vx, Vy;
    /// <summary>The heading in radians: 0 points right, and it turns clockwise as it grows.</summary>
    public double A;
    /// <summary>Fuel, from 1 down to 0: thrust and hyperspace burn it.</summary>
    public double Fuel = 1;
    public int Torpedoes;
    public bool Alive = true;
    /// <summary>Time left away in hyperspace; while it is above zero the ship is nowhere.</summary>
    public double Away;
    public int HyperJumps;
    public double FireWait;
    public bool Thrusting;
    public int Kills;
    internal bool HyperHeld;

    public bool Needle => Player == 0;
    public bool InPlay => Alive && Away <= 0;
    public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy);
}

public sealed class Torpedo
{
    public double X, Y, Vx, Vy, Age;
    public int Owner;
    public bool Dead;
}

/// <summary>A piece of a wrecked ship: one of its lines, tumbling away.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, A, Va, Length, Age, Life = 1.6;
    public uint Colour;
}

public enum EffectKind { Ring, Flash, Score }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    /// <summary>A ring that closes in (going into hyperspace) rather than opens out.</summary>
    public bool Inward;
    public bool Done => Age >= Life;
}
