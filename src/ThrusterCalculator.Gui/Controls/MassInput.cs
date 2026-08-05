using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ThrusterCalculator.Gui.Controls;

/// <summary>
/// A numeric field for a mass in kilograms: grouped thousands, whole numbers only.
/// </summary>
/// <remarks>
/// Masses appear in several places and every one of them wants the same behaviour, so the rules
/// live here once rather than being restated as four attributes per field — and a field that
/// forgets one of them cannot drift out of step with the rest.
/// <para>
/// Whole numbers only because the unit is kilograms and the game reports whole kilograms. A
/// fractional kilogram is not a quantity anyone has, and allowing "." invites a decimal
/// separator that means different things under different locales — the kind of ambiguity that
/// silently turns 1.500 into one and a half.
/// </para>
/// </remarks>
public class MassInput : NumericUpDown
{
    public MassInput()
    {
        // N0: grouped thousands, no decimals. The parser below is what makes the grouped form
        // round-trip — without it, typing over a displayed "500,000" would fail to parse back.
        FormatString = "N0";
        Minimum = 0;
        Increment = 1000;
        ParsingNumberStyle = NumberStyles.Number;
    }

    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // Filtering at the text box is what stops the character reaching the buffer at all.
        // Validating after the fact would either reject a half-typed number or silently discard
        // it on blur, both of which read as the field being broken.
        if (e.NameScope.Find<TextBox>("PART_TextBox") is { } textBox)
        {
            textBox.AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        }
    }

    /// <summary>
    /// Accepts digits and group separators; rejects everything else before it is entered.
    /// </summary>
    /// <remarks>
    /// The separator is allowed through so a pasted or retyped "500,000" survives, and because
    /// <see cref="NumberStyles.Number"/> parses it back. Letters, spaces, signs and decimal points
    /// are dropped: none of them can begin a valid kilogram figure.
    /// </remarks>
    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;

        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;

        foreach (var character in e.Text)
        {
            if (char.IsAsciiDigit(character)) continue;
            if (separator.Length == 1 && character == separator[0]) continue;

            e.Handled = true;
            return;
        }
    }
}
