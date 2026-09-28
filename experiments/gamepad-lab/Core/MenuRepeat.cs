namespace GamepadLab.Core;

public sealed class MenuRepeat
{
    private int _direction;
    private double _elapsed;
    private bool _repeating;
    public int Update(int digitalDirection, double analog, double delta)
    {
        // Godot axis values arrive as floats; retain the documented inclusive
        // .65/.45 boundaries despite their binary representation.
        var magnitude = Math.Abs(analog);
        var direction = digitalDirection != 0 ? Math.Sign(digitalDirection) : magnitude >= .65 - 1e-6 ? Math.Sign(analog) : magnitude <= .45 + 1e-6 ? 0 : _direction;
        if (direction == 0) { _direction = 0; _elapsed = 0; _repeating = false; return 0; }
        if (direction != _direction) { _direction = direction; _elapsed = 0; _repeating = false; return direction; }
        _elapsed += delta;
        var threshold = _repeating ? .10 : .35;
        if (_elapsed < threshold) return 0;
        _elapsed -= threshold; _repeating = true; return _direction;
    }
    public void Clear() { _direction = 0; _elapsed = 0; _repeating = false; }
}
