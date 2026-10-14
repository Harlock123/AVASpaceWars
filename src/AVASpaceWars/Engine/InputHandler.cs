using Avalonia.Input;

namespace AVASpaceWars.Engine;

/// <summary>
/// Keyboard and gamepads boiled down to each ship's controls. Alone, the needle answers to the arrows
/// or W A S D and the first gamepad. In a two-player game the keyboard is split down the middle: the
/// needle on W A S D, the wedge on the arrows; the second gamepad flies the wedge.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0), _pad2 = new(1);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
        _pad2.Update();
    }

    /// <summary>Whichever of the two keys was pressed last wins, so rolling from one to the other turns at once.</summary>
    private int Axis(Key[] minus, Key[] plus)
    {
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (minus.Contains(_held[i])) return -1;
            if (plus.Contains(_held[i])) return 1;
        }
        return 0;
    }

    private static Controls Pad(ControllerManager p) => new(
        p.DPadLeft || p.LeftStickX < -0.4f ? -1 : p.DPadRight || p.LeftStickX > 0.4f ? 1 : 0,
        p.DPadUp || p.LeftStickY < -0.5f || p.RightTrigger > 0.3f,
        p.ButtonA || p.ButtonX,
        p.ButtonB || p.ButtonY || p.DPadDown);

    private static Controls Merge(Controls a, Controls b) =>
        new(a.Turn != 0 ? a.Turn : b.Turn, a.Thrust || b.Thrust, a.Fire || b.Fire, a.Hyper || b.Hyper);

    /// <summary>The needle's controls (player one).</summary>
    public Controls One(bool twoPlayer)
    {
        Controls keys = twoPlayer
            ? new(Axis([Key.A], [Key.D]), Has(Key.W), Any(Key.Space, Key.LeftCtrl), Any(Key.S, Key.LeftShift))
            : new(Axis([Key.Left, Key.A], [Key.Right, Key.D]), Any(Key.Up, Key.W),
                Any(Key.Space, Key.Z, Key.LeftCtrl, Key.RightCtrl), Any(Key.Down, Key.S, Key.X, Key.LeftShift, Key.RightShift));
        return Merge(keys, Pad(_pad));
    }

    /// <summary>The wedge's controls, in a two-player game.</summary>
    public Controls Two() => Merge(
        new(Axis([Key.Left], [Key.Right]), Has(Key.Up), Any(Key.RightCtrl, Key.NumPad0, Key.OemQuestion), Any(Key.Down, Key.RightShift)),
        Pad(_pad2));

    /// <summary>One player: Enter, Space, 1, or the first pad's Start.</summary>
    public bool Start => Any(Key.Enter, Key.Space, Key.D1, Key.NumPad1) || _pad.ButtonStart || _pad.ButtonA;

    /// <summary>Two players: 2, or the second pad's Start.</summary>
    public bool Start2 => Any(Key.D2, Key.NumPad2) || _pad2.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart || _pad2.ButtonStart;
    public bool PadBack => _pad.ButtonBack || _pad2.ButtonBack;

    public void Dispose()
    {
        _pad.Dispose();
        _pad2.Dispose();
    }
}
