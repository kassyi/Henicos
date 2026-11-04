#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Represents the outcome of a post-launch property or invariant check (<see cref="DifferentialCase.Verify"/> or
/// <see cref="DifferentialCase.VerifyAgainstInitial"/>).
/// </summary>
/// <param name="Success">Whether the property check succeeded.</param>
/// <param name="FailureMessage">The diagnostic message when <paramref name="Success"/> is <see langword="false"/>.</param>
public readonly record struct PropertyResult(bool Success, string? FailureMessage = null)
{
    /// <summary>Gets a value indicating whether the property check passed.</summary>
    public bool IsPassed => Success;

    /// <summary>Gets the diagnostic failure message, if any.</summary>
    public string? Message => FailureMessage;

    /// <summary>Gets a passing <see cref="PropertyResult"/>.</summary>
    public static PropertyResult Passed => new(true, null);

    /// <summary>Returns a passing <see cref="PropertyResult"/>.</summary>
    public static PropertyResult Pass() => Passed;

    /// <summary>Returns a passing <see cref="PropertyResult"/>.</summary>
    public static PropertyResult Ok() => Passed;

    /// <summary>Creates a failing <see cref="PropertyResult"/> with the specified diagnostic <paramref name="message"/>.</summary>
    public static PropertyResult Failed(string message) => new(false, message);

    /// <summary>Creates a failing <see cref="PropertyResult"/> with the specified diagnostic <paramref name="message"/>.</summary>
    public static PropertyResult Fail(string message) => new(false, message);

    /// <summary>Converts a boolean outcome into a <see cref="PropertyResult"/>.</summary>
    public static implicit operator PropertyResult(bool success) =>
        success ? Passed : Failed("Property check returned false.");
}
