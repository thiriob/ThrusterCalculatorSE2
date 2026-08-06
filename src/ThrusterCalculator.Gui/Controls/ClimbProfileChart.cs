using System;
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
/// Spare acceleration against altitude, with altitude running up the vertical axis.
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
/// <para>
/// <b>The curve is faint wherever the ship is falling</b> — left of the zero line. Everything
/// plotted is a hovering analysis, "if the ship were at this height, could it hold itself up?", so
/// the faint stretches are heights where the answer is no. Drawn dashed rather than dropped: for a
/// mixed loadout the line climbing back out of the dip is precisely what says "more ion and this
/// becomes reachable", and a truncated line would hide the reason.
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

    /// <summary>
    /// Where the ship runs out of climb, as a fraction of the plot, or <c>null</c> if it never does.
    /// </summary>
    /// <remarks>
    /// Supplied rather than derived. The chart used to find its own zero crossing, which was both a
    /// second copy of the profiler's arithmetic and the wrong number — the ship stops where its
    /// momentum runs out, not where its lift does, and those are different heights.
    /// </remarks>
    public static readonly StyledProperty<double?> StopAltitudeProperty =
        AvaloniaProperty.Register<ClimbProfileChart, double?>(nameof(StopAltitude));

    static ClimbProfileChart()
    {
        AffectsRender<ClimbProfileChart>(
            SamplesProperty, TargetSamplesProperty, BandsProperty, MaxRatioProperty,
            StopAltitudeProperty);
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

    public double? StopAltitude
    {
        get => GetValue(StopAltitudeProperty);
        set => SetValue(StopAltitudeProperty, value);
    }

    private static readonly IBrush Axis = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
    private static readonly IBrush Label = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
    private static readonly IBrush Curve = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));
    private static readonly IBrush Hover = new SolidColorBrush(Color.FromRgb(0xD0, 0x8A, 0x3E));
    private static readonly IBrush Target = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));

    /// <summary>The climb above the ceiling: real physics at heights the ship cannot reach.</summary>
    private static readonly IBrush Unreachable =
        new SolidColorBrush(Color.FromArgb(70, 0x4C, 0x9A, 0xFF));

    /// <summary>Room for the altitude labels on the left, and ticks plus a title underneath.</summary>
    private const double LeftGutter = 96;
    private const double BottomGutter = 38;
    private const double TopPad = 8;
    private const double RightPad = 20;

    /// <summary>
    /// Ticks to aim for. The step is rounded to a readable one afterwards, so this is a target and
    /// not a count.
    /// </summary>
    /// <remarks>
    /// Eight rather than six because rounding only ever coarsens the step, never refines it: a
    /// target of six turned a −10…25 range into ticks at 0, 10 and 20, which is barely a scale.
    /// </remarks>
    private const int TargetTickCount = 8;

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

        // Numbers along the acceleration axis, because the scale moves: swapping a thruster family
        // can change spare acceleration by an order of magnitude, and two curves that look
        // identical then are not. Zero alone cannot carry that.
        DrawTicks(context, minValue, maxValue, plotWidth, plotHeight, At);

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

        // The margin asked for is a curve, not a line: it shrinks as gravity does.
        DrawCurve(context, TargetSamples, new Pen(Target, 1, new DashStyle([3, 3], 0)), false, At);

        DrawCurve(context, samples, new Pen(Curve, 2), true, At);

        // Where the ship actually runs out of climb — marked *on the curve*, not on the zero line,
        // because it stops while still decelerating and so at negative spare acceleration. Putting
        // the dot at zero would point at a height it sailed straight past.
        if (StopAltitude is { } altitude)
        {
            context.DrawEllipse(Hover, null, At(ValueAt(samples, altitude), altitude), 4, 4);
        }

        Write(context, "spare acceleration  m/s²",
            new Point(LeftGutter + (plotWidth / 2) - 60, Bounds.Height - 14), 11, Label);
    }

    /// <summary>Graduations along the acceleration axis, at readable round numbers.</summary>
    private static void DrawTicks(
        DrawingContext context, double minValue, double maxValue, double plotWidth,
        double plotHeight, Func<double, double, Point> at)
    {
        var step = NiceStep(maxValue - minValue);
        if (step <= 0) return;

        // Stepping from a multiple of the step means zero always lands exactly on a tick, so the
        // graduations and the orange floor line agree rather than sitting a pixel apart.
        var format = step < 1 ? "0.##" : "0";
        var gridPen = new Pen(Axis, 1, new DashStyle([1, 5], 0));

        for (var value = Math.Ceiling(minValue / step) * step; value <= maxValue; value += step)
        {
            var x = at(value, 0).X;

            // Zero gets its own louder line later; a grey one underneath would just fatten it.
            if (Math.Abs(value) > step / 2)
            {
                context.DrawLine(gridPen, new Point(x, TopPad), new Point(x, TopPad + plotHeight));
            }

            var text = value.ToString(format, CultureInfo.CurrentCulture);

            // Roughly centred on the tick, and never allowed to run off either end of the plot.
            var width = text.Length * 6.0;
            var left = Math.Clamp(x - (width / 2), LeftGutter - 8, LeftGutter + plotWidth - width + 8);

            Write(context, text, new Point(left, TopPad + plotHeight + 4), 11, Label);
        }
    }

    /// <summary>A round step — 1, 2 or 5 times a power of ten — near the range over the tick target.</summary>
    private static double NiceStep(double range)
    {
        if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) return 0;

        var raw = range / TargetTickCount;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalised = raw / magnitude;

        var multiple = normalised switch
        {
            <= 1 => 1.0,
            <= 2 => 2.0,
            <= 5 => 5.0,
            _ => 10.0,
        };

        return multiple * magnitude;
    }

    /// <summary>Spare acceleration at an altitude, interpolated between the samples around it.</summary>
    private static double ValueAt(IReadOnlyList<ClimbSample> samples, double altitude)
    {
        if (altitude <= samples[0].Altitude) return samples[0].NetAcceleration;

        for (var i = 1; i < samples.Count; i++)
        {
            if (samples[i].Altitude < altitude) continue;

            var span = samples[i].Altitude - samples[i - 1].Altitude;
            var t = span > 0 ? (altitude - samples[i - 1].Altitude) / span : 0.0;

            return samples[i - 1].NetAcceleration
                   + (t * (samples[i].NetAcceleration - samples[i - 1].NetAcceleration));
        }

        return samples[^1].NetAcceleration;
    }

    /// <summary>
    /// Draws the polyline, faint wherever the ship cannot hold itself up.
    /// </summary>
    /// <remarks>
    /// <b>The rule is the sign, not the height.</b> An earlier version faded everything above the
    /// ceiling, which read correctly for an atmospheric ship and uselessly for an ion one: ion
    /// crosses zero going <em>up</em>, so its ceiling sits at ground level and the whole curve went
    /// faint — hiding the one thing worth seeing, which is that it flies perfectly well higher up.
    /// <para>
    /// Keying off the sign makes the two symmetric, and makes a mixed loadout legible for free: the
    /// line is solid low, faint through the handover dip, and solid again once ion takes over.
    /// </para>
    /// </remarks>
    private static void DrawCurve(
        DrawingContext context, IReadOnlyList<ClimbSample>? samples, Pen pen, bool fadeWhenFalling,
        Func<double, double, Point> at)
    {
        if (samples is null) return;

        var faint = new Pen(Unreachable, pen.Thickness, new DashStyle([4, 4], 0));
        Pen For(double value) => !fadeWhenFalling || value >= 0 ? pen : faint;

        // Segment by segment: no geometry builder needed for a polyline.
        for (var i = 1; i < samples.Count; i++)
        {
            var (fromValue, fromAltitude) = (samples[i - 1].NetAcceleration, samples[i - 1].Altitude);
            var (toValue, toAltitude) = (samples[i].NetAcceleration, samples[i].Altitude);

            // A segment straddling zero is split on the crossing, so the change of pen lands exactly
            // on the floor line rather than up to one sample away from it.
            if (fadeWhenFalling && (fromValue < 0) != (toValue < 0) && fromValue != toValue)
            {
                var t = fromValue / (fromValue - toValue);
                var crossing = at(0, fromAltitude + (t * (toAltitude - fromAltitude)));

                context.DrawLine(For(fromValue), at(fromValue, fromAltitude), crossing);
                context.DrawLine(For(toValue), crossing, at(toValue, toAltitude));
                continue;
            }

            context.DrawLine(For(fromValue),
                at(fromValue, fromAltitude), at(toValue, toAltitude));
        }
    }

    private static void Write(DrawingContext context, string text, Point at, double size, IBrush brush) =>
        context.DrawText(
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, size, brush),
            at);
}
