namespace SystemInfo;

/// <summary>
/// Removes empty space between boxes without changing the arrangement the user built:
/// boxes are grouped into columns by their left edge, each column is packed vertically,
/// then each column is slid into its tightest slot left-to-right. Columns are never merged
/// into a single horizontal line, boxes are never resized, and any pre-existing overlap
/// between columns is resolved by sliding the later column right until it clears the earlier one.
/// </summary>
public static class LayoutCompactor
{
    public const double Padding = 8;
    public const double Gap = 4;
    public const double ColumnTolerance = 16;

    /// <summary>Compacts <paramref name="layout"/> in place. Returns true when at least one visible box was moved.</summary>
    public static bool Compact(WindowLayout layout)
    {
        var boxes = layout.Boxes.Where(b => b.Visible).ToList();
        if (boxes.Count == 0) return false;

        var before = boxes.Select(b => (b.X, b.Y)).ToList();

        // 1. Columns: group boxes whose left edges line up. Vertical order inside a column is kept.
        var columns = new List<List<BoxLayout>>();
        foreach (var b in boxes.OrderBy(b => b.X).ThenBy(b => b.Y))
        {
            var col = columns.FirstOrDefault(c => Math.Abs(c[0].X - b.X) <= ColumnTolerance);
            if (col is null) columns.Add([b]);
            else col.Add(b);
        }

        // 2. Remove vertical gaps inside each column. X offsets within the column are preserved.
        foreach (var col in columns)
        {
            double y = Padding;
            foreach (var b in col.OrderBy(b => b.Y))
            {
                b.Y = y;
                y += b.Height + Gap;
            }
        }

        // 3. Slide columns left, one at a time, stopping at anything they would collide with.
        var placed = new List<BoxLayout>();
        foreach (var col in columns.OrderBy(c => c.Min(b => b.X)))
        {
            double minX = col.Min(b => b.X);
            double dx = Padding - minX;
            foreach (var b in col)
            {
                foreach (var p in placed)
                {
                    bool verticalOverlap = b.Y < p.Y + p.Height + Gap && b.Y + b.Height + Gap > p.Y;
                    if (!verticalOverlap) continue;
                    dx = Math.Max(dx, p.X + p.Width + Gap - b.X);
                }
            }
            // If the existing layout already overlaps a previous column, slide right until it clears.
            foreach (var b in col)
            {
                b.X = Math.Max(Padding, b.X + dx);
                placed.Add(b);
            }
        }

        return boxes.Select((b, i) => Math.Abs(b.X - before[i].X) > 0.01 || Math.Abs(b.Y - before[i].Y) > 0.01).Any(moved => moved);
    }

    /// <summary>Content size (including padding) needed to show every visible box.</summary>
    public static (double width, double height) ContentSize(WindowLayout layout)
    {
        var boxes = layout.Boxes.Where(b => b.Visible).ToList();
        if (boxes.Count == 0) return (0, 0);
        return (boxes.Max(b => b.X + b.Width) + Padding, boxes.Max(b => b.Y + b.Height) + Padding);
    }
}
