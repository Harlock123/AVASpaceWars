using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASpaceWars.Engine;
using Effect = AVASpaceWars.Engine.Effect;

namespace AVASpaceWars.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASpaceWars/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFEAF4FF, Yellow = 0xFFFFE14A, Red = 0xFFFF5A4A, Dim = 0xFF7F8CA8, Cyan = 0xFF8FE8FF, Amber = 0xFFFFC870;

    /// <summary>Text with a soft glow behind it, like the beam on a vector screen.</summary>
    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool glow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        var pos = new Point(x, at.Y - ft.Height / 2);
        if (glow)
        {
            var halo = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour & 0x30FFFFFF));
            foreach (var (ox, oy) in new[] { (-1.5, 0.0), (1.5, 0.0), (0.0, -1.5), (0.0, 1.5) }) dc.DrawText(halo, new Point(pos.X + ox, pos.Y + oy));
        }
        dc.DrawText(ft, pos);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    /// <summary>A line as the beam draws it: a wide faint halo, a narrower one, and the bright core.</summary>
    private static void Beam(DrawingContext dc, Point a, Point b, uint colour, double bright = 1)
    {
        uint Alpha(double f) => (uint)Math.Clamp(255 * f * bright, 0, 255) << 24 | (colour & 0x00FFFFFF);
        dc.DrawLine(Pen(Alpha(0.12), 7), a, b);
        dc.DrawLine(Pen(Alpha(0.3), 3.5), a, b);
        dc.DrawLine(Pen(Alpha(1), 1.4), a, b);
    }

    private static void Outline(DrawingContext dc, IReadOnlyList<Point> pts, uint colour, double bright = 1)
    {
        for (int i = 0; i < pts.Count; i++) Beam(dc, pts[i], pts[(i + 1) % pts.Count], colour, bright);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF02040A), new Rect(0, 0, Field.Width, Field.Height));
        DrawStars(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (inGame || g.State == GameState.GameOver) DrawSun(dc, g.Arena);
        else DrawSun(dc, Arena.All[0]);
        if (inGame)
        {
            if (!g.Arena.Wrap) DrawBox(dc);
            foreach (var t in g.Torpedoes) DrawTorpedo(dc, t);
            foreach (var s in g.Ships)
                if (s.InPlay) DrawShip(dc, s.X, s.Y, s.A, s.Needle, s.Thrusting);
        }
        foreach (var d in g.Debris) DrawDebris(dc, d);
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
        {
            Text(dc, g.TwoPlayer ? "DUEL OVER" : "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
            if (g.TwoPlayer) Text(dc, g.RoundWinner == 0 ? "THE NEEDLE WINS THE DUEL" : "THE WEDGE WINS THE DUEL", new Point(Field.Width / 2, 410), 40, g.RoundWinner == 0 ? Cyan : Amber, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - ENTER: ONE PLAYER   2: TWO PLAYERS", new Point(Field.Width / 2, Field.Height - 22), 26, Yellow, center: true);
    }

    // ───────────────────────────── space ─────────────────────────────

    private static void DrawStars(DrawingContext dc)
    {
        for (int i = 0; i < 110; i++)
        {
            uint h = Hash(i, 3, 17);
            double x = h % 1024, y = Field.Top + (h >> 10) % (int)(Field.Height - Field.Top);
            byte a = (byte)(40 + (h >> 20) % 90);
            dc.FillRectangle(Brush((uint)a << 24 | 0x00C8D8FF), new Rect(x, y, 1.4, 1.4));
        }
    }

    /// <summary>The sun: a burst of flickering rays. A black hole shows only as a faint ring that bends the light.</summary>
    private void DrawSun(DrawingContext dc, Arena arena)
    {
        if (!arena.HasSun) return;
        var c = new Point(Field.CX, Field.CY);
        if (!arena.SunShows)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(_clock * 2);
            dc.DrawEllipse(null, Pen((uint)(30 + 30 * pulse) << 24 | 0x00B070FF, 2), c, GameEngine.SunKill + 6, GameEngine.SunKill + 6);
            return;
        }
        int frame = (int)(_clock * 24);
        int rays = 16;
        double heavy = arena.Gravity > 5e6 ? 1.35 : 1;
        for (int i = 0; i < rays; i++)
        {
            double ang = i * Math.PI * 2 / rays + _clock * 0.4;
            double len = (10 + Hash(i, frame, 5) % 22) * heavy;
            var a = new Point(c.X + Math.Cos(ang) * 4, c.Y + Math.Sin(ang) * 4);
            var b = new Point(c.X + Math.Cos(ang) * len, c.Y + Math.Sin(ang) * len);
            Beam(dc, a, b, 0xFFFFF0C0, 0.9);
        }
        dc.DrawEllipse(Brush(0x50FFE0A0), null, c, 16 * heavy, 16 * heavy);
        dc.DrawEllipse(Brush(0xFFFFFFF0), null, c, 4, 4);
    }

    private static void DrawBox(DrawingContext dc)
    {
        var pts = new[] { new Point(2, Field.Top + 2), new Point(Field.Width - 2, Field.Top + 2), new Point(Field.Width - 2, Field.Height - 2), new Point(2, Field.Height - 2) };
        Outline(dc, pts, 0xFF6A8CFF, 0.6);
    }

    // ───────────────────────────── ships and torpedoes ─────────────────────────────

    private static Point[] ShipPoints(double x, double y, double a, bool needle, double scale = 1)
    {
        double cos = Math.Cos(a), sin = Math.Sin(a);
        return GameEngine.Shape(needle).Select(p => new Point(x + (p.X * cos - p.Y * sin) * scale, y + (p.X * sin + p.Y * cos) * scale)).ToArray();
    }

    private void DrawShip(DrawingContext dc, double x, double y, double a, bool needle, bool thrusting, double scale = 1)
    {
        uint colour = needle ? Cyan : Amber;
        Outline(dc, ShipPoints(x, y, a, needle, scale), colour);
        // Near an edge of wrapping space the ship shows on the far side too.
        if (Engine.State is GameState.Playing or GameState.Attract or GameState.Paused && Engine.Arena.Wrap && scale == 1)
        {
            double h = Field.Height - Field.Top;
            foreach (var (ox, oy) in new[] { (Field.Width, 0.0), (-Field.Width, 0.0), (0.0, h), (0.0, -h) })
            {
                double nx = x + ox, ny = y + oy;
                if (nx > -20 && nx < Field.Width + 20 && ny > Field.Top - 20 && ny < Field.Height + 20)
                    Outline(dc, ShipPoints(nx, ny, a, needle), colour);
            }
        }
        if (!thrusting) return;
        // The exhaust flickers behind.
        double cos = Math.Cos(a), sin = Math.Sin(a);
        double back = needle ? -16 : -10, len = 8 + Hash((int)(_clock * 30), needle ? 1 : 2, 9) % 9;
        var p0 = new Point(x + back * cos * scale - 3 * -sin * scale, y + back * sin * scale - 3 * cos * scale);
        var p1 = new Point(x + back * cos * scale + 3 * -sin * scale, y + back * sin * scale + 3 * cos * scale);
        var tip = new Point(x + (back - len) * cos * scale, y + (back - len) * sin * scale);
        Beam(dc, p0, tip, 0xFFFFB060, 0.9);
        Beam(dc, p1, tip, 0xFFFFB060, 0.9);
    }

    private static void DrawTorpedo(DrawingContext dc, Torpedo t)
    {
        uint colour = t.Owner == 0 ? Cyan : Amber;
        var c = new Point(t.X, t.Y);
        var tail = new Point(t.X - t.Vx * 0.025, t.Y - t.Vy * 0.025);
        if (Math.Abs(tail.X - c.X) < 40 && Math.Abs(tail.Y - c.Y) < 40) Beam(dc, tail, c, colour, 0.5);
        dc.DrawEllipse(Brush(colour & 0x50FFFFFF), null, c, 5, 5);
        dc.DrawEllipse(Brush(0xFFFFFFFF), null, c, 2, 2);
    }

    private static void DrawDebris(DrawingContext dc, Debris d)
    {
        double f = 1 - d.Age / d.Life;
        if (f <= 0) return;
        double hx = Math.Cos(d.A) * d.Length / 2, hy = Math.Sin(d.A) * d.Length / 2;
        Beam(dc, new Point(d.X - hx, d.Y - hy), new Point(d.X + hx, d.Y + hy), d.Colour, f);
    }

    private static void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Ring:
            {
                double r = e.Inward ? 34 * (1 - t) + 2 : 34 * t + 2;
                int n = 14;
                var pts = Enumerable.Range(0, n).Select(i => new Point(c.X + Math.Cos(i * Math.PI * 2 / n) * r, c.Y + Math.Sin(i * Math.PI * 2 / n) * r)).ToArray();
                Outline(dc, pts, e.Colour, 1 - t * 0.6);
                break;
            }
            case EffectKind.Flash:
            {
                // A burst of light: rays flung out, and sparks beyond them.
                double f = 1 - t;
                if (t < 0.25) dc.DrawEllipse(Brush((uint)(220 * (1 - t * 4)) << 24 | 0x00FFFFFF), null, c, 6, 6);
                for (int i = 0; i < 12; i++)
                {
                    double ang = i * Math.PI / 6 + Hash(i, (int)e.X, 1) % 10 * 0.04, len = 0.6 + Hash(i, 2, 3) % 10 * 0.06;
                    var a = new Point(c.X + Math.Cos(ang) * 60 * t * len, c.Y + Math.Sin(ang) * 60 * t * len);
                    var b = new Point(c.X + Math.Cos(ang) * (60 * t * len + 10 * f), c.Y + Math.Sin(ang) * (60 * t * len + 10 * f));
                    Beam(dc, a, b, i % 2 == 0 ? 0xFFFFFFFF : e.Colour, f);
                    dc.FillRectangle(Brush((uint)(255 * f) << 24 | 0x00FFFFFF), new Rect(c.X + Math.Cos(ang + 0.26) * 90 * t * len, c.Y + Math.Sin(ang + 0.26) * 90 * t * len, 2.2, 2.2));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 24 - t * 30), 28, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF02040A), new Rect(0, 0, Field.Width, Field.Top));
        Beam(dc, new Point(0, Field.Top - 1), new Point(Field.Width, Field.Top - 1), 0xFF4060B0, 0.5);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        // Each ship's corner: its name, its points this round, its fuel and its torpedoes.
        for (int i = 0; i < 2; i++)
        {
            var s = g.Ships[i];
            bool left = i == 0;
            double x = left ? 16 : Field.Width - 16;
            uint colour = left ? Cyan : Amber;
            string name = left ? "NEEDLE" : g.TwoPlayer || g.State == GameState.Attract ? "WEDGE" : "WEDGE (CPU)";
            Text(dc, name, new Point(x, 16), 24, colour, right: !left);
            if (!inGame) continue;
            for (int k = 0; k < g.KillsToWin; k++)
            {
                double px = left ? x + 6 + k * 18 : x - 6 - k * 18;
                var dot = new Point(px, 44);
                if (k < s.Kills) dc.DrawEllipse(Brush(colour), null, dot, 6, 6);
                else dc.DrawEllipse(null, Pen(colour & 0x80FFFFFF, 1.4), dot, 6, 6);
            }
            double bx = left ? 160 : Field.Width - 160 - 120;
            Text(dc, "FUEL", new Point(bx, 16), 20, Dim);
            dc.DrawRectangle(null, Pen(colour & 0x90FFFFFF, 1), new Rect(bx, 32, 120, 10));
            dc.FillRectangle(Brush(s.Fuel < 0.2 ? Red : colour), new Rect(bx + 1, 33, 118 * s.Fuel, 8));
            Text(dc, $"TORPEDOES {s.Torpedoes,2}", new Point(bx, 54), 20, s.Torpedoes == 0 ? Red : Dim);
        }
        double cx = Field.Width / 2;
        if (g.TwoPlayer && inGame)
        {
            Text(dc, "TWO PLAYER DUEL", new Point(cx, 16), 24, White, center: true);
            Text(dc, $"FIRST TO {g.KillsToWin}", new Point(cx, 44), 22, Dim, center: true);
            return;
        }
        Text(dc, $"SCORE {g.Score:D6}", new Point(cx - 10, 16), 24, White, right: true);
        Text(dc, $"HI {Math.Max(g.HighScores.TopScore, g.Score):D6}", new Point(cx + 10, 16), 24, Dim);
        if (!inGame) return;
        Text(dc, $"ROUND {g.Round} - {g.Arena.Name}", new Point(cx, 44), 22, Yellow, center: true);
        // Ships in reserve.
        for (int i = 0; i < Math.Min(5, g.Lives - 1); i++)
            Outline(dc, ShipPoints(cx + 150 + i * 22, 44, -Math.PI / 2, true, 0.55), Cyan, 0.8);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2;
        uint colour = g.Phase == Phase.RoundOver && !g.TwoPlayer && g.RoundWinner == 1 ? Red : Yellow;
        Text(dc, msg, new Point(cx, 190), 64, colour, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, 244), 30, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // Two ships circling the sun behind the title.
        for (int i = 0; i < 2; i++)
        {
            double ang = _clock * 0.55 + i * Math.PI;
            const double rx = 470, ry = 330;
            double x = Field.CX + Math.Cos(ang) * rx, y = Field.CY + 10 + Math.Sin(ang) * ry;
            double heading = Math.Atan2(Math.Cos(ang) * ry, -Math.Sin(ang) * rx);
            DrawShip(dc, x, y, heading, i == 0, true, 1.5);
        }
        Text(dc, "AVA SPACE WARS", new Point(cx, 150), 112, Cyan, center: true);
        Text(dc, "AFTER THE 1977 ARCADE CLASSIC", new Point(cx, 218), 28, Dim, center: true);
        string[] lines =
        [
            "THE NEEDLE AGAINST THE WEDGE, AROUND A SUN THAT PULLS",
            "TORPEDOES AND FUEL RUN OUT - HYPERSPACE IS RISKIER EACH JUMP",
            "ONE PLAYER: BEAT THE COMPUTER ROUND BY ROUND, ARENA BY ARENA",
            "TWO PLAYERS: FIRST TO SEVEN",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 290 + i * 36), 28, White, center: true);
        if ((int)(_clock * 2) % 2 == 0)
        {
            Text(dc, "ENTER: ONE PLAYER", new Point(cx, 480), 46, Yellow, center: true);
            Text(dc, "2: TWO PLAYERS", new Point(cx, 528), 40, Yellow, center: true);
        }
        Text(dc, "ONE PLAYER: ARROWS TURN AND THRUST - SPACE FIRES - DOWN JUMPS", new Point(cx, 610), 24, Dim, center: true);
        Text(dc, "TWO PLAYERS: NEEDLE W A S D + SPACE   WEDGE ARROWS + RIGHT CTRL", new Point(cx, 642), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC002040A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "TOP DUELLISTS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "ENTER: ONE PLAYER   2: TWO PLAYERS", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC002040A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
