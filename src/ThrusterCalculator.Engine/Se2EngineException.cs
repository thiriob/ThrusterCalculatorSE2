namespace ThrusterCalculator.Engine;

/// <summary>
/// Thrown when the game's assemblies or cache cannot be loaded or read.
/// </summary>
/// <remarks>
/// Callers are expected to catch this and carry on without engine data rather than fail the run:
/// engine hosting is an enrichment, never a prerequisite (Design.md P5).
/// </remarks>
public sealed class Se2EngineException : Exception
{
    public Se2EngineException(string message) : base(message) { }

    public Se2EngineException(string message, Exception? inner) : base(message, inner) { }
}
