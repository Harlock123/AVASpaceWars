namespace AVASpaceWars.Engine;

/// <summary>
/// A computer pilot. In order of what matters most, it keeps out of the sun, gets out of the way of
/// torpedoes (with a jump to hyperspace when one is about to hit, if it is sharp enough), keeps its
/// speed in hand, and otherwise turns to lead the other ship and fires when the shot lines up.
/// <see cref="Skill"/> (0 to 1) sets how quickly it reacts, how close its aim has to be, and how
/// often it dodges.
/// </summary>
public sealed class DuelPilot(int seed)
{
    public double Skill { get; set; } = 0.5;
    /// <summary>What it is doing, for the tests and for tuning.</summary>
    public string Doing { get; private set; } = "";

    private readonly Random _rng = new(seed);
    private double _rethink, _aimError;
    private bool _hyperPressed;

    private readonly Queue<(double X, double Y, double Vx, double Vy)> _seen = new();
    private double _fireHold;

    /// <summary>How long it takes to notice things: a quarter of a second for a novice, nothing for the sharpest.</summary>
    private double Reaction => (1 - Skill) * 0.3;

    /// <summary>The controls for this step. A novice also waits a little after each shot before firing again.</summary>
    public Controls Decide(GameEngine g, int me)
    {
        // It sees the other ship where it was a reaction time ago.
        var e = g.Ships[1 - me];
        _seen.Enqueue((e.X, e.Y, e.Vx, e.Vy));
        while (_seen.Count > (int)Math.Round(Reaction * 60) + 1) _seen.Dequeue();
        var now = Think(g, me);
        _fireHold = Math.Max(0, _fireHold - Field.Step);
        if (now.Fire)
        {
            if (_fireHold > 0) now = now with { Fire = false };
            else _fireHold = (1 - Skill) * 0.7;
        }
        return now;
    }

    private Controls Think(GameEngine g, int me)
    {
        var s = g.Ships[me];
        var e = g.Ships[1 - me];
        if (!s.InPlay || g.Phase is not (Phase.Duel or Phase.Exploded)) { _hyperPressed = false; return default; }

        // Now and then it settles on a new small error in its aim: the less skilled, the bigger.
        _rethink -= Field.Step;
        if (_rethink <= 0)
        {
            _rethink = 0.35 + (1 - Skill) * 0.4;
            _aimError = (_rng.NextDouble() - 0.5) * (1 - Skill) * 0.5;
        }

        // 1. The sun: if coasting would take it too close, turn outward along its course and burn.
        if (g.Arena.HasSun && SunDanger(g, s) is { } escape)
        {
            Doing = "sun";
            return Steer(s, escape, thrust: true);
        }

        // 2. Torpedoes coming its way.
        foreach (var t in g.Torpedoes)
        {
            if ((t.Owner == me && t.Age < 0.3) || t.Age < Reaction) continue;
            var (dx, dy) = g.Towards(s.X, s.Y, t.X, t.Y);
            double rvx = t.Vx - s.Vx, rvy = t.Vy - s.Vy, v2 = rvx * rvx + rvy * rvy;
            if (v2 < 1) continue;
            double when = -(dx * rvx + dy * rvy) / v2;
            if (when is < 0 or > 0.7) continue;
            double mx = dx + rvx * when, my = dy + rvy * when;
            if (mx * mx + my * my > 26 * 26) continue;
            if (_rng.NextDouble() > 0.1 + 0.85 * Skill * Skill) continue;
            // Right on top of it: a sharp pilot jumps.
            if (when < 0.22 && Skill > 0.55 && s.Fuel > GameEngine.HyperFuel && GameEngine.JumpRisk(s.HyperJumps) == 0 && !_hyperPressed)
            {
                Doing = "jump";
                _hyperPressed = true;
                return new Controls(0, Hyper: true);
            }
            // Otherwise burn sideways, across the torpedo's path, away from where it will pass.
            double side = mx * rvy - my * rvx > 0 ? 1 : -1;
            double away = Math.Atan2(rvy, rvx) + side * Math.PI / 2;
            Doing = "dodge";
            return Steer(s, away, thrust: s.Fuel > 0.05);
        }
        _hyperPressed = false;

        var seen = _seen.Count > 0 ? _seen.Peek() : (e.X, e.Y, e.Vx, e.Vy);
        var (ex, ey) = g.Towards(s.X, s.Y, seen.X, seen.Y);
        double dist = Math.Sqrt(ex * ex + ey * ey);

        // 3. Too fast: turn tail to its course and brake, unless it is lined up on a shot.
        double speed = s.Speed;
        if (speed > 230 + Skill * 60 && s.Fuel > 0.2 && Math.Abs(Angle.Diff(Math.Atan2(ey, ex), s.A)) > 0.3)
        {
            Doing = "brake";
            return Steer(s, Math.Atan2(-s.Vy, -s.Vx), thrust: true);
        }

        // 4. Hunt: aim where the other ship will be when a torpedo gets there.
        if (!e.InPlay)
        {
            Doing = "wait";
            return Steer(s, s.A, thrust: false);
        }
        double wx = seen.Vx - s.Vx, wy = seen.Vy - s.Vy;
        // A novice leads its target only partly.
        double lead = Intercept(ex, ey, wx, wy, GameEngine.TorpedoSpeed) * (0.35 + 0.65 * Skill);
        double ax = ex + wx * lead, ay = ey + wy * lead;
        double aim = Math.Atan2(ay, ax) + _aimError;
        double err = Angle.Diff(aim, s.A);
        bool inRange = Math.Sqrt(ax * ax + ay * ay) < GameEngine.TorpedoSpeed * GameEngine.TorpedoLife * 0.85;
        bool fire = inRange && Math.Abs(err) < 0.07 && s.Torpedoes > 0;
        // Close in when far off and slow; keep a little way off when close.
        bool thrust = s.Fuel > 0.25 && Math.Abs(err) < 0.4 && ((dist > 380 && speed < 140) || speed < 40);
        Doing = fire ? "fire" : "aim";
        var c = Steer(s, aim, thrust);
        return c with { Fire = fire };
    }

