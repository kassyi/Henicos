#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// An array-like kernel argument means what it means in a C# call: a null array is null and an empty one is empty, a
/// default span-like value has no array and one over an empty array has one, and <c>Memory&lt;T&gt;?</c> has a value
/// whenever C# gives it one. Each case passes one value and makes one read (<see cref="ArrayArgumentReads"/>); the
/// oracle is the kernel's own C# body, so a read that throws in C# must throw the same exception on every path.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.DeviceExclusive)]
public sealed partial class ArrayArgumentNullEmptyDifferentialTests
{
    private const string Kernels = nameof(ArrayArgumentNullEmptyDifferentialKernels);

    private static readonly int[] s_arrayReads =
    [
        ArrayArgumentReads.IsNull, ArrayArgumentReads.Length, ArrayArgumentReads.Element, ArrayArgumentReads.ForEach,
        ArrayArgumentReads.AsSpanLength, ArrayArgumentReads.SpanLength,
        ArrayArgumentReads.ToNonNullable, ArrayArgumentReads.InNullableFunction,
    ];

    private static readonly (string Name, Func<int[]?> Create)[] s_arrayValues =
        [("Null", static () => null), ("Empty", static () => []), ("One", static () => [7])];

    internal static readonly DifferentialCase[] s_cases = [.. CreateCases()];

    /// <summary>The names of <see cref="s_cases"/> without the class name and the <c>Kernel</c> suffix, short enough to read in a test list.</summary>
    public static TheoryData<string> CaseNames => [.. s_cases.Select(static c => ShortName(c.Name))];

    [SkippableTheory]
    [MemberData(nameof(CaseNames))]
    public void ArrayArgument_EachValueAndRead_ReadsAsCSharpDoes(string caseName) =>
        DifferentialRunner.Run(s_cases.Single(c => ShortName(c.Name) == caseName));

    // ArrayArgumentNullEmptyDifferentialKernels.NullableArrayKernel.Null.Length -> NullableArray.Null.Length
    private static string ShortName(string name)
    {
        string rest = name[(Kernels.Length + 1)..];
        int kernelEnd = rest.IndexOf("Kernel.", StringComparison.Ordinal);
        return rest[..kernelEnd] + rest[(kernelEnd + "Kernel".Length)..];
    }

    private static IEnumerable<DifferentialCase> CreateCases()
    {
        foreach (var (valueName, create) in s_arrayValues)
        {
            foreach (int read in s_arrayReads)
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableArrayKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableArrayKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => ArrayArgumentNullEmptyDifferentialKernels.NullableArrayKernel(create(), read, o));
                // C# does not check the annotation of a parameter: a null reaches the body
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NonNullableArrayKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNonNullableArrayKernel(
                        create()!, read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => ArrayArgumentNullEmptyDifferentialKernels.NonNullableArrayKernel(create()!, read, o));
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.ObliviousArrayKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchObliviousArrayKernel(
                        create()!, read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => ArrayArgumentNullEmptyDifferentialKernels.ObliviousArrayKernel(create()!, read, o));
            }

            foreach (int read in (int[])[ArrayArgumentReads.IsNull, ArrayArgumentReads.Length, ArrayArgumentReads.InNullableFunction])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableArrayAfterBarrierKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableArrayAfterBarrierKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => ArrayArgumentNullEmptyDifferentialKernels.NullableArrayAfterBarrierKernel(create(), read, o));
            }
        }

        foreach (var (valueName, create) in ((string, Func<DiffDeepBag[]?>)[])
                 [("Null", static () => null), ("Empty", static () => []), ("One", static () => [new DiffDeepBag { Data = [1], Tag = 7 }])])
        {
            foreach (int read in (int[])[ArrayArgumentReads.IsNull, ArrayArgumentReads.Length, ArrayArgumentReads.ForEach, ArrayArgumentReads.AsSpanLength])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.ConvertedArrayKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchConvertedArrayKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => ArrayArgumentNullEmptyDifferentialKernels.ConvertedArrayKernel(create(), read, o));
            }
        }
    }

    private static DifferentialCase Create(string kernel, string value, int read, Action<int[], LaunchShape> launch,
        Action<int[]> oracle) =>
        new(DiffKernelNames.Of(typeof(ArrayArgumentNullEmptyDifferentialKernels), kernel, $"{value}.{ReadName(read)}"))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            OracleExceptions = OracleExceptionMode.MatchOracle,
            Inputs = static (_, _) => new DiffBuffers().Out<int>("o", 1, OutputPolicy.Exact),
            Launch = (b, s) => launch(b.Get<int>("o"), s),
            Oracle = (b, _) => oracle(b.Get<int>("o")),
        };

    private static string ReadName(int read) => read switch
    {
        ArrayArgumentReads.IsNull => "IsNull",
        ArrayArgumentReads.Length => "Length",
        ArrayArgumentReads.Element => "Element",
        ArrayArgumentReads.ForEach => "ForEach",
        ArrayArgumentReads.AsSpanLength => "AsSpanLength",
        ArrayArgumentReads.SpanLength => "SpanLength",
        ArrayArgumentReads.ToNonNullable => "ToNonNullable",
        _ => "InNullableFunction",
    };
}
