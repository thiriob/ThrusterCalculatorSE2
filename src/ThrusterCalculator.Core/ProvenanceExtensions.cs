using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core;

public static class ProvenanceExtensions
{
    /// <summary>
    /// The least trustworthy of the given provenances — a result is only as good as its worst input.
    /// </summary>
    /// <remarks>
    /// Relies on <see cref="Provenance"/> being declared weakest-last
    /// (Measured &lt; Derived &lt; Assumed &lt; Unknown), so "weakest" is simply the maximum.
    /// <see cref="ProvenanceOrderTests"/> in the test suite pins that ordering so a future
    /// reordering of the enum cannot silently invert this.
    /// </remarks>
    public static Provenance Weakest(params ReadOnlySpan<Provenance> provenances)
    {
        var worst = Provenance.Measured;
        foreach (var p in provenances)
        {
            if (p > worst) worst = p;
        }

        return worst;
    }
}
