namespace GamepadLab.Core;

public sealed class MenuRepeat
{
    private int _direction;
    private double _elapsed;
    private bool _repeating;
    public int Update(int digitalDirection, double analog, double delta)
    {
        var direction = digitalDirection != 0 ? Math.Sign(digitalDirection) : Math.Abs(analog) >= .65 ? Math.Sign(analog) : Math.Abs(analog) <= .45 ? 0 : _direction;
        if (direction == 0) { _direction = 0; _elapsed = 0; _repeating = false; return 0; }
        if (direction != _direction) { _direction = direction; _elapsed = 0; _repeating = false; return direction; }
        _elapsed += delta;
        var threshold = _repeating ? .10 : .35;
        if (_elapsed < threshold) return 0;
        _elapsed -= threshold; _repeating = true; return _direction;
    }
    public void Clear() { _direction = 0; _elapsed = 0; _repeating = false; }
}
