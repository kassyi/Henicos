#nullable enable
using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of properties whose accessors use the <c>field</c> keyword, one case per form: the accessors run
/// as in C#, the backing field counts in the layout, and the launcher transfers the stored value without running the
/// accessors.
/// </summary>
public class FieldKeywordPropertyDifferentialTests
{
    private const int Rows = 441;

    internal static readonly DifferentialCase s_setterOnly = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordSetterOnly,
        FieldKeywordPropertyDifferentialKernels.SetterOnlyColumns);

    internal static readonly DifferentialCase s_getterOnly = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordGetterOnly,
        FieldKeywordPropertyDifferentialKernels.GetterOnlyColumns);

    internal static readonly DifferentialCase s_compound = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordCompound,
        FieldKeywordPropertyDifferentialKernels.CompoundColumns);

    internal static readonly DifferentialCase s_blockBodies = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordBlockBodies,
        FieldKeywordPropertyDifferentialKernels.BlockBodiesColumns);

    internal static readonly DifferentialCase s_steps = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordSteps,
        FieldKeywordPropertyDifferentialKernels.StepColumns);

    internal static readonly DifferentialCase s_init = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordInit,
        FieldKeywordPropertyDifferentialKernels.InitColumns);

    internal static readonly DifferentialCase s_with = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordWith,
        FieldKeywordPropertyDifferentialKernels.WithColumns);

    internal static readonly DifferentialCase s_constructor = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordConstructor,
        FieldKeywordPropertyDifferentialKernels.ConstructorColumns);

    internal static readonly DifferentialCase s_constructorStep = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordConstructorStep,
        FieldKeywordPropertyDifferentialKernels.ConstructorStepColumns);

    internal static readonly DifferentialCase s_mutatingGetter = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordMutatingGetter,
        FieldKeywordPropertyDifferentialKernels.MutatingGetterColumns);

    internal static readonly DifferentialCase s_computedReader = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordComputedReader,
        FieldKeywordPropertyDifferentialKernels.ComputedReaderColumns);

    internal static readonly DifferentialCase s_partial = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordPartial,
        FieldKeywordPropertyDifferentialKernels.PartialColumns);

    internal static readonly DifferentialCase s_deconstruction = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordDeconstruction,
        FieldKeywordPropertyDifferentialKernels.DeconstructionColumns);

    internal static readonly DifferentialCase s_library = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordLibrary,
        FieldKeywordPropertyDifferentialKernels.LibraryColumns);

    internal static readonly DifferentialCase s_libraryMacroTarget = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordLibraryMacroTarget,
        FieldKeywordPropertyDifferentialKernels.LibraryMacroColumns,
        static (i, r) =>
        {
            // The operator macro has no host body, so the reference adds the values the target adds
            for (int k = 0; k < i.Length; k++)
            {
                r[k] = (i[k] % 1000) * 2;
            }
        });

    internal static readonly DifferentialCase s_order = IntCase(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordOrder,
        FieldKeywordPropertyDifferentialKernels.OrderColumns);

    private static readonly IElementComparer<DiffFieldKeywordPacked> s_packedComparer =
        new StructElementComparer<DiffFieldKeywordPacked>(static (ctx, o, a) =>
        {
            ctx.Field(nameof(DiffFieldKeywordPacked.A), o.A, a.A);
            ctx.Field(nameof(DiffFieldKeywordPacked.P), o.P, a.P);
            ctx.Field(nameof(DiffFieldKeywordPacked.B), o.B, a.B);
            ctx.Field(nameof(DiffFieldKeywordPacked.Q), o.Q, a.Q);
        });

    internal static readonly DifferentialCase s_blockCopy = DiffCases.Create<DiffFieldKeywordPacked, long>(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordPropertyBlockCopy,
        inputs: static (rng, rows) =>
        {
            var values = DiffValues.Int32(rng, rows);
            var items = new DiffFieldKeywordPacked[rows];
            for (int i = 0; i < rows; i++)
            {
                items[i].A = (byte)values[i];
                items[i].P = values[i] % 100000;
                items[i].B = values[i] / 3;
                items[i].Q = (short)(values[i] >> 8);
            }

            return new DiffBuffers()
                .In("items", items, comparer: s_packedComparer)
                .Out<long>("r", rows, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        inputName: "items",
        outputName: "r");

    internal static readonly DifferentialCase s_memberwise = DiffCases.Create<DiffFieldKeywordWithArray, int>(
        FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordPropertyMemberwise,
        inputs: static (rng, rows) =>
        {
            var values = DiffValues.Int32(rng, rows);
            var items = new DiffFieldKeywordWithArray[rows];
            for (int i = 0; i < rows; i++)
            {
                items[i].Data = [values[i] % 1000];
                items[i].P = values[i] % 10000;
            }

            return new DiffBuffers()
                .InDeep("items", items, CloneWithArray, CompareWithArray)
                .Out<int>("r", rows, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        inputName: "items",
        outputName: "r");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void FieldKeywordSetter_ImplementedGetter_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_setterOnly);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void FieldKeywordGetter_ImplementedSetter_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_getterOnly);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void FieldKeywordProperty_CompoundAssignment_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_compound);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordProperty_BlockBodies_MatchTheOracleOnEveryPath() => DifferentialRunner.Run(s_blockBodies);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void FieldKeywordProperty_PrefixAndPostfixSteps_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_steps);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void FieldKeywordInit_ObjectInitializer_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_init);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void FieldKeywordProperty_WithExpression_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_with);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordProperty_InitializerAndSetterlessConstructorWrite_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_constructor);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordProperty_SetterlessStepsInConstructor_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_constructorStep);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordGetter_WritesItsField_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mutatingGetter);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void FieldKeywordProperty_ReadByComputedProperty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_computedReader);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordProperty_PartialLikeAutoProperty_MatchesTheOracleAndTheAutoProperty() =>
        DifferentialRunner.Run(s_partial);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordProperty_Deconstruction_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_deconstruction);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void FieldKeywordProperty_StructFromLibrary_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_library);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void OperatorMacro_LibraryAutoPropertyTarget_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_libraryMacroTarget);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordGetter_ReadThenCallThatWritesIt_KeepsTheCSharpOrder() => DifferentialRunner.Run(s_order);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordProperty_StructArrayCopiedAsOneBlock_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_blockCopy);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordProperty_StructArrayConvertedMemberByMember_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_memberwise);

    // A case of a kernel (int[] inputs, int[] r) with one row of columns per input
    // The launch is the oracle, unless the case gives a reference model of the inputs and the results
    private static DifferentialCase IntCase(
        Int32PairLaunch launch,
        string[] columns,
        Action<int[], int[]>? reference = null,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (rng, rows) => new DiffBuffers()
                .In("inputs", DiffValues.Int32(rng, rows))
                .Out<int>("r", rows, columns, OutputPolicy.Exact),
            shapes: LaunchShapes.OneDimensionalMatrix(Rows),
            inputName: "inputs",
            outputName: "r",
            reference: reference is null ? null : (b, _) => reference(b.Get<int>("inputs"), b.Get<int>("r")),
            launchExpression: launchExpression);

    // [WHY] A copy of the struct keeps the stored value; an object initializer would run the accessors
    private static DiffFieldKeywordWithArray CloneWithArray(DiffFieldKeywordWithArray source, DiffArrayCloneScope scope)
    {
        var copy = source;
        copy.Data = scope.CloneArray(source.Data) ?? [];
        return copy;
    }

    private static void CompareWithArray(StructFieldCheckContext ctx, DiffFieldKeywordWithArray o,
        DiffFieldKeywordWithArray a)
    {
        ctx.Field(nameof(DiffFieldKeywordWithArray.P), o.P, a.P);
        ctx.Field("Data.Length", o.Data.Length, a.Data.Length);
        for (int i = 0; i < Math.Min(o.Data.Length, a.Data.Length); i++)
        {
            ctx.Field($"Data[{i}]", o.Data[i], a.Data[i]);
        }
    }
}
