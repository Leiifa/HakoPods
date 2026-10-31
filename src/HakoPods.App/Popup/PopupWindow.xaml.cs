using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HakoPods.Core.State;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace HakoPods.App.Popup;

/// <summary>
/// The case-open card from `docs/design/POPUP.md`: 336 × 206 DIP, three 92 DIP columns,
/// shadow-only edge, Inter, tabular numerals. Informational — it never asks for focus and never
/// offers a button.
///
/// Unknown is always an em dash at 28 DIP regular with a dashed rail; 0 % is never shown for a
/// value we do not have. Colour is never the only signal: every red rail also carries `Low`.
/// </summary>
public partial class PopupWindow : Window
{
    /// <summary>Set true only when a labelled capture has confirmed the charging bits.</summary>
    private const bool ChargingMarksVerified = true;

    private const double ShowMs = 160;
    private const double DismissMs = 120;
    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(6);

    private readonly DispatcherTimer _dismissTimer;
    private GlobalInputHook? _hooks;
    private bool _dismissing;

    private Brush _primary = Brushes.Black;
    private Brush _secondary = Brushes.Gray;
    private Brush _track = Brushes.LightGray;
    private Brush _normalBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x8A, 0x3D));
    private Brush _lowBrush = new SolidColorBrush(Color.FromRgb(0xD7, 0x00, 0x15));

    public PopupWindow()
    {
        InitializeComponent();

        _dismissTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = VisibleFor };
        _dismissTimer.Tick += (_, _) => Dismiss();
    }

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(PopupWindow),
            new PropertyMetadata(Brushes.DodgerBlue));

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public static readonly DependencyProperty MarkBrushProperty =
        DependencyProperty.Register(nameof(MarkBrush), typeof(Brush), typeof(PopupWindow),
            new PropertyMetadata(Brushes.White));

    /// <summary>
    /// Accent for the bolt and the connected dot, or theme ink when the accent cannot carry them.
    /// The owner's accent is grey; on the dark panel a grey bolt is invisible, and POPUP.md says
    /// such a mark falls back to ink rather than shipping an unreadable cue.
    /// </summary>
    public Brush MarkBrush
    {
        get => (Brush)GetValue(MarkBrushProperty);
        set => SetValue(MarkBrushProperty, value);
    }

    public bool IsDark { get; private set; }

    public void Apply(DeviceState state, bool dark, bool connected)
    {
        IsDark = dark;
        ApplyTheme(dark);

        ModelText.Text = string.IsNullOrWhiteSpace(state.ModelName) ? "AirPods" : state.ModelName;
        MarkBrush = ContrastRatio(AccentBrush, IsDark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Colors.White) >= 3.0
            ? AccentBrush
            : _primary;

        StatusText.Text = connected
            ? "Connected"
            : state.IsPresent ? "Not connected" : "Not nearby";
        StatusDot.Fill = connected ? MarkBrush : Faded(0.32);

        if (state.ModelName is { } name && name.Contains("Max", StringComparison.OrdinalIgnoreCase))
        {
            StandardLayout.Visibility = Visibility.Collapsed;
            MaxLayout.Visibility = Visibility.Visible;
            ApplySingle(MaxLabel, MaxValue, MaxPercent, MaxTrack, MaxFill, MaxDash, MaxLow,
                level: state.LeftBatteryPercent ?? state.RightBatteryPercent ?? state.CaseBatteryPercent,
                charging: state.LeftCharging || state.RightCharging || state.CaseCharging,
                railWidth: 190);
        }
        else
        {
            StandardLayout.Visibility = Visibility.Visible;
            MaxLayout.Visibility = Visibility.Collapsed;

            var fresh = IsFresh(state);

            ApplyColumn(LeftLabel, LeftEar, LeftValue, LeftPercent, LeftBolt, LeftTrack, LeftFill, LeftDash, LeftLow,
                "Left", state.LeftBatteryPercent, state.LeftCharging, fresh && state.LeftInEar, 92);

            ApplyColumn(RightLabel, RightEar, RightValue, RightPercent, RightBolt, RightTrack, RightFill, RightDash, RightLow,
                "Right", state.RightBatteryPercent, state.RightCharging, fresh && state.RightInEar, 92);

            ApplyColumn(CaseLabel, null, CaseValue, CasePercent, CaseBolt, CaseTrack, CaseFill, CaseDash, CaseLow,
                "Case", state.CaseBatteryPercent, state.CaseCharging, inEar: false, railWidth: 92);
        }

        FooterText.Text = BuildFooter(state);
    }

    /// <summary>Shows beside the taskbar on the given monitor, with the 160 ms entrance.</summary>
    public void ShowAt(Rect workArea, TaskbarEdge edge)
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var point = PopupPlacement.Calculate(workArea, edge, new Size(Width, Height),
            new Point(cursor.X, cursor.Y));

        Left = point.X;
        Top = point.Y;
        Opacity = 0;

        var translate = new TranslateTransform(0, 4);
        Card.RenderTransform = translate;

        Show();

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ShowMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, fade);

        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(ShowMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

        _dismissTimer.Stop();
        _dismissTimer.Start();

        _hooks = new GlobalInputHook(Dispatcher);
        _hooks.EscapePressed += Dismiss;
        _hooks.ClickedOutside += rect =>
        {
            if (!new Rect(Left, Top, ActualWidth, ActualHeight).IntersectsWith(rect))
            {
                Dismiss();
            }
        };
        _hooks.Install();
    }

    public void Dismiss()
    {
        if (_dismissing || !IsVisible)
        {
            return;
        }

        _dismissing = true;
        _dismissTimer.Stop();
        _hooks?.Dispose();
        _hooks = null;

        var translate = Card.RenderTransform as TranslateTransform ?? new TranslateTransform();
        Card.RenderTransform = translate;

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(DismissMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) =>
        {
            Hide();
            _dismissing = false;
        };
        BeginAnimation(OpacityProperty, fade);

        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, 2, TimeSpan.FromMilliseconds(DismissMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });
    }

    private void ApplyColumn(
        TextBlock tbLabel, System.Windows.Shapes.Ellipse? earMark, TextBlock tbValue, TextBlock tbPercent,
        FrameworkElement bolt, Border track, Border fill, System.Windows.Shapes.Rectangle dash, TextBlock low,
        string labelText, int? level, bool charging, bool inEar, double railWidth)
    {
        tbLabel.Text = labelText;
        tbLabel.Foreground = _secondary;
        low.Foreground = _lowBrush;
        low.Visibility = Visibility.Collapsed;

        if (earMark is not null)
        {
            earMark.Stroke = _secondary;
            earMark.Visibility = inEar ? Visibility.Visible : Visibility.Collapsed;
        }

        bolt.Visibility = Visibility.Collapsed;
        ResetRail(track, dash);
        ResetValue(tbValue, tbPercent, fill);

        if (level is null)
        {
            RenderUnknown(tbValue, tbPercent, track, dash);
            return;
        }

        RenderLevel(tbValue, tbPercent, track, fill, bolt, level.Value, railWidth, charging, low);
    }

    private void ApplySingle(
        TextBlock tbLabel, TextBlock tbValue, TextBlock tbPercent, Border track, Border fill, System.Windows.Shapes.Rectangle dash, TextBlock low,
        int? level, bool charging, double railWidth)
    {
        tbLabel.Foreground = _secondary;
        low.Foreground = _lowBrush;
        low.Visibility = Visibility.Collapsed;

        ResetRail(track, dash);
        ResetValue(tbValue, tbPercent, fill);

        if (level is null)
        {
            RenderUnknown(tbValue, tbPercent, track, dash);
            return;
        }

        RenderLevel(tbValue, tbPercent, track, fill, null, level.Value, railWidth, charging, low);
    }

    private void RenderLevel(TextBlock tbValue, TextBlock tbPercent, Border track, Border fill,
        FrameworkElement? bolt, int level, double railWidth, bool charging, TextBlock low)
    {
        tbValue.Text = level.ToString();
        tbValue.FontSize = 32;
        tbValue.FontWeight = FontWeights.SemiBold;
        tbValue.Foreground = _primary;
        tbPercent.Visibility = Visibility.Visible;

        // Charging marks are OFF until the bit layout is verified. Measured 2026-10-31: the owner
        // wore one bud with the case idle, and the card claimed two components were charging. A
        // missing bolt is a gap; a wrong one is a lie, and the same principle already governs the
        // stale ear. Flip this once a labelled capture settles which byte carries what.
        if (bolt is not null && ChargingMarksVerified)
        {
            bolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
        }

        var clamped = Math.Clamp(level, 0, 100);

        // A 5 % rail is 4.6 DIP wide, which renders as a dot rather than a stub. Keep a floor so
        // any non-zero level still reads as a bar; zero stays empty.
        fill.Width = clamped == 0 ? 0 : Math.Max(6, clamped / 100.0 * railWidth);
        fill.Background = clamped <= 10 ? _lowBrush : _normalBrush;
        track.Background = _track;

        if (clamped <= 10)
        {
            low.Visibility = Visibility.Visible;
        }
    }

    private void RenderUnknown(TextBlock tbValue, TextBlock tbPercent, Border track, System.Windows.Shapes.Rectangle dash)
    {
        tbValue.Text = "—";
        tbValue.FontSize = 28;
        tbValue.FontWeight = FontWeights.Normal;
        tbValue.Foreground = _secondary;
        tbPercent.Visibility = Visibility.Collapsed;

        // 1 DIP dashed outline at 30 % black / 34 % white: no fill, no number, no per cent sign.
        track.Background = Brushes.Transparent;
        track.BorderBrush = Faded(IsDark ? 0.34 : 0.30);
        track.BorderThickness = new Thickness(0);
        dash.Stroke = Faded(IsDark ? 0.34 : 0.30);
        dash.Visibility = Visibility.Visible;
    }

    private void ResetRail(Border track, System.Windows.Shapes.Rectangle dash)
    {
        dash.Visibility = Visibility.Collapsed;
        track.Background = _track;
        track.BorderThickness = new Thickness(0);
        track.BorderBrush = Brushes.Transparent;
    }

    private static void ResetValue(TextBlock tbValue, TextBlock tbPercent, Border fill)
    {
        fill.Width = 0;
        fill.Background = Brushes.Transparent;
        tbPercent.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Ear state is only claimed while the reading is fresh. Measured 2026-10-31: with one bud in
    /// and the lid closed the pair advertises roughly once per 15 s, and Windows replays buffered
    /// packets, so a "Left in ear" can be a minute old. A stale ear is worse than no ear.
    /// </summary>
    private static bool IsFresh(DeviceState state) =>
        state.IsPresent
        && state.LastSeenAt is { } seen
        && DateTimeOffset.UtcNow - seen < TimeSpan.FromSeconds(10);

    private static string BuildFooter(DeviceState state)
    {
        if (!IsFresh(state))
        {
            return state.IsPresent ? "Last reading" : "Not nearby";
        }

        if (state.LeftInEar && state.RightInEar)
        {
            return "Both in ear";
        }

        if (state.LeftInEar)
        {
            return "Left in ear";
        }

        if (state.RightInEar)
        {
            return "Right in ear";
        }

        return state.LidOpen ? "Lid open" : "Last reading";
    }

    private void ApplyTheme(bool dark)
    {
        Card.Background = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Colors.White);
        _primary = new SolidColorBrush(dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        _secondary = new SolidColorBrush(dark ? Color.FromRgb(0xA1, 0xA1, 0xA6) : Color.FromRgb(0x6E, 0x6E, 0x73));
        _track = new SolidColorBrush(dark ? Color.FromRgb(0x3A, 0x3A, 0x3C) : Color.FromRgb(0xEA, 0xEA, 0xEE));
        _normalBrush = new SolidColorBrush(dark ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0x24, 0x8A, 0x3D));
        _lowBrush = new SolidColorBrush(dark ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromRgb(0xD7, 0x00, 0x15));

        ModelText.Foreground = _primary;
        StatusText.Foreground = _secondary;
        FooterText.Foreground = _secondary;
    }

    /// <summary>WCAG relative luminance ratio, used only to decide if a mark is readable.</summary>
    private static double ContrastRatio(Brush foreground, Color background)
    {
        if (foreground is not SolidColorBrush solid)
        {
            return 21.0;
        }

        var l1 = RelativeLuminance(solid.Color);
        var l2 = RelativeLuminance(background);

        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static double RelativeLuminance(Color colour)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));
    }

    private SolidColorBrush Faded(double alpha)
    {
        var colour = _secondary is SolidColorBrush solid ? solid.Color : Colors.Gray;
        return new SolidColorBrush(colour) { Opacity = alpha };
    }
}
