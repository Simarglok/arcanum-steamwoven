namespace GamepadLab.Core;

/// <summary>Lab-only camera zoom, not WORLD's policy or persistence.</summary>
public static class LabZoomPolicy
{
    public static double Apply(double current,double drive,double delta)
    {
        if(!double.IsFinite(current)||current<.5||current>2||!double.IsFinite(drive)||drive is < -1 or > 1||!double.IsFinite(delta)||delta<0)
            throw new ArgumentException("invalid lab zoom frame");
        if(drive==0||delta==0)return current;
        // Clamping log2 space before exponentiation prevents overflow/underflow and NaN.
        return Math.Pow(2,Math.Clamp(Math.Log2(current)+drive*delta,-1,1));
    }
}
