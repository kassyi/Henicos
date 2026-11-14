#nullable enable
using System;
using System.Collections.Immutable;
using System.Numerics;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for struct fields of array-like types other than arrays and their nullable forms (#1267,
/// #1115): the views are built at launch over the input buffers, so the kernel's writes land in buffers the harness
/// compares.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public sealed class ArrayViewFieldDifferentialTests
{
    private const int Rows = 16;
    private const string Kernels = nameof(ArrayViewFieldDifferentialKernels);

    private static readonly IElementComparer<ArrayViewElement> ElementComparer =
        new StructElementComparer<ArrayViewElement>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(ArrayViewElement.X), e.X, a.X, OutputPolicy.Exact);
            ctx.Field("V.X", e.V.X, a.V.X, OutputPolicy.Exact);
            ctx.Field("V.Y", e.V.Y, a.V.Y, OutputPolicy.Exact);
        });

    internal static readonly DifferentialCase s_views = new(
        $"{Kernels}.{nameof(ArrayViewFieldDifferentialKernels.ReadAndWriteViewsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: Rows),
        Seeds = [0, 1],
        Inputs = static (rng, s) =>
        {
            bool present = (rng.NextInt32() & 1) == 0;
            return new DiffBuffers()
                .In("memory", DiffValues.Int32(rng, 12))
                .In("readOnly", DiffValues.Int32(rng, 9))
                .In("segment", DiffValues.Int32(rng, 10))
                .In("immutable", DiffValues.Int32(rng, 5))
                .In("optional", DiffValues.Int32(rng, 6))
                .InScalar<bool>("present", present)
                .Out<int>("r", s.Rows, ArrayViewFieldDifferentialKernels.FieldColumns, OutputPolicy.Exact);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            bool present = b.GetScalar<bool>("present");
            var optional = b.Get<int>("optional");
            var fields = new ArrayViewFields
            {
                Tag = 41,
                Memory = b.Get<int>("memory").AsMemory(2, 7),
                ReadOnly = b.Get<int>("readOnly").AsMemory(1),
                Segment = new ArraySegment<int>(b.Get<int>("segment"), 3, 5),
                Immutable = ImmutableArray.Create(b.Get<int>("immutable")),
                Optional = present ? optional.AsMemory(1, 4) : null,
                OptionalSegment = present ? null : new ArraySegment<int>(optional, 2, 3),
            };
            ArrayViewFieldDifferentialKernels.LaunchReadAndWriteViewsKernel(
                fields,
                b.Get<int>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_convertedElements = new(
        $"{Kernels}.{nameof(ArrayViewFieldDifferentialKernels.ConvertedElementsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: Rows),
        Seeds = [0],
        Inputs = static (rng, s) =>
        {
            var elements = new ArrayViewElement[10];
            for (int i = 0; i < elements.Length; i++)
            {
                elements[i] = new ArrayViewElement { X = i + 0.5f, V = new Vector2(i * 2f, -i - 0.25f) };
            }

            return new DiffBuffers()
                .In("elements", elements, OutputPolicy.ForStruct(ElementComparer), ElementComparer)
                .In("managed", DiffValues.Int32(rng, 6))
                .Out<float>("r", s.Rows, ArrayViewFieldDifferentialKernels.ElementColumns, OutputPolicy.Exact);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            var elements = b.Get<ArrayViewElement>("elements");
            var managed = b.Get<int>("managed");

            // A view that no array holds: a MemoryManager over the bytes of the buffer
            var manager = new HostAliasMemoryManager<int>(managed, sizeof(int), managed.Length - 1);
            var fields = new ArrayViewElementFields
            {
                Elements = elements.AsMemory(1, 6),
                ElementSegment = new ArraySegment<ArrayViewElement>(elements, 7, 3),
                Managed = manager.Memory,
            };
            ArrayViewFieldDifferentialKernels.LaunchConvertedElementsKernel(
                fields,
                b.Get<float>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_MemorySegmentImmutableAndNullable_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_views);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_ConvertedElementsAndManagerMemory_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_convertedElements);
}
