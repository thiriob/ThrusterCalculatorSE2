using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ThrusterCalculator.Gui.Controls;

/// <summary>One point on the climb: the thrust-to-weight available at a given height.</summary>
/// <param name="Altitude">Height as a fraction of the climb, 0 at the ground, 1 past the gravity well.</param>
public sealed record ClimbSample(double Altitude, double ThrustToWeight);

/// <summary>
/// Thrust-to-weight against altitude, with altitude running up the vertical axis.
/// </summary>
/// <remarks>
/// Drawn directly rather than with a charting package. Avalonia ships no chart control, but it does
/// ship a full 2D drawing surface, and one curve with two reference lines does not justify the
/// dependency — Technic §6 keeps the frontend to CommunityToolkit.Mvvm until something demands
/// more.
/// <para>
/// <b>Altitude is the vertical axis on purpose.</b> The reader is following a climb, so the picture
/// should agree with the thing it describes: up is up. That puts thrust-to-weight on the horizontal
/// axis, which conveniently makes the <c>1.0</c> threshold a vertical line — everything to its left
/// is a ship that cannot climb, and the height where the curve crosses it is the ceiling.
/// </para>
/// </remarks>
public class ClimbProfileChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ClimbSample>?> SamplesProperty =
        AvaloniaProperty.Register<ClimbProfileChart, IReadOnlyList<ClimbSample>?>(nameof(Samples));

    public static readonly StyledProperty<double> TargetRatioProperty =
        AvaloniaProperty.Register<ClimbProfileChart, double>(nameof(TargetRatio), 1.5);

    /// <summary>Named heights drawn as gridlines, since planet radii mean nothing to a player.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> BandLabelsProperty =
        AvaloniaProperty.Register<ClimbProfileChart, IReadOnlyList<string>?>(nameof(BandLabels));

    static ClimbProfileChart()
    {
        AffectsRender<ClimbProfileChart>(SamplesProperty, TargetRatioProperty, BandLabelsProperty);
    }

    public IReadOnlyList<ClimbSample>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public double TargetRatio
    {
        get => GetValue(TargetRatioProperty);
        set => SetValue(TargetRatioProperty, value);
    }

    public IReadOnlyList<string>? BandLabels
    {
        get => GetValue(BandLabelsProperty);
        set => SetValue(BandLabelsProperty, value);
    }

    private static readonly IBrush Axis = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
    private static readonly IBrush Label = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
    private static readonly IBrush Curve = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));
    private static readonly IBrush Hover = new SolidColorBrush(Color.FromRgb(0xD0, 0x8A, 0x3E));
    private static readonly IBrush Target = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));

    /// <summary>Room for the altitude labels on the left and the ratio labels underneath.</summary>
    private const double LeftGutter = 96;
    private const double BottomGutter = 22;
    private const double TopPad = 8;
    private const double RightPad = 12;

    public override void Render(DrawingContext context)
    {
        var samples = Samples;
        if (samples is null || samples.Count < 2) return;

        var plotWidth = Bounds.Width - LeftGutter - RightPad;
        var plotHeight = Bounds.Height - BottomGutter - TopPad;
        if (plotWidth <= 10 || plotHeight <= 10) return;

        // Always show at least up to the target, so the target line never falls off the edge.
        var maxRatio = Math.Max(samples.Max(s => s.ThrustToWeight), TargetRatio) * 1.15;

        Point At(double ratio, double altitude) => new(
            LeftGutter + (Math.Clamp(ratio / maxRatio, 0, 1) * plotWidth),
            TopPad + ((1 - Math.Clamp(altitude, 0, 1)) * plotHeight));

        var axisPen = new Pen(Axis, 1);

        // Frame.
        context.DrawLine(axisPen, At(0, 0), At(maxRatio, 0));
        context.DrawLine(axisPen, At(0, 0), At(0, 1));

        // Named heights instead of numbers: "atmosphere edge" is a thing a player can picture.
        var labels = BandLabels ?? [];
        for (var i = 0; i < labels.Count; i++)
        {
            var altitude = labels.Count == 1 ? 0 : (double)i / (labels.Count - 1);
            var y = At(0, altitude).Y;

            context.DrawLine(new Pen(Axis, 1, new DashStyle([2, 4], 0)),
                new Point(LeftGutter, y), new Point(LeftGutter + plotWidth, y));

            Write(context, labels[i], new Point(4, y - 8), 11, Label);
        }

        // The hard ceiling: left of this line the ship cannot climb at all.
        var hoverPen = new Pen(Hover, 1.5);
        context.DrawLine(hoverPen, At(1, 0), At(1, 1));
        Write(context, "1.0", new Point(At(1, 0).X - 8, Bounds.Height - BottomGutter + 4), 11, Hover);

        // The margin the user actually asked for.
        if (TargetRatio > 0)
        {
            context.DrawLine(new Pen(Target, 1, new DashStyle([3, 3], 0)),
                At(TargetRatio, 0), At(TargetRatio, 1));

            Write(context, TargetRatio.ToString("0.0", CultureInfo.InvariantCulture),
                new Point(At(TargetRatio, 0).X - 8, Bounds.Height - BottomGutter + 4), 11, Label);
        }

        // The curve itself, segment by segment: no geometry builder needed for a polyline.
        var curvePen = new Pen(Curve, 2);
        for (var i = 1; i < samples.Count; i++)
        {
            context.DrawLine(curvePen,
                At(samples[i - 1].ThrustToWeight, samples[i - 1].Altitude),
                At(samples[i].ThrustToWeight, samples[i].Altitude));
        }

        // Where the curve crosses 1.0 is the ceiling — mark it, because it is the answer.
        for (var i = 1; i < samples.Count; i++)
        {
            var below = samples[i].ThrustToWeight;
            var above = samples[i - 1].ThrustToWeight;
            if (above < 1 || below >= 1) continue;

            var t = (above - 1) / (above - below);
            var altitude = samples[i - 1].Altitude + (t * (samples[i].Altitude - samples[i - 1].Altitude));
            var point = At(1, altitude);

            context.DrawEllipse(Hover, null, point, 4, 4);
            break;
        }

        Write(context, "thrust ÷ weight",
            new Point(LeftGutter + (plotWidth / 2) - 40, Bounds.Height - BottomGutter + 4), 11, Label);
    }

    private static void Write(DrawingContext context, string text, Point at, double size, IBrush brush) =>
        context.DrawText(
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, size, brush),
            at);
}
