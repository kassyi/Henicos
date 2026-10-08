namespace Kassyi.GSharp.Tests.Common;

/// <summary>
/// Specifies the behavioral scenario or condition of the test case.
/// </summary>
public enum TestScenario
{
    /// <summary>Nominal execution flow with valid inputs and expected outcomes.</summary>
    HappyPath,

    /// <summary>Invalid input, diagnostic reporting, or expected exceptions.</summary>
    SadPath,

    /// <summary>Boundary conditions, edge cases, zero/max limits, and extreme inputs.</summary>
    Boundary
}
