namespace AVASpaceWars.Engine;

public sealed partial class GameEngine
{
    public const double TurnRate = 4.2, ThrustPower = 250, MaxSpeed = 420, FuelBurn = 0.11;
    public const double TorpedoSpeed = 420, TorpedoLife = 1.5, FireDelay = 0.22;
    public const int TorpedoLoad = 20, MaxInFlight = 5;
    public const double HyperTime = 0.9, HyperFuel = 0.12, HyperRisk = 0.15;
    /// <summary>How close to the sun's centre a ship can come before it is pulled in and burnt up.</summary>
    public const double SunKill = 22;
    public const double StartSeconds = 2.4, ExplodedSeconds = 2.2, RoundOverSeconds = 3.2;
    public const int KillsToWinRound = 3, KillsToWinDuel = 7;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 30000;

    public bool TwoPlayer { get; private set; }
    public int Round { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Arena Arena => TwoPlayer ? Arena.All[0] : Arena.ForRound(Round);
    public int KillsToWin => TwoPlayer ? KillsToWinDuel : KillsToWinRound;
    public Ship[] Ships { get; } = [new Ship { Player = 0 }, new Ship { Player = 1 }];
    public List<Torpedo> Torpedoes { get; } = [];
    public List<Debris> Debris { get; } = [];
    /// <summary>The computer pilots: the wedge's in a one-player game, and both in the demonstration.</summary>
    public DuelPilot[] Pilots { get; } = [new DuelPilot(11), new DuelPilot(23)];
    /// <summary>Who won the last round (0 the needle, 1 the wedge).</summary>
    public int RoundWinner { get; private set; } = -1;
    /// <summary>Why the last ship went: "HIT", "INTO THE SUN", "HYPERSPACE FAILED", "COLLISION".</summary>
    public string Cause { get; private set; } = "";

    private int _nextExtra;
    private double _thrustSound;

    private void NewGame(bool twoPlayer)
    {
        _rng = new Random(_seed + (twoPlayer ? 2 : 1));
        TwoPlayer = twoPlayer;
        Score = 0;
        Lives = 3;
        Round = 1;
        _nextExtra = FirstExtraLife;
        Torpedoes.Clear();
        Debris.Clear();
        foreach (var s in Ships) s.Kills = 0;
        Serve();
        SetPhase(Phase.RoundStart);
    }

    /// <summary>A round against the computer, or the two-player duel.</summary>
    private void BeginRound()
    {
        foreach (var s in Ships) s.Kills = 0;
        RoundWinner = -1;
        // The computer gets sharper round by round; in the demonstration both are fair.
        Pilots[1].Skill = State == GameState.Attract ? 0.7 : Math.Min(1, 0.32 + 0.11 * (Round - 1));
        Pilots[0].Skill = 0.7;
        Serve();
        SetPhase(Phase.RoundStart);
        if (TwoPlayer) Say("THE DUEL", $"FIRST TO {KillsToWin} WINS", StartSeconds);
        else Say($"ROUND {Round} - {Arena.Name}", $"{Arena.Note} - FIRST TO {KillsToWin}", StartSeconds);
        Play(Sfx.RoundStart);
    }

    /// <summary>Both ships back in their corners with full tanks and racks; the sky is cleared.</summary>
    private void Serve()
    {
        Torpedoes.Clear();
        var (a, b) = (Ships[0], Ships[1]);
        Place(a, Field.CX - 360, Field.Top + 120, Math.PI / 2);
        Place(b, Field.CX + 360, Field.Height - 120, -Math.PI / 2);
    }

    private static void Place(Ship s, double x, double y, double heading)
    {
        s.X = x;
        s.Y = y;
        s.Vx = s.Vy = 0;
        s.A = heading;
        s.Fuel = 1;
        s.Torpedoes = TorpedoLoad;
        s.Alive = true;
        s.Away = 0;
        s.HyperJumps = 0;
        s.FireWait = 0;
        s.Thrusting = false;
    }

    /// <summary>Behind the title and the tables: just the wreckage settling.</summary>
    private void Drift(double dt)
    {
        foreach (var d in Debris) MoveDebris(d, dt);
        Debris.RemoveAll(d => d.Age > d.Life);
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls one, Controls two)
    {
        PhaseTime += dt;
        Drift(dt);
        switch (Phase)
        {
            case Phase.RoundStart:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Duel);
                    if (State == GameState.Playing) Music(true);
                }
                return;
            case Phase.Duel:
                Fly(Ships[0], one, dt);
                Fly(Ships[1], two, dt);
                MoveTorpedoes(dt);
                Collide();
                return;
            case Phase.Exploded:
                // The survivor flies on while the pieces scatter, but can't score again.
                for (int i = 0; i < 2; i++) Fly(Ships[i], i == 0 ? one with { Fire = false } : two with { Fire = false }, dt);
                MoveTorpedoes(dt);
                Collide(scoring: false);
                if (PhaseTime > ExplodedSeconds) NextPoint();
                return;
            case Phase.RoundOver:
                if (PhaseTime > RoundOverSeconds) AfterRound();
                return;
        }
    }

    private void Fly(Ship s, Controls c, double dt)
    {
        if (!s.Alive) return;
        s.FireWait = Math.Max(0, s.FireWait - dt);
        if (s.Away > 0)
        {
            s.Away -= dt;
            if (s.Away <= 0) Reappear(s);
            return;
        }
        bool hyper = c.Hyper && !s.HyperHeld;
        s.HyperHeld = c.Hyper;
        if (hyper && s.Fuel >= HyperFuel)
        {
            Jump(s);
            return;
        }

        s.A += c.Turn * TurnRate * dt;
        s.Thrusting = c.Thrust && s.Fuel > 0;
        if (s.Thrusting)
        {
            s.Vx += Math.Cos(s.A) * ThrustPower * dt;
            s.Vy += Math.Sin(s.A) * ThrustPower * dt;
            s.Fuel = Math.Max(0, s.Fuel - FuelBurn * dt);
            _thrustSound -= dt;
            if (_thrustSound <= 0) { _thrustSound = 0.11; Play(Sfx.Thrust); }
        }
        var (gx, gy) = Pull(s.X, s.Y);
        s.Vx += gx * dt;
        s.Vy += gy * dt;
        double speed = s.Speed;
        if (speed > MaxSpeed) { s.Vx *= MaxSpeed / speed; s.Vy *= MaxSpeed / speed; }
        s.X += s.Vx * dt;
        s.Y += s.Vy * dt;
        Edges(s);

        if (c.Fire && s.FireWait <= 0 && Torpedoes.Count(t => t.Owner == s.Player) < MaxInFlight)
        {
            s.FireWait = FireDelay;
            if (s.Torpedoes > 0)
            {
                s.Torpedoes--;
                double cos = Math.Cos(s.A), sin = Math.Sin(s.A);
                Torpedoes.Add(new Torpedo
                {
                    X = s.X + cos * 16, Y = s.Y + sin * 16, Owner = s.Player,
                    Vx = s.Vx + cos * TorpedoSpeed, Vy = s.Vy + sin * TorpedoSpeed,
                });
                Play(Sfx.Fire);
            }
            else Play(Sfx.Empty);
        }
    }

    /// <summary>The sun's pull at a point.</summary>
    public (double X, double Y) Pull(double x, double y)
    {
        if (!Arena.HasSun) return (0, 0);
        double dx = Field.CX - x, dy = Field.CY - y;
        double r2 = Math.Max(dx * dx + dy * dy, 30 * 30), r = Math.Sqrt(r2);
        double a = Arena.Gravity / r2;
        return (dx / r * a, dy / r * a);
    }

    /// <summary>Off one edge and back on the other, or bounced back in the box.</summary>
    private void Edges(Ship s)
    {
        if (Arena.Wrap)
        {
            (s.X, s.Y) = Wrap(s.X, s.Y);
            return;
        }
        bool bounced = false;
        if (s.X < Ship.Radius) { s.X = Ship.Radius; s.Vx = Math.Abs(s.Vx) * 0.85; bounced = true; }
        if (s.X > Field.Width - Ship.Radius) { s.X = Field.Width - Ship.Radius; s.Vx = -Math.Abs(s.Vx) * 0.85; bounced = true; }
        if (s.Y < Field.Top + Ship.Radius) { s.Y = Field.Top + Ship.Radius; s.Vy = Math.Abs(s.Vy) * 0.85; bounced = true; }
        if (s.Y > Field.Height - Ship.Radius) { s.Y = Field.Height - Ship.Radius; s.Vy = -Math.Abs(s.Vy) * 0.85; bounced = true; }
        if (bounced) Play(Sfx.Bounce);
    }

    private static (double X, double Y) Wrap(double x, double y)
    {
        const double h = Field.Height - Field.Top;
        if (x < 0) x += Field.Width;
        else if (x >= Field.Width) x -= Field.Width;
        if (y < Field.Top) y += h;
        else if (y >= Field.Height) y -= h;
        return (x, y);
    }

    /// <summary>The shortest way from one point to another, across the edges when space wraps.</summary>
    public (double X, double Y) Towards(double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        if (Arena.Wrap)
        {
            const double h = Field.Height - Field.Top;
            if (dx > Field.Width / 2) dx -= Field.Width;
            else if (dx < -Field.Width / 2) dx += Field.Width;
            if (dy > h / 2) dy -= h;
            else if (dy < -h / 2) dy += h;
        }
        return (dx, dy);
    }

    // ───────────────────────────── hyperspace ─────────────────────────────

    /// <summary>The chance that the next jump goes wrong: none for the first, then more each time.</summary>
    public static double JumpRisk(int jumps) => Math.Min(0.6, jumps * HyperRisk);

    private void Jump(Ship s)
    {
        s.Fuel = Math.Max(0, s.Fuel - HyperFuel);
        double risk = JumpRisk(s.HyperJumps);
        s.HyperJumps++;
        Effects.Add(new Effect { Kind = EffectKind.Ring, X = s.X, Y = s.Y, Life = 0.4, Inward = true, Colour = Colour(s) });
        if (_rng.NextDouble() < risk)
        {
            Play(Sfx.HyperFail);
            Destroy(s, "HYPERSPACE FAILED");
            return;
        }
        s.Away = HyperTime;
        s.Thrusting = false;
        Play(Sfx.Hyper);
    }

    private void Reappear(Ship s)
    {
        var other = Ships[1 - s.Player];
        for (int tries = 0; tries < 40; tries++)
        {
            s.X = 40 + _rng.NextDouble() * (Field.Width - 80);
            s.Y = Field.Top + 40 + _rng.NextDouble() * (Field.Height - Field.Top - 80);
            double ds = Math.Sqrt((s.X - Field.CX) * (s.X - Field.CX) + (s.Y - Field.CY) * (s.Y - Field.CY));
            var (ox, oy) = Towards(s.X, s.Y, other.X, other.Y);
            if ((!Arena.HasSun || ds > 160) && Math.Sqrt(ox * ox + oy * oy) > 180) break;
        }
        Effects.Add(new Effect { Kind = EffectKind.Ring, X = s.X, Y = s.Y, Life = 0.4, Colour = Colour(s) });
    }

    // ───────────────────────────── torpedoes and collisions ─────────────────────────────

    private void MoveTorpedoes(double dt)
    {
        foreach (var t in Torpedoes)
        {
            t.X += t.Vx * dt;
            t.Y += t.Vy * dt;
            t.Age += dt;
            if (Arena.Wrap) (t.X, t.Y) = Wrap(t.X, t.Y);
            else if (t.X < 0 || t.X > Field.Width || t.Y < Field.Top || t.Y > Field.Height) t.Dead = true;
            // The sun swallows torpedoes too.
            if (Arena.HasSun && Dist2(t.X, t.Y, Field.CX, Field.CY) < SunKill * SunKill) t.Dead = true;
            if (t.Age > TorpedoLife) t.Dead = true;
        }
        Torpedoes.RemoveAll(t => t.Dead);
    }

    private static double Dist2(double ax, double ay, double bx, double by) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by);

    private void Collide(bool scoring = true)
    {
        var (a, b) = (Ships[0], Ships[1]);
        foreach (var s in Ships)
        {
            if (!s.InPlay) continue;
            if (Arena.HasSun && Dist2(s.X, s.Y, Field.CX, Field.CY) < SunKill * SunKill)
            {
                Play(Sfx.SunDeath);
                Destroy(s, "INTO THE SUN", scoring);
                continue;
            }
            foreach (var t in Torpedoes)
            {
                // A ship's own torpedoes can hit it too, once they have had a moment to get clear.
                if (t.Dead || (t.Owner == s.Player && t.Age < 0.3)) continue;
                var (dx, dy) = Towards(t.X, t.Y, s.X, s.Y);
                if (dx * dx + dy * dy > (Ship.Radius + 2) * (Ship.Radius + 2)) continue;
                t.Dead = true;
                Destroy(s, "HIT", scoring);
                break;
            }
        }
        if (a.InPlay && b.InPlay)
        {
            var (dx, dy) = Towards(a.X, a.Y, b.X, b.Y);
            if (dx * dx + dy * dy < (Ship.Radius * 1.6) * (Ship.Radius * 1.6))
            {
                Destroy(a, "COLLISION", false);
                Destroy(b, "COLLISION", false);
            }
        }
        Torpedoes.RemoveAll(t => t.Dead);
    }

    private void Destroy(Ship s, string cause, bool scoring = true)
    {
        if (!s.Alive) return;
        s.Alive = false;
        s.Thrusting = false;
        Cause = cause;
        Shatter(s);
        Effects.Add(new Effect { Kind = EffectKind.Flash, X = s.X, Y = s.Y, Life = 0.5, Colour = Colour(s) });
        Play(Sfx.Explode);
        // Whatever ends a ship counts for the other one, except flying into each other.
        if (scoring && Phase == Phase.Duel)
        {
            var other = Ships[1 - s.Player];
            other.Kills++;
            if (other.Player == 0 && !TwoPlayer && State == GameState.Playing) AddScore(500 + 100 * (Round - 1), s.X, s.Y);
            Play(Sfx.Point);
        }
        if (Phase == Phase.Duel) SetPhase(Phase.Exploded);
    }

    /// <summary>The ship comes apart line by line.</summary>
    private void Shatter(Ship s)
    {
        var pts = Shape(s.Needle);
        double cos = Math.Cos(s.A), sin = Math.Sin(s.A);
        for (int i = 0; i < pts.Length; i++)
        {
            var (x0, y0) = pts[i];
            var (x1, y1) = pts[(i + 1) % pts.Length];
            double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
            double wx = s.X + mx * cos - my * sin, wy = s.Y + mx * sin + my * cos;
            double ang = _rng.NextDouble() * Math.PI * 2, sp = 30 + _rng.NextDouble() * 90;
            Debris.Add(new Debris
            {
                X = wx, Y = wy, Vx = s.Vx * 0.4 + Math.Cos(ang) * sp, Vy = s.Vy * 0.4 + Math.Sin(ang) * sp,
                A = s.A + Math.Atan2(y1 - y0, x1 - x0), Va = (_rng.NextDouble() - 0.5) * 8,
                Length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)), Life = 1.4 + _rng.NextDouble() * 0.8,
                Colour = Colour(s),
            });
        }
    }

    private void MoveDebris(Debris d, double dt)
    {
        d.X += d.Vx * dt;
        d.Y += d.Vy * dt;
        d.A += d.Va * dt;
        d.Age += dt;
    }

    /// <summary>A ship's outline, nose along +x: the long needle or the broad wedge.</summary>
    public static (double X, double Y)[] Shape(bool needle) => needle ? NeedleShape : WedgeShape;

    private static readonly (double X, double Y)[] NeedleShape =
        [(22, 0), (-7, -4), (-15, -10), (-11, -3), (-16, 0), (-11, 3), (-15, 10), (-7, 4)];
    private static readonly (double X, double Y)[] WedgeShape = [(17, 0), (-14, -14), (-7, 0), (-14, 14)];

    public static uint Colour(Ship s) => s.Needle ? 0xFF8FE8FFu : 0xFFFFC870u;

    // ───────────────────────────── points and rounds ─────────────────────────────

    private void NextPoint()
    {
        var (a, b) = (Ships[0], Ships[1]);
        if (a.Kills >= KillsToWin || b.Kills >= KillsToWin)
        {
            // A dead heat goes to the needle.
            RoundWinner = a.Kills >= KillsToWin ? 0 : 1;
            SetPhase(Phase.RoundOver);
            Music(false);
            Torpedoes.Clear();
            if (TwoPlayer) Say(RoundWinner == 0 ? "THE NEEDLE WINS!" : "THE WEDGE WINS!", $"{a.Kills} - {b.Kills}", RoundOverSeconds);
            else if (RoundWinner == 0)
            {
                int bonus = 1000 * Round;
                if (State == GameState.Playing) AddScore(bonus, Field.CX, Field.CY + 60);
                Say($"ROUND {Round} WON!", $"BONUS {bonus}", RoundOverSeconds);
                Play(Sfx.RoundWon);
            }
            else
            {
                Say($"ROUND {Round} LOST", Lives > 1 ? "TRY IT AGAIN" : null, RoundOverSeconds);
                Play(Sfx.RoundLost);
            }
            return;
        }
        Serve();
        SetPhase(Phase.Duel);
    }

    private void AfterRound()
    {
        if (TwoPlayer) { GameOver(); return; }
        if (State == GameState.Attract)
        {
            // The demonstration goes on to another arena.
            Round = Round % Arena.All.Length + 1;
            BeginRound();
            return;
        }
        if (RoundWinner == 0) Round++;
        else
        {
            Lives--;
            if (Lives <= 0) { GameOver(); return; }
        }
        BeginRound();
    }

    private void AddScore(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Life = 1.2, Colour = 0xFFFFE14A });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls one, Controls two = default)
    {
        for (int i = 0; i < ticks; i++) Tick(one, two);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>For tests: the wedge takes the second controls even in a one-player game.</summary>
    internal bool BothManual { get; set; }

    /// <summary>Straight into the duel of a one-player round, with both ships under the test's control.</summary>
    internal void Duel(int round = 1)
    {
        BothManual = true;
        StartGame(false);
        Round = round;
        BeginRound();
        SetPhase(Phase.Duel);
        Message = SubMessage = null;
    }

    internal void SetRound(int round) => Round = round;

    internal void Put(int ship, double x, double y, double heading = 0, double vx = 0, double vy = 0)
    {
        var s = Ships[ship];
        s.X = x;
        s.Y = y;
        s.A = heading;
        s.Vx = vx;
        s.Vy = vy;
    }

    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void SetKills(int needle, int wedge) { Ships[0].Kills = needle; Ships[1].Kills = wedge; }
}
