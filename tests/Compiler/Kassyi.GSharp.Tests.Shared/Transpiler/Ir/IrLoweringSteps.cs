#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Reconverging;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>Runs the IR lowering pipeline up to reconvergence and linearization for IR tests.</summary>
internal sealed record IrLoweringSteps(Compilation Compilation, IrMethod Method, IrSplitLoweredBody Lowered,
    IrReconvergedBody Reconverged, ImmutableArray<IrStatement> Linear)
{
    public const string Cases = "Ir/IrReconvergenceCases.cs";
    public const string CasesType = "IrReconvergence.IrReconvergenceCases";

    /// <summary>Builds, classifies, applies split plans, inserts reconvergence regions, and linearizes.</summary>
    public static IrLoweringSteps Run(string testCase, string type, string method, bool isDeviceFunction)
    {
        var compilation = IrTestCases.Compile(testCase);
        var built = IrTestCases.Build(testCase, type, method);
        var classified = IrClassifier.Classify(built, compilation);
        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);
        Assert.Equal(IrSplitLoweringFailureKind.None, failure.Kind);
        Assert.NotNull(lowered);
        var reconverged = IrReconvergence.Apply(classified, lowered, compilation, isDeviceFunction);
        var linear = IrLinearizer.Linearize(reconverged.Body, compilation);
        return new IrLoweringSteps(compilation, classified, lowered, reconverged, linear);
    }
}
