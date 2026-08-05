using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ThrusterCalculator.Gui.Controls;

/// <summary>One point on the climb: the spare acceleration available at a given height.</summary>
/// <param name="Altitude">Height as a fraction of the climb, 0 at the ground, 1 past the gravity well.</param>
/// <param name="NetAcceleration">Thrust over mass, less gravity, in m/s². Zero exactly hovers.</param>
public sealed record ClimbSample(double Altitude, double NetAcceleration);

/// <summary>A named height on the vertical axis, e.g. the atmosphere edge.</summary>
/// <param name="Altitude">Where it sits on the climb, 0 at the ground and 1 at the top.</param>
public sealed record ClimbBand(string Label, double Altitude);

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
/// should agree with the thing it describes: up is up. That puts the acceleration on the horizontal
/// axis, which makes zero a vertical line — everything to its left is a ship that is falling, and
/// the height where the curve crosses it is the ceiling.
/// </para>
/// <para>
/// <b>Spare acceleration, not thrust-to-weight.</b> TWR is the right question beside a planet and a
/// meaningless one away from it: weight goes to zero out of the gravity well, so every ship&apos;s
/// ratio runs to infinity and a nimble ship reads the same as a sluggish one. Subtracting gravity
/// instead of dividing by it keeps the number finite and keeps it meaning something — at the top of
/// the climb it settles at plain thrust over mass, which is exactly how quickly the ship picks up
/// speed in space.
/// </para>
/// </remarks>
public class ClimbProfileChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ClimbSample>?> SamplesProperty =
        AvaloniaProperty.Register<ClimbProfileChart, IReadOnlyList<ClimbSample>?>(nameof(Samples));

    /// <summary>The margin the user asked for, as its own curve: it falls away with gravity.</summary>
    public static readonly StyledProperty<IReadOnlyList<ClimbSample>?> TargetSamplesProperty =
        AvaloniaProperty.Register<ClimbProfileChart, IReadOnlyList<ClimbSample>?>(nameof(TargetSamples));

    /// <summary>Named heights drawn as gridlines, since planet radii mean nothing to a player.</summary>
    public static readonly StyledProperty<IReadOnlyList<ClimbBand>?> BandsProperty =
        AvaloniaProperty.Register<ClimbProfileChart, IReadOnlyList<ClimbBand>?>(nameof(Bands));

    /// <summary>Right-hand end of the acceleration axis, in m/s². Zero fits it to the data.</summary>
    public static readonly StyledProperty<double> MaxRatioProperty =
        AvaloniaProperty.Register<ClimbProfileChart, double>(nameof(MaxRatio));

    static ClimbProfileChart()
    {
        AffectsRender<ClimbProfileChart>(
            SamplesProperty, TargetSamplesProperty, BandsProperty, MaxRatioProperty);
    }

    public IReadOnlyList<ClimbSample>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public IReadOnlyList<ClimbSample>? TargetSamples
    {
        get => GetValue(TargetSamplesProperty);
        set => SetValue(TargetSamplesProperty, value);
    }

    public IReadOnlyList<ClimbBand>? Bands
    {
        get => GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    public double MaxRatio
    {
        get => GetValue(MaxRatioProperty);
        set => SetValue(MaxRatioProperty, value);
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

        // The axis runs from a little below zero — a stalling ship must have somewhere to go —
        // out to whatever ceiling was given, or the data if none was.
        var maxValue = MaxRatio > 0 ? MaxRatio : samples.Max(s => s.NetAcceleration) * 1.15;
        var minValue = Math.Min(-1.0, samples.Min(s => s.NetAcceleration) * 1.15);

        Point At(double value, double altitude) => new(
            LeftGutter + (Math.Clamp((value - minValue) / (maxValue - minValue), 0, 1) * plotWidth),
            TopPad + ((1 - Math.Clamp(altitude, 0, 1)) * plotHeight));

        var axisPen = new Pen(Axis, 1);

        // Frame.
        context.DrawLine(axisPen, At(minValue, 0), At(maxValue, 0));
        context.DrawLine(axisPen, At(minValue, 0), At(minValue, 1));

        // Named heights instead of numbers: "atmosphere edge" is a thing a player can picture.
        // Each carries its own altitude, because they are not evenly spaced — the atmosphere is a
        // thin skin against the depth of the gravity well.
        foreach (var band in Bands ?? [])
        {
            var y = At(minValue, band.Altitude).Y;

            context.DrawLine(new Pen(Axis, 1, new DashStyle([2, 4], 0)),
                new Point(LeftGutter, y), new Point(LeftGutter + plotWidth, y));

            Write(context, band.Label, new Point(4, y - 8), 11, Label);
        }

        // Zero is the hard floor: left of this line the ship is going down, whatever it wants.
        context.DrawLine(new Pen(Hover, 1.5), At(0, 0), At(0, 1));
        Write(context, "0", new Point(At(0, 0).X - 4, Bounds.Height - BottomGutter + 4), 11, Hover);

        // The margin asked for is a curve, not a line: it shrinks as gravity does.
        DrawCurve(context, TargetSamples, new Pen(Target, 1, new DashStyle([3, 3], 0)), At);

        DrawCurve(context, samples, new Pen(Curve, 2), At);

        // Where the curve crosses zero is the ceiling — mark it, because it is the answer.
        for (var i = 1; i < samples.Count; i++)
        {
            var above = samples[i - 1].NetAcceleration;
            var below = samples[i].NetAcceleration;
            if (above < 0 || below >= 0) continue;

            var t = above / (above - below);
            var altitude = samples[i - 1].Altitude + (t * (samples[i].Altitude - samples[i - 1].Altitude));

            context.DrawEllipse(Hover, null, At(0, altitude), 4, 4);
            break;
        }

        Write(context, "spare acceleration  m/s²",
            new Point(LeftGutter + (plotWidth / 2) - 60, Bounds.Height - BottomGutter + 4), 11, Label);
    }

    private static void DrawCurve(
        DrawingContext context, IReadOnlyList<ClimbSample>? samples, Pen pen,
        Func<double, double, Point> at)
    {
        if (samples is null) return;

        // Segment by segment: no geometry builder needed for a polyline.
        for (var i = 1; i < samples.Count; i++)
        {
            context.DrawLine(pen,
                at(samples[i - 1].NetAcceleration, samples[i - 1].Altitude),
                at(samples[i].NetAcceleration, samples[i].Altitude));
        }
    }

    private static void Write(DrawingContext context, string text, Point at, double size, IBrush brush) =>
        context.DrawText(
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, size, brush),
            at);
}
