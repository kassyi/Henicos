#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for C# generics, nested types, extension methods, reduced extension method groups,
/// tuple deconstruction, <c>readonly record struct</c>, and pattern matching (Lane D6 Stage 1).
/// </summary>
public class LanguageGenericsAndTypesDifferentialTests
{
    private const int Rows = 256;

    private static readonly DifferentialCase s_genericStructsAndNestedTypes = new(
        $"{nameof(LanguageGenericsAndTypesDifferentialKernels)}.{nameof(LanguageGenericsAndTypesDifferentialKernels.GenericStructsAndNestedTypesKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, LanguageGenericsAndTypesDifferentialKernels.GenericStructsAndNestedTypesColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageGenericsAndTypesDifferentialKernels.LaunchGenericStructsAndNestedTypesKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => LanguageGenericsAndTypesDifferentialKernels.GenericStructsAndNestedTypesKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_extensionMethodsAndDelegates = new(
        $"{nameof(LanguageGenericsAndTypesDifferentialKernels)}.{nameof(LanguageGenericsAndTypesDifferentialKernels.ExtensionMethodsAndDelegatesKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, LanguageGenericsAndTypesDifferentialKernels.ExtensionMethodsAndDelegatesColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageGenericsAndTypesDifferentialKernels.LaunchExtensionMethodsAndDelegatesKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => LanguageGenericsAndTypesDifferentialKernels.ExtensionMethodsAndDelegatesKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_deconstructionRecordsAndPatterns = new(
        $"{nameof(LanguageGenericsAndTypesDifferentialKernels)}.{nameof(LanguageGenericsAndTypesDifferentialKernels.DeconstructionRecordsAndPatternsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, LanguageGenericsAndTypesDifferentialKernels.DeconstructionRecordsAndPatternsColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageGenericsAndTypesDifferentialKernels.LaunchDeconstructionRecordsAndPatternsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => LanguageGenericsAndTypesDifferentialKernels.DeconstructionRecordsAndPatternsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GenericStructsAndNestedTypes_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_genericStructsAndNestedTypes);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ExtensionMethodsAndDelegates_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_extensionMethodsAndDelegates);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeconstructionRecordsAndPatterns_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_deconstructionRecordsAndPatterns);
}
