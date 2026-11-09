namespace SystemInfo;

/// <summary>
/// Monotonic high-watermark tracker. Used to keep per-row name/value widths stable across
/// refreshes; once observed, the width only grows, so text/bars/graphs stop jittering on every tick.
/// </summary>
public sealed class StickyMax
{
    public double Value { get; private set; } = 0;

    public void Add(double observed)
    {
        if (observed > Value) Value = observed;
    }

    public void Reset(double initial = 0) => Value = initial;
}

/// <summary>Monotonic low-watermark tracker (unused for now, keeps the API symmetric).</summary>
public sealed class StickyMin
{
    public double Value { get; private set; } = double.PositiveInfinity;

    public void Add(double observed)
    {
        if (observed < Value) Value = observed;
    }

    public void Reset(double initial = double.PositiveInfinity) => Value = initial;
}
