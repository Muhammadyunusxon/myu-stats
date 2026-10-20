namespace MYUStats;

public readonly record struct PointD(double X, double Y);

public readonly record struct SizeD(double Width, double Height);

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(PointD point) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public RectD Inflate(double amount) => new(X - amount, Y - amount, Width + 2 * amount, Height + 2 * amount);
}

/// <summary>The two buttons at the ends of the pill.</summary>
public enum PillButton
{
    /// <summary>At the start: drag it to slide the pill along the edge, or onto another edge.</summary>
    Move,
    /// <summary>At the end: opens Settings.</summary>
    Settings,
}

/// <summary>
/// Geometry of the pill, its buttons, the folded tab and the detail card, in DIPs relative to the work
/// area of the display the pill sits on (origin top-left, y down).
/// </summary>
/// <remarks>
/// Everything is worked out in edge-relative terms, along the edge and across it (measured from the
/// screen edge inward), and mapped to the work area once, in <see cref="Map"/>. That keeps one set of
/// rules for all four edges, as in the macOS app.
/// </remarks>
public sealed record EdgeLayout(ScreenEdge Edge, int CellCount, SizeD Area)
{
    public const double VerticalDepth = 54;
    public const double HorizontalDepth = 60;
    public const double VerticalCellLength = 72;
    public const double HorizontalCellLength = 66;
    /// <summary>Room for each end button along the pill.</summary>
    public const double ButtonLength = 34;
    public const double BodyPadding = 5;
    public const double CardWidth = 248;
    public const double CardGap = 8;
    /// <summary>Space kept between a card and the ends of the work area.</summary>
    public const double CardMargin = 4;
    /// <summary>The folded pill: a thin tab on the edge.</summary>
    public const double TabLength = 60;
    public const double TabDepth = 5;
    public const double DropZoneLength = 180;
    public const double DropZoneDepth = 64;

    public bool IsVertical => Edge.IsVertical();
    public double Depth => IsVertical ? VerticalDepth : HorizontalDepth;
    public double CellLength => IsVertical ? VerticalCellLength : HorizontalCellLength;
    public double PillLength => CellCount * CellLength + 2 * (ButtonLength + BodyPadding);

    /// <summary>Length of the work area along the edge.</summary>
    public double EdgeLength => IsVertical ? Area.Height : Area.Width;

    /// <summary>A box <paramref name="length"/> long along the edge and <paramref name="depth"/> deep, starting <paramref name="across"/> in from it.</summary>
    public RectD Map(double along, double length, double across, double depth) => Edge switch
    {
        ScreenEdge.Right => new RectD(Area.Width - across - depth, along, depth, length),
        ScreenEdge.Left => new RectD(across, along, depth, length),
        ScreenEdge.Top => new RectD(along, across, length, depth),
        _ => new RectD(along, Area.Height - across - depth, length, depth),
    };

    /// <summary>Inverse of <see cref="Map"/> for a point.</summary>
    public (double Along, double Across) EdgeCoordinates(PointD point) => Edge switch
    {
        ScreenEdge.Right => (point.Y, Area.Width - point.X),
        ScreenEdge.Left => (point.Y, point.X),
        ScreenEdge.Top => (point.X, point.Y),
        _ => (point.X, Area.Height - point.Y),
    };

    // MARK: - Pill

    /// <summary>Where the pill starts along the edge: centred, nudged by <paramref name="offset"/>, and kept on screen.</summary>
    public double PillStart(double offset) =>
        Math.Clamp((EdgeLength - PillLength) / 2 + offset, 0, Math.Max(0, EdgeLength - PillLength));

    public RectD PillRect(double offset) => Map(PillStart(offset), PillLength, 0, Depth);

    /// <summary>The folded state: a strip along the pill's whole span, as deep as the tab, so touching the edge anywhere next to it opens the pill.</summary>
    public RectD StripRect(double offset) => Map(PillStart(offset), PillLength, 0, TabDepth);

    /// <summary>The visible tab, centred in the strip.</summary>
    public RectD TabRect(double offset) => Map(PillStart(offset) + (PillLength - TabLength) / 2, TabLength, 0, TabDepth);

    /// <summary>Centre of a cell, measured along the pill from its start.</summary>
    public double CellCenter(int index) => BodyPadding + ButtonLength + (index + 0.5) * CellLength;

