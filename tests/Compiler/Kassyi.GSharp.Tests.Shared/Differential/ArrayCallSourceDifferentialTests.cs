#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using C = Kassyi.GSharp.Tests.Shared.Differential.ArrayCalls;
using K = Kassyi.GSharp.Tests.Shared.Differential.ArrayCallSourceDifferentialKernels;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// <c>Array.Fill</c>, <c>Clear</c>, <c>Reverse</c> and <c>Copy</c> check their array inside the kernel, whatever the
/// array comes from: a null array from a struct field, an element of a struct array, a local or a device function
/// parameter throws <see cref="ArgumentNullException"/> naming the parameter as in C#, before any range check, and an
/// empty one from the same place is not taken for null. The oracle is the kernel's own C# body.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class ArrayCallSourceDifferentialTests
{
    private delegate void Launcher(int[]? array, int call, int[] o, LaunchShape shape);

    // The launch of each kernel of ArrayCallSourceDifferentialKernels with the array in its source
    private static readonly (string Kernel, Launcher Launch)[] s_sources =
    [
        (nameof(K.FieldArrayCallKernel), static (array, call, o, s) => K.LaunchFieldArrayCallKernel(
            new ArrayCallHolder { Data = array, Tag = 3 }, call, o, gridDimX: s.GridX, blockDimX: s.BlockX)),
        (nameof(K.ElementArrayCallKernel), static (array, call, o, s) => K.LaunchElementArrayCallKernel(
            [new ArrayCallHolder { Data = array, Tag = 3 }, new ArrayCallHolder { Data = [1, 2], Tag = 4 }],
            call, o, gridDimX: s.GridX, blockDimX: s.BlockX)),
        (nameof(K.LocalArrayCallKernel), static (array, call, o, s) => K.LaunchLocalArrayCallKernel(
            array, call, o, gridDimX: s.GridX, blockDimX: s.BlockX)),
        (nameof(K.ParameterArrayCallKernel), static (array, call, o, s) => K.LaunchParameterArrayCallKernel(
            array, call, o, gridDimX: s.GridX, blockDimX: s.BlockX)),
    ];

    private static readonly (string Name, Func<int[]?> Create)[] s_values =
        [("Null", static () => null), ("Empty", static () => [])];

    internal static readonly DifferentialCase[] s_cases = [.. CreateCases()];

    /// <summary>The names of <see cref="s_cases"/> without the class name.</summary>
    public static TheoryData<string> CaseNames => [.. s_cases.Select(static c => ShortName(c.Name))];

    [SkippableTheory]
    [MemberData(nameof(CaseNames))]
    public void ArrayCall_NullOrEmptyArrayFromEachSource_ChecksAsCSharpDoes(string caseName) =>
        DifferentialRunner.Run(s_cases.Single(c => ShortName(c.Name) == caseName));

    // ArrayCallSourceDifferentialKernels.FieldArrayCallKernel.Fill.Null -> FieldArrayCall.Fill.Null
    private static string ShortName(string name)
    {
        string rest = name[(nameof(ArrayCallSourceDifferentialKernels).Length + 1)..];
        int kernelEnd = rest.IndexOf("Kernel.", StringComparison.Ordinal);
        return rest[..kernelEnd] + rest[(kernelEnd + "Kernel".Length)..];
    }

    private static IEnumerable<DifferentialCase> CreateCases()
    {
        foreach (var (kernel, launch) in s_sources)
        {
            for (int call = 0; call < C.Count; call++)
            {
                foreach (var (valueName, create) in s_values)
                {
                    yield return Create(kernel, launch, call, valueName, create);
                }
            }
        }
    }

    // A null array: ArgumentNullException naming the parameter on every path, read from the message, since the
    // native and CUDA exceptions carry the name only there. An empty array: whatever the kernel's C# does
    private static DifferentialCase Create(
        string kernel,
        Launcher launch,
        int call,
        string value,
        Func<int[]?> create
    ) =>
        new(DiffKernelNames.Of(typeof(K), kernel, $"{CallName(call)}.{value}"))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            OracleExceptions = OracleExceptionMode.MatchOracle,
            ExceptionPolicy = value == "Null"
                ? ExceptionPolicy.For<ArgumentNullException>(
                    DiffPaths.CheckedPaths(),
                    paramName: ParameterName(call))
                : null,
            Inputs = static (_, _) => new DiffBuffers().Out<int>("o", 2, OutputPolicy.Exact),
            Launch = (b, s) => launch(create(), call, b.Get<int>("o"), s),
            OracleIsLaunch = true,
        };

    private static string ParameterName(int call) => call switch
    {
        C.CopyFrom or C.CopyRangeFrom or C.CopyFromPastEnd => "sourceArray",
        C.CopyTo or C.CopyRangeTo => "destinationArray",
        _ => "array",
    };

    private static string CallName(int call) => call switch
    {
        C.Fill => "Fill",
        C.FillRange => "FillRange",
        C.Clear => "Clear",
        C.ClearRange => "ClearRange",
        C.Reverse => "Reverse",
        C.ReverseRange => "ReverseRange",
        C.CopyFrom => "CopyFrom",
        C.CopyRangeFrom => "CopyRangeFrom",
        C.CopyTo => "CopyTo",
        C.CopyRangeTo => "CopyRangeTo",
        C.FillPastEnd => "FillPastEnd",
        C.ClearPastEnd => "ClearPastEnd",
        _ => "CopyFromPastEnd",
    };
}
