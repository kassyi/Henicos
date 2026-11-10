#nullable enable
using System;
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

    private const string Kernels = nameof(FieldKeywordPropertyDifferentialKernels);

    private static readonly DifferentialCase s_setterOnly = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordSetterOnly),
        FieldKeywordPropertyDifferentialKernels.SetterOnlyColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordSetterOnly(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordSetterOnly);

    private static readonly DifferentialCase s_getterOnly = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordGetterOnly),
        FieldKeywordPropertyDifferentialKernels.GetterOnlyColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordGetterOnly(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordGetterOnly);

    private static readonly DifferentialCase s_compound = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordCompound),
        FieldKeywordPropertyDifferentialKernels.CompoundColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordCompound(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordCompound);

    private static readonly DifferentialCase s_blockBodies = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordBlockBodies),
        FieldKeywordPropertyDifferentialKernels.BlockBodiesColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordBlockBodies(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordBlockBodies);

    private static readonly DifferentialCase s_steps = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordSteps),
        FieldKeywordPropertyDifferentialKernels.StepColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordSteps(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordSteps);

    private static readonly DifferentialCase s_init = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordInit),
        FieldKeywordPropertyDifferentialKernels.InitColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordInit(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordInit);

    private static readonly DifferentialCase s_with = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordWith),
        FieldKeywordPropertyDifferentialKernels.WithColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordWith(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordWith);

    private static readonly DifferentialCase s_constructor = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordConstructor),
        FieldKeywordPropertyDifferentialKernels.ConstructorColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordConstructor(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordConstructor);

    private static readonly DifferentialCase s_constructorStep = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordConstructorStep),
        FieldKeywordPropertyDifferentialKernels.ConstructorStepColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordConstructorStep(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordConstructorStep);

    private static readonly DifferentialCase s_mutatingGetter = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordMutatingGetter),
        FieldKeywordPropertyDifferentialKernels.MutatingGetterColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordMutatingGetter(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordMutatingGetter);

    private static readonly DifferentialCase s_computedReader = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordComputedReader),
        FieldKeywordPropertyDifferentialKernels.ComputedReaderColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordComputedReader(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordComputedReader);

    private static readonly DifferentialCase s_partial = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordPartial),
        FieldKeywordPropertyDifferentialKernels.PartialColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordPartial(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordPartial);

    private static readonly DifferentialCase s_deconstruction = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordDeconstruction),
        FieldKeywordPropertyDifferentialKernels.DeconstructionColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordDeconstruction(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordDeconstruction);

    private static readonly DifferentialCase s_library = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordLibrary),
        FieldKeywordPropertyDifferentialKernels.LibraryColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordLibrary(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        FieldKeywordPropertyDifferentialKernels.FieldKeywordLibrary);

    private static readonly DifferentialCase s_libraryMacroTarget = IntCase(
        nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordLibraryMacroTarget),
        FieldKeywordPropertyDifferentialKernels.LibraryMacroColumns,
        static (i, r, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordLibraryMacroTarget(
            i, r, gridDimX: s.GridX, blockDimX: s.BlockX),
        static (i, r) =>
        {
            // The operator macro has no host body, so the oracle adds the values the target adds
            for (int k = 0; k < i.Length; k++)
            {
                r[k] = (i[k] % 1000) * 2;
            }
        });

    private static readonly IElementComparer<DiffFieldKeywordPacked> s_packedComparer =
        new StructElementComparer<DiffFieldKeywordPacked>(static (ctx, o, a) =>
        {
            ctx.Field(nameof(DiffFieldKeywordPacked.A), o.A, a.A);
            ctx.Field(nameof(DiffFieldKeywordPacked.P), o.P, a.P);
            ctx.Field(nameof(DiffFieldKeywordPacked.B), o.B, a.B);
            ctx.Field(nameof(DiffFieldKeywordPacked.Q), o.Q, a.Q);
        });

    private static readonly DifferentialCase s_blockCopy = new(
        $"{Kernels}.{nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordPropertyBlockCopy)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) =>
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
        Launch = static (b, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordPropertyBlockCopy(
            b.Get<DiffFieldKeywordPacked>("items"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => FieldKeywordPropertyDifferentialKernels.FieldKeywordPropertyBlockCopy(
            b.Get<DiffFieldKeywordPacked>("items"),
            b.Get<long>("r")),
    };

    private static readonly DifferentialCase s_memberwise = new(
        $"{Kernels}.{nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordPropertyMemberwise)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) =>
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
        Launch = static (b, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordPropertyMemberwise(
            b.Get<DiffFieldKeywordWithArray>("items"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => FieldKeywordPropertyDifferentialKernels.FieldKeywordPropertyMemberwise(
            b.Get<DiffFieldKeywordWithArray>("items"),
            b.Get<int>("r")),
    };

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
    public void FieldKeywordProperty_StructArrayCopiedAsOneBlock_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_blockCopy);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void FieldKeywordProperty_StructArrayConvertedMemberByMember_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_memberwise);

    // A case of a kernel (int[] inputs, int[] r) with one row of columns per input
    private static DifferentialCase IntCase(string kernel, string[] columns,
        Action<int[], int[], LaunchShape> launch, Action<int[], int[]> oracle) =>
        new($"{Kernels}.{kernel}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
            Inputs = (rng, rows) => new DiffBuffers()
                .In("inputs", DiffValues.Int32(rng, rows))
                .Out<int>("r", rows, columns, OutputPolicy.Exact),
            Launch = (b, s) => launch(b.Get<int>("inputs"), b.Get<int>("r"), s),
            Oracle = (b, _) => oracle(b.Get<int>("inputs"), b.Get<int>("r")),
        };

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
