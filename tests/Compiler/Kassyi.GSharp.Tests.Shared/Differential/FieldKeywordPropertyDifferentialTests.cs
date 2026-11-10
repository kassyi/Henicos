#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of properties whose accessors use the <c>field</c> keyword: the accessors run as in C#, the
/// backing field counts in the layout, and the launcher transfers the stored value without running the accessors.
/// </summary>
public class FieldKeywordPropertyDifferentialTests
{
    private const int Rows = 441;

    private static readonly DifferentialCase s_accessors = new(
        $"{nameof(FieldKeywordPropertyDifferentialKernels)}.{nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordPropertyAccessors)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, FieldKeywordPropertyDifferentialKernels.AccessorColumns, OutputPolicy.Exact),
        Launch = static (b, s) => FieldKeywordPropertyDifferentialKernels.LaunchFieldKeywordPropertyAccessors(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => FieldKeywordPropertyDifferentialKernels.FieldKeywordPropertyAccessors(
            b.Get<int>("inputs"),
            b.Get<int>("r")),
    };

    private static readonly IElementComparer<DiffFieldKeywordPacked> s_packedComparer =
        new StructElementComparer<DiffFieldKeywordPacked>(static (ctx, o, a) =>
        {
            ctx.Field(nameof(DiffFieldKeywordPacked.A), o.A, a.A);
            ctx.Field(nameof(DiffFieldKeywordPacked.P), o.P, a.P);
            ctx.Field(nameof(DiffFieldKeywordPacked.B), o.B, a.B);
            ctx.Field(nameof(DiffFieldKeywordPacked.Q), o.Q, a.Q);
        });

    private static readonly DifferentialCase s_blockCopy = new(
        $"{nameof(FieldKeywordPropertyDifferentialKernels)}.{nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordPropertyBlockCopy)}")
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
        $"{nameof(FieldKeywordPropertyDifferentialKernels)}.{nameof(FieldKeywordPropertyDifferentialKernels.FieldKeywordPropertyMemberwise)}")
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
    public void FieldKeywordProperty_AccessorsOfEveryForm_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_accessors);

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
