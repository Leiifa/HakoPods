using System.Windows;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace HakoPods.App.Popup;

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// Where the popup sits relative to the taskbar. Pure geometry, so the clamping rules can be
/// tested without a desktop — and they need testing, because a popup that lands half off-screen
/// on a 125 % secondary monitor is the classic regression.
///
/// Rules from `docs/design/POPUP.md`: 12 DIP inset from the taskbar, 8 DIP from the work-area
/// edge, always clamped to the work area, never crossing a monitor boundary.
/// </summary>
public static class PopupPlacement
{
    public const double TaskbarInset = 12;
    public const double WorkAreaInset = 8;

    /// <param name="workArea">Work area of the target monitor, in that monitor's DIPs.</param>
    /// <param name="taskbarEdge">Edge the taskbar occupies on that monitor.</param>
    /// <param name="size">Popup size in DIPs.</param>
    /// <param name="cursor">Cursor position, used to pick the horizontal/vertical alignment.</param>
    public static Point Calculate(Rect workArea, TaskbarEdge taskbarEdge, Size size, Point cursor)
    {
        double left;
        double top;

        switch (taskbarEdge)
        {
            case TaskbarEdge.Bottom:
                left = AlignToCursorX(workArea, size, cursor);
                top = workArea.Bottom - size.Height - TaskbarInset;
                break;

            case TaskbarEdge.Top:
                left = AlignToCursorX(workArea, size, cursor);
                top = workArea.Top + TaskbarInset;
                break;

            case TaskbarEdge.Left:
                left = workArea.Left + TaskbarInset;
                top = AlignToCursorY(workArea, size, cursor);
                break;

            default: // Right
                left = workArea.Right - size.Width - TaskbarInset;
                top = AlignToCursorY(workArea, size, cursor);
                break;
        }

        return new Point(
            Clamp(left, workArea.Left + WorkAreaInset, workArea.Right - size.Width - WorkAreaInset),
            Clamp(top, workArea.Top + WorkAreaInset, workArea.Bottom - size.Height - WorkAreaInset));
    }

    private static double AlignToCursorX(Rect workArea, Size size, Point cursor) =>
        Clamp(cursor.X - (size.Width / 2), workArea.Left + WorkAreaInset, workArea.Right - size.Width - WorkAreaInset);

    private static double AlignToCursorY(Rect workArea, Size size, Point cursor) =>
        Clamp(cursor.Y - (size.Height / 2), workArea.Top + WorkAreaInset, workArea.Bottom - size.Height - WorkAreaInset);

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Clamp(value, min, max);
}
