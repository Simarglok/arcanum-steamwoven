// Historical namespace retained for the lab's public source and forwarded type API.
namespace GamepadLab.Core;

public readonly record struct Vec2(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vec2 Normalized() => Length <= 0 ? default : new(X / Length, Y / Length);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double b) => new(a.X * b, a.Y * b);
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static Vec2 CircularDeadzone(Vec2 value, double deadzone)
    {
        var magnitude = value.Length;
        if (magnitude <= deadzone || magnitude == 0) return default;
        return value.Normalized() * Math.Clamp((magnitude - deadzone) / (1d - deadzone), 0, 1);
    }
}