    /// <summary>If coasting for the next moment would take the ship into the sun's reach, the heading that gets it clear.</summary>
    private static double? SunDanger(GameEngine g, Ship s)
    {
        double x = s.X, y = s.Y, vx = s.Vx, vy = s.Vy;
        const double dt = 0.05;
        double reach = GameEngine.SunKill + 34 + g.Arena.Gravity / 2.5e5;
        for (int i = 0; i < 30; i++)
        {
            var (gx, gy) = g.Pull(x, y);
            vx += gx * dt;
            vy += gy * dt;
            x += vx * dt;
            y += vy * dt;
            double dx = x - Field.CX, dy = y - Field.CY;
            if (dx * dx + dy * dy < reach * reach)
            {
                // Out from the sun, and along the way it is already going.
                double rx = s.X - Field.CX, ry = s.Y - Field.CY, r = Math.Max(1, Math.Sqrt(rx * rx + ry * ry));
                double sp = Math.Max(1, s.Speed);
                double ox = rx / r * 0.8 + s.Vx / sp * 0.6, oy = ry / r * 0.8 + s.Vy / sp * 0.6;
                return Math.Atan2(oy, ox);
            }
        }
        return null;
    }

    /// <summary>The time for a shot at <paramref name="speed"/> to meet a target at (dx, dy) moving at (wx, wy).</summary>
    private static double Intercept(double dx, double dy, double wx, double wy, double speed)
    {
        double a = wx * wx + wy * wy - speed * speed, b = 2 * (dx * wx + dy * wy), c = dx * dx + dy * dy;
        double disc = b * b - 4 * a * c;
        if (disc < 0 || Math.Abs(a) < 1e-6) return Math.Sqrt(c) / speed;
        double sq = Math.Sqrt(disc);
        double t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a);
        double t = Math.Min(t1, t2) > 0 ? Math.Min(t1, t2) : Math.Max(t1, t2);
        return t > 0 ? Math.Min(t, 2) : Math.Sqrt(c) / speed;
    }

    private static Controls Steer(Ship s, double heading, bool thrust)
    {
        double err = Angle.Diff(heading, s.A);
        int turn = Math.Abs(err) < 0.04 ? 0 : Math.Sign(err);
        return new Controls(turn, thrust && Math.Abs(err) < 0.6);
    }
}

public static class Angle
{
    /// <summary>The turn from <paramref name="from"/> to <paramref name="to"/>, between −π and π.</summary>
    public static double Diff(double to, double from)
    {
        double d = (to - from) % (2 * Math.PI);
        if (d > Math.PI) d -= 2 * Math.PI;
        if (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }
}
