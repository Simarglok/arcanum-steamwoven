namespace GamepadLab.Core;


public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public bool Contains(Vec2 point) => point.X >= X && point.Y >= Y && point.X <= X + Width && point.Y <= Y + Height;
    public Vec2 Center => new(X + Width / 2d, Y + Height / 2d);
}
