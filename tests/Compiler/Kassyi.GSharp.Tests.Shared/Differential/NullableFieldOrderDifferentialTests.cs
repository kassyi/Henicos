#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for structs with a <c>Nullable&lt;S&gt;</c> field of another user struct (#1888).
/// </summary>
public class NullableFieldOrderDifferentialTests
{
    private const int Rows = 64;
    private const string Kernels = nameof(NullableFieldOrderDifferentialKernels);

    private static readonly IElementComparer<NullableOrderOuter> OuterComparer =
        new StructElementComparer<NullableOrderOuter>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(NullableOrderOuter.Tag), e.Tag, a.Tag, OutputPolicy.Exact);
            ctx.Equal(nameof(NullableOrderOuter.Value), e.Value.HasValue, a.Value.HasValue);
            ctx.Field("Value.A", e.Value.GetValueOrDefault().A, a.Value.GetValueOrDefault().A, OutputPolicy.Exact);
            ctx.Field("Value.B", e.Value.GetValueOrDefault().B, a.Value.GetValueOrDefault().B, OutputPolicy.Exact);
        });

    internal static readonly DifferentialCase s_readNullableStructField = new(
        $"{Kernels}.{nameof(NullableFieldOrderDifferentialKernels.ReadNullableStructFieldKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 32),
        Inputs = static (rng, s) =>
        {
            var tags = DiffValues.Int32(rng, s.Rows);
            var items = new NullableOrderOuter[s.Rows];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = new NullableOrderOuter
                {
                    Tag = tags[i],
                    Value = i % 3 == 0 ? null : new NullableOrderInner { A = tags[i] ^ 0x5A, B = i - 9 },
                };
            }

            return new DiffBuffers()
                .In("items", items, OutputPolicy.ForStruct(OuterComparer), OuterComparer)
                .Out<int>("r", s.Rows, NullableFieldOrderDifferentialKernels.ReadColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => NullableFieldOrderDifferentialKernels.LaunchReadNullableStructFieldKernel(
            b.Get<NullableOrderOuter>("items"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    private sealed class InnerComparer : IElementComparer<NullableOrderArrayInner?>
    {
        public static readonly InnerComparer Instance = new();

        public bool Equals(
            in NullableOrderArrayInner? expected,
            in NullableOrderArrayInner? actual,
            OutputPolicy policy) =>
            expected.HasValue == actual.HasValue && expected.GetValueOrDefault().A == actual.GetValueOrDefault().A;

        public string Format(in NullableOrderArrayInner? value) =>
            value.HasValue
                ? value.GetValueOrDefault().A.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "null";
    }

    internal static readonly DifferentialCase s_readNullableArrayField = new(
        $"{Kernels}.{nameof(NullableFieldOrderDifferentialKernels.ReadNullableArrayFieldKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 32),
        Inputs = static (rng, s) =>
        {
            var values = DiffValues.Int32(rng, s.Rows);
            var items = new NullableOrderArrayInner?[s.Rows];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = i % 4 == 1 ? null : new NullableOrderArrayInner { A = values[i] };
            }

            return new DiffBuffers()
                .In("items", items, OutputPolicy.ForStruct(InnerComparer.Instance), InnerComparer.Instance)
                .Out<int>("r", s.Rows, OutputPolicy.Exact);
        },
        Launch = static (b, s) => NullableFieldOrderDifferentialKernels.LaunchReadNullableArrayFieldKernel(
            new NullableOrderArrayHolder { Items = b.Get<NullableOrderArrayInner?>("items") },
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void StructDefinition_NullableArrayFieldOfAnotherStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_readNullableArrayField);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void StructDefinition_NullableFieldOfAnotherStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_readNullableStructField);
}
