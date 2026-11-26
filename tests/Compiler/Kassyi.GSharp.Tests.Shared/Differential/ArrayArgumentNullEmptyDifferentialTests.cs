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
/// whenever C# gives it one. Each case passes one value and makes one read (<see cref="ArrayArgumentRead"/>); the
/// oracle is the kernel's own C# body, so a read that throws in C# must throw the same exception on every path.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.DeviceExclusive)]
public sealed partial class ArrayArgumentNullEmptyDifferentialTests
{
    private const string Kernels = nameof(ArrayArgumentNullEmptyDifferentialKernels);

    private static readonly ArrayArgumentRead[] s_arrayReads =
    [
        ArrayArgumentRead.IsNull, ArrayArgumentRead.Length, ArrayArgumentRead.Element, ArrayArgumentRead.ForEach,
        ArrayArgumentRead.AsSpanLength, ArrayArgumentRead.SpanLength,
        ArrayArgumentRead.ToNonNullable, ArrayArgumentRead.InNullableFunction,
    ];

    private static readonly (string Name, Func<int[]?> Create)[] s_arrayValues =
        [("Null", static () => null), ("Empty", static () => []), ("One", static () => [7])];

    internal static readonly DifferentialCase[] s_cases =
        [.. CreateCases(), .. CreateSpanLikeCases(), .. CreateNullableViewCases()];

    /// <summary>
    /// The names of <see cref="s_cases"/> without the class name and the <c>Kernel</c> suffix, short enough to read in
    /// a test list.
    /// </summary>
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
            foreach (var read in (ArrayArgumentRead[])
                     [
                         ArrayArgumentRead.StackLinqSum, ArrayArgumentRead.StackLinqStoredSum, ArrayArgumentRead.StackLinqIndexedSum, ArrayArgumentRead.StackLinqInDeviceFunction,
                         ArrayArgumentRead.StackLinqStaticSum,
                     ])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableArrayKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableArrayKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (var read in s_arrayReads)
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableArrayKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableArrayKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
                // C# does not check the annotation of a parameter: a null reaches the body
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NonNullableArrayKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNonNullableArrayKernel(
                        create()!, read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.ObliviousArrayKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchObliviousArrayKernel(
                        create()!, read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }

            foreach (var read in (ArrayArgumentRead[])[ArrayArgumentRead.IsNull, ArrayArgumentRead.Length, ArrayArgumentRead.InNullableFunction])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableArrayAfterBarrierKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableArrayAfterBarrierKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX),
                    o => AfterBarrierModel(create(), read, o));
            }

            yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.NullableArrayForEachBarrierKernel), valueName, ArrayArgumentRead.ForEach,
                (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableArrayForEachBarrierKernel(
                    create(), o, gridDimX: s.GridX, blockDimX: s.BlockX),
                o => ForEachModel(create(), o));
        }

        foreach (var (valueName, create) in ((string, Func<DiffDeepBag[]?>)[])
                 [
                     ("Null", static () => null),
                     ("Empty", static () => []),
                     ("One", static () => [new DiffDeepBag { Data = [1], Tag = 7 }]),
                 ])
        {
            foreach (var read in (ArrayArgumentRead[])[ArrayArgumentRead.IsNull, ArrayArgumentRead.Length, ArrayArgumentRead.ForEach, ArrayArgumentRead.AsSpanLength])
            {
                yield return Create(nameof(ArrayArgumentNullEmptyDifferentialKernels.ConvertedArrayKernel), valueName, read,
                    (o, s) => ArrayArgumentNullEmptyDifferentialKernels.LaunchConvertedArrayKernel(
                        create(), read, o, gridDimX: s.GridX, blockDimX: s.BlockX));
            }
        }
    }

    // The read of NullableArrayAfterBarrierKernel for its one thread: the C# execution does not run a barrier, so this
    // model is its oracle, and it throws where the kernel's C# throws
    private static void AfterBarrierModel(int[]? a, ArrayArgumentRead read, int[] o) => o[0] = read switch
    {
        ArrayArgumentRead.IsNull => a is null ? 1 : 0,
        _ => a!.Length, // Deliberately unchecked: Length and InNullableFunction read a.Length, null throws as in C#
    };

    // The body of NullableArrayForEachBarrierKernel for its one thread without the barrier, which the C# execution
    // cannot run: a null array throws NullReferenceException when the loop starts
    private static void ForEachModel(int[]? a, int[] o)
    {
        o[0] = 100;
        foreach (var v in a!) // Deliberately unchecked: a null array throws as in the kernel's C#
        {
            o[0] += v + 1;
        }
    }

    // The launch is the oracle unless the case gives a model of the kernel's C#
    private static DifferentialCase Create(string kernel, string value, ArrayArgumentRead read, Action<int[], LaunchShape> launch,
        Action<int[]>? model = null) =>
        new(DiffKernelNames.Of(typeof(ArrayArgumentNullEmptyDifferentialKernels), kernel, $"{value}.{read}"))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            OracleExceptions = OracleExceptionMode.MatchOracle,
            Inputs = static (_, _) => new DiffBuffers().Out<int>("o", 1, OutputPolicy.Exact),
            Launch = (b, s) => launch(b.Get<int>("o"), s),
            OracleIsLaunch = model is null,
            Oracle = model is null ? null : (b, _) => model(b.Get<int>("o")),
        };
}
