namespace Kassyi.GSharp.Core;

/// <summary>
/// Indicates that this GPU kernel should be skipped during automated snapshot consistency tests.
/// Use this for WIP kernels or kernels that require multi-file compilation (e.g. partial classes) 
/// which cannot be compiled in isolation by the snapshot generator.
/// </summary>
/// <param name="reason">The reason explaining why this kernel is excluded from automated snapshot consistency testing.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class SkipSnapshotConsistencyAttribute(string reason = "") : Attribute
{
    /// <summary>
    /// Gets the reason explaining why snapshot consistency checking is skipped.
    /// </summary>
    public string Reason { get; } = reason;
}

