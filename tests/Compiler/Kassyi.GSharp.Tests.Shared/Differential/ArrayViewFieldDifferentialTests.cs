#nullable enable
using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
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

    private static readonly string[] ValueColumn = ["value"];
    private static readonly string[] LengthColumns = ["tag", "lengths"];
    private static readonly string[] NestedColumns = ["memory", "segment"];
    private static readonly string[] ItemColumns = ["memory_0", "segment_0", "memory_1", "segment_1"];

    // One case per container: the kernel reads (and writes) one field of the struct, the other fields are marshaled
    // beside it, so a divergence of one container fails its own case only
    internal static readonly DifferentialCase s_memory = ViewCase(
        ArrayViewFieldDifferentialKernels.LaunchMemoryKernel);

    internal static readonly DifferentialCase s_readOnlyMemory = ViewCase(
        ArrayViewFieldDifferentialKernels.LaunchReadOnlyMemoryKernel);

    internal static readonly DifferentialCase s_segment = ViewCase(
        ArrayViewFieldDifferentialKernels.LaunchSegmentKernel);

    internal static readonly DifferentialCase s_immutableArray = ViewCase(
        ArrayViewFieldDifferentialKernels.LaunchImmutableArrayKernel);

    internal static readonly DifferentialCase s_optionalMemory = ViewCase(
        ArrayViewFieldDifferentialKernels.LaunchOptionalMemoryKernel);

    internal static readonly DifferentialCase s_optionalSegment = ViewCase(
        ArrayViewFieldDifferentialKernels.LaunchOptionalSegmentKernel);

    internal static readonly DifferentialCase s_lengths = ViewCase(
        ArrayViewFieldDifferentialKernels.LaunchLengthsKernel,
        LengthColumns);

    internal static readonly DifferentialCase s_nestedByValue = new(
        $"{Kernels}.{nameof(ArrayViewFieldDifferentialKernels.NestedByValueKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: Rows),
        Seeds = [0, 1],
        Inputs = static (rng, s) => new DiffBuffers()
            .In("memory", DiffValues.Int32(rng, 12))
            .In("segment", DiffValues.Int32(rng, 10))
            .Out<int>("r", s.Rows, NestedColumns, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            var outer = new OuterViewFields
            {
                Tag = 5,
                Inner = new NestedViewFields
                {
                    Tag = 6,
                    Memory = b.Get<int>("memory").AsMemory(2, 7),
                    Segment = new ArraySegment<int>(b.Get<int>("segment"), 3, 5),
                },
            };
            ArrayViewFieldDifferentialKernels.LaunchNestedByValueKernel(
                outer,
                b.Get<int>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_arrayOfStructs = new(
        $"{Kernels}.{nameof(ArrayViewFieldDifferentialKernels.ArrayOfStructsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: Rows),
        Seeds = [0, 1],
        Inputs = static (rng, s) => new DiffBuffers()
            .In("memory0", DiffValues.Int32(rng, 12))
            .In("segment0", DiffValues.Int32(rng, 9))
            .In("memory1", DiffValues.Int32(rng, 11))
            .In("segment1", DiffValues.Int32(rng, 6))
            .Out<int>("r", s.Rows, ItemColumns, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            NestedViewFields[] items =
            [
                new()
                {
                    Tag = 3,
                    Memory = b.Get<int>("memory0").AsMemory(1, 8),
                    Segment = new ArraySegment<int>(b.Get<int>("segment0"), 2, 5),
                },
                new()
                {
                    Tag = 9,
                    Memory = b.Get<int>("memory1").AsMemory(3, 6),
                    Segment = new ArraySegment<int>(b.Get<int>("segment1"), 1, 4),
                },
            ];
            ArrayViewFieldDifferentialKernels.LaunchArrayOfStructsKernel(
                items,
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
    public void ArrayViewFields_Memory_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_memory);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_ReadOnlyMemory_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_readOnlyMemory);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_ArraySegment_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_segment);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_ImmutableArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_immutableArray);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_NullableMemory_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_optionalMemory);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_NullableArraySegment_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_optionalSegment);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_Lengths_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_lengths);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_NestedStructByValue_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nestedByValue);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_ArrayOfStructs_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayOfStructs);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ArrayViewFields_ConvertedElementsAndManagerMemory_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_convertedElements);

    // The views of every field are built over the input buffers; the kernel of the case uses one of them
    private static DifferentialCase ViewCase(
        DiffLaunch<ArrayViewFields, int[]> launch,
        string[]? columns = null,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        new(DiffKernelNames.FromLaunchExpression(launchExpression, paramName: nameof(launch)))
        {
            Shape = LaunchShape.ForRows(Rows, blockX: Rows),
            Seeds = [0, 1],
            Inputs = (rng, s) =>
            {
                bool present = (rng.NextInt32() & 1) == 0;
                return new DiffBuffers()
                    .In("memory", DiffValues.Int32(rng, 12))
                    .In("readOnly", DiffValues.Int32(rng, 9))
                    .In("segment", DiffValues.Int32(rng, 10))
                    .In("immutable", DiffValues.Int32(rng, 5))
                    .In("optional", DiffValues.Int32(rng, 6))
                    .InScalar<bool>("present", present)
                    .Out<int>("r", s.Rows, columns ?? ValueColumn, OutputPolicy.Exact);
            },
            OracleIsLaunch = true,
            Launch = (b, s) =>
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
                launch(fields, b.Get<int>("r"), s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, 0, default, default);
            },
        };
}