    public RectD CellRect(double offset, int index) =>
        Map(PillStart(offset) + BodyPadding + ButtonLength + index * CellLength, CellLength, 0, Depth);

    public RectD ButtonRect(double offset, PillButton button) => Map(
        button == PillButton.Move ? PillStart(offset) + BodyPadding : PillStart(offset) + PillLength - BodyPadding - ButtonLength,
        ButtonLength, 0, Depth);

    public int? CellAt(double offset, PointD point)
    {
        for (var index = 0; index < CellCount; index++)
        {
            if (CellRect(offset, index).Contains(point)) return index;
        }
        return null;
    }

    public PillButton? ButtonAt(double offset, PointD point) =>
        ButtonRect(offset, PillButton.Move).Contains(point) ? PillButton.Move
        : ButtonRect(offset, PillButton.Settings).Contains(point) ? PillButton.Settings
        : null;

    /// <summary>The offset that would centre an unclamped pill where <paramref name="pill"/> actually is.</summary>
    public double OffsetOf(RectD pill) =>
        IsVertical ? pill.Y - (Area.Height - pill.Height) / 2 : pill.X - (Area.Width - pill.Width) / 2;

    // MARK: - Card

    /// <summary>The card beside a cell: centred on it along the edge and kept inside the work area.
    /// <paramref name="measured"/> is the card's height as laid out.</summary>
    public RectD CardRect(double offset, int index, double measured)
    {
        var center = PillStart(offset) + CellCenter(index);
        var length = IsVertical ? measured : CardWidth;
        var depth = IsVertical ? CardWidth : measured;
        var start = Math.Clamp(center - length / 2, CardMargin, Math.Max(CardMargin, EdgeLength - length - CardMargin));
        return Map(start, length, Depth + CardGap, depth);
    }

    // MARK: - Moving to another edge

    /// <summary>The edge a dragged pill is heading for: the nearest edge other than
    /// <paramref name="current"/>, once the cursor is inside the outer quarter of the screen on that side.</summary>
    public static ScreenEdge? DropTarget(PointD mouse, SizeD area, ScreenEdge current)
    {
        (ScreenEdge Edge, double Distance, double Band)[] candidates =
        [
            (ScreenEdge.Left, mouse.X, area.Width / 4),
            (ScreenEdge.Right, area.Width - mouse.X, area.Width / 4),
            (ScreenEdge.Top, mouse.Y, area.Height / 4),
            (ScreenEdge.Bottom, area.Height - mouse.Y, area.Height / 4),
        ];
        // Compare relative to each band, so a wide screen does not favour the top and bottom.
        var nearest = candidates.MinBy(candidate => candidate.Distance / candidate.Band);
        return nearest.Edge != current && nearest.Distance < nearest.Band ? nearest.Edge : null;
    }

    /// <summary>A drop slot on <paramref name="edge"/>, following the cursor along it and kept on screen.</summary>
    public static RectD DropZoneRect(ScreenEdge edge, PointD cursor, SizeD area)
    {
        if (edge.IsVertical())
        {
            var y = Math.Clamp(cursor.Y - DropZoneLength / 2, 0, Math.Max(0, area.Height - DropZoneLength));
            var x = edge == ScreenEdge.Right ? area.Width - DropZoneDepth : 0;
            return new RectD(x, y, DropZoneDepth, DropZoneLength);
        }
        var left = Math.Clamp(cursor.X - DropZoneLength / 2, 0, Math.Max(0, area.Width - DropZoneLength));
        var top = edge == ScreenEdge.Top ? 0 : area.Height - DropZoneDepth;
        return new RectD(left, top, DropZoneLength, DropZoneDepth);
    }

    /// <summary>The offset that lands the pill centred on the cursor on a new edge.</summary>
    public static double LandingOffset(ScreenEdge target, PointD mouse, SizeD area) =>
        target.IsVertical() ? mouse.Y - area.Height / 2 : mouse.X - area.Width / 2;
}

/// <summary>Which display the pill sits on. A missing display falls back to the main one until it returns.</summary>
public static class DisplayChoice
{
    /// <summary>Index of the chosen display in <paramref name="available"/>, where the first entry is the main display.</summary>
    public static int? Index(string saved, IReadOnlyList<string?> available)
    {
        if (available.Count == 0) return null;
        if (saved.Length == 0) return 0;
        for (var index = 0; index < available.Count; index++)
        {
            if (available[index] == saved) return index;
        }
        return 0;
    }
}
