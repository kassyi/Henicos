namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Contains constants for xUnit test collections to prevent magic strings and hallucination.
/// </summary>
public static class TestCollections
{
    /// <summary>
    /// Enforces strictly sequential execution for tests that mutate global state (e.g. NativeLibrary.Load, Environment variables).
    /// </summary>
    public const string GlobalStateSequential = "GlobalStateSequential";
}
