#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Reports a case the harness cannot judge: the oracle's output depends on the thread order (a data race in the kernel),
/// the oracle or reference model threw, or the case is malformed. It is a fault of the test, not of a path.
/// </summary>
public sealed class DifferentialHarnessException : Exception
{
    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public DifferentialHarnessException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public DifferentialHarnessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
