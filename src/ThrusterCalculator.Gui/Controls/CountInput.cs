using System.Globalization;
using Avalonia.Controls;

namespace ThrusterCalculator.Gui.Controls;

/// <summary>
/// A spinner for a whole number of things: how many thrusters, how many containers.
/// </summary>
/// <remarks>
/// The rules live here rather than being restated per field, for the same reason as
/// <see cref="MassInput"/>: a field that forgets one of them drifts out of step with the rest.
/// </remarks>
public class CountInput : NumericUpDown
{
    public CountInput()
    {
        Minimum = 0;
        Maximum = 9999;
        FormatString = "0";
        ParsingNumberStyle = NumberStyles.Integer;

        // Without this, a value outside the range is not brought back into it — typing 345678 left
        // the field showing 3456 rather than the 9999 it clamps to. Defaults to false, which is
        // almost never what a bounded numeric field wants.
        ClipValueToMinMax = true;

        // An emptied field settles back to its minimum once you leave it. Clearing the text writes
        // null, which is a real state while mid-edit — the binding has to accept it or Avalonia
        // paints a cast exception into the box — but a blank box left behind reads as broken. So it
        // resolves on the way out rather than fighting the keystroke that produced it.
        LostFocus += (_, _) => Value ??= Minimum;
    }

    protected override Type StyleKeyOverride => typeof(NumericUpDown);

}
