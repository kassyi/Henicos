namespace Kassyi.GSharp.Compiler.Models;

#pragma warning disable SA1402 // ManagedBarrierModel enum and its MSBuild property reader live together
/// <summary>The execution model for block barriers on the managed CPU path.</summary>
internal enum ManagedBarrierModel
{
    /// <summary>The default state-machine lowering and step-by-step launcher.</summary>
    StateMachine,

    /// <summary>One OS thread per GPU thread running the unlowered body with explicit block barriers.</summary>
    Stack
}

/// <summary>Reads the MSBuild property <c>GSharpManagedBarrierModel</c> for the generator.</summary>
internal static class ManagedBarrierModels
{
    /// <summary>The analyzer config key of <c>GSharpManagedBarrierModel</c>.</summary>
    public const string Property = "build_property.GSharpManagedBarrierModel";

    /// <summary>
    /// Reads <see cref="Property"/> from <paramref name="options"/>: <see cref="ManagedBarrierModel.Stack"/> when the
    /// value is <c>Stack</c> (case-insensitive), and <see cref="ManagedBarrierModel.StateMachine"/> otherwise.
    /// </summary>
    public static ManagedBarrierModel Read(AnalyzerConfigOptions options) =>
        options.TryGetValue(Property, out var value) &&
        value.AsSpan().Trim().Equals("Stack".AsSpan(), StringComparison.OrdinalIgnoreCase)
            ? ManagedBarrierModel.Stack
            : ManagedBarrierModel.StateMachine;
}
#pragma warning restore SA1402
