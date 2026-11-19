#nullable enable
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Shared factory for <see cref="IrDecisionContext"/> and split state machines in IR decision tests (#1561).
/// </summary>
internal static class IrDecisionTestContext
{
    /// <summary>Creates a context for deciding the pre-split body <paramref name="method"/>.</summary>
    public static IrDecisionContext Create(Compilation compilation, IrMethod method,
        ITranspilerDialect? nativeDialect = null) =>
        new(
            compilation,
            IrTemporaryInitializers.Create(method.Body),
            nativeDialect ?? HostDialects.Cpu,
            method);

    /// <summary>
    /// Creates a context for deciding <paramref name="machine"/> lowered from <paramref name="original"/>.
    /// </summary>
    public static IrDecisionContext Create(Compilation compilation, IrStateMachine machine, IrMethod original,
        ITranspilerDialect? nativeDialect = null) =>
        new(
            compilation,
            IrTemporaryInitializers.Create(machine),
            nativeDialect ?? HostDialects.Cpu,
            original);

    /// <summary>
    /// Builds, classifies, splits, linearizes, and lowers <paramref name="methodName"/> to a state machine.
    /// </summary>
    public static (IrMethod Built, IrStateMachine Machine) BuildSplitMachine(Compilation compilation,
        string testCase, string typeName, string methodName, bool isDeviceFunction = false)
    {
        var built = IrTestCases.Build(testCase, typeName, methodName);
        var classified = IrClassifier.Classify(built, compilation);
        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);
        Assert.NotNull(lowered);
        Assert.Equal(IrSplitLoweringFailureKind.None, failure.Kind);
        var linear = IrLinearizer.Linearize(lowered, compilation);
        var machine = IrStateMachineBuilder.Build(linear, isDeviceFunction, built.Body.Source, compilation);
        return (built, machine);
    }
}
