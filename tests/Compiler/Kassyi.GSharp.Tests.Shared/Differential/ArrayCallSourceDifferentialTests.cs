#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using C = Kassyi.GSharp.Tests.Shared.Differential.ArrayCalls;
using K = Kassyi.GSharp.Tests.Shared.Differential.ArrayCallSourceDifferentialKernels;
using S = Kassyi.GSharp.Tests.Shared.Differential.ArrayCallSources;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// <c>Array.Fill</c>, <c>Clear</c>, <c>Reverse</c> and <c>Copy</c> check their array inside the kernel, whatever the
/// array comes from: a null array from a struct field, a local or a device function parameter throws
/// <see cref="ArgumentNullException"/> as in C#, and an empty one from the same place is not taken for null. The oracle
/// is the kernel's own C# body.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class ArrayCallSourceDifferentialTests
{
    private static readonly int[] s_sources = [S.Field, S.Local, S.Parameter];

    private static readonly (string Name, Func<int[]?> Create)[] s_values =
        [("Null", static () => null), ("Empty", static () => [])];

    internal static readonly DifferentialCase[] s_cases = [.. CreateCases()];

    /// <summary>The names of <see cref="s_cases"/> without the class name.</summary>
    public static TheoryData<string> CaseNames => [.. s_cases.Select(static c => ShortName(c.Name))];

    [SkippableTheory]
    [MemberData(nameof(CaseNames))]
    public void ArrayCall_NullOrEmptyArrayFromEachSource_ChecksAsCSharpDoes(string caseName) =>
        DifferentialRunner.Run(s_cases.Single(c => ShortName(c.Name) == caseName));

    // ArrayCallSourceDifferentialKernels.ArrayCallKernel.Field.Fill.Null -> Field.Fill.Null
    private static string ShortName(string name) =>
        name[(name.IndexOf("Kernel.", StringComparison.Ordinal) + "Kernel.".Length)..];

    private static IEnumerable<DifferentialCase> CreateCases()
    {
        foreach (int source in s_sources)
        {
            for (int call = 0; call < C.Count; call++)
            {
                foreach (var (valueName, create) in s_values)
                {
                    yield return Create(source, call, valueName, create);
                }
            }
        }
    }

    // The sources the case does not test hold an array with elements, so a call that took the wrong array would not
    // throw
    private static DifferentialCase Create(int source, int call, string value, Func<int[]?> create) =>
        new(DiffKernelNames.Of(typeof(K), nameof(K.ArrayCallKernel), $"{SourceName(source)}.{CallName(call)}.{value}"))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            OracleExceptions = OracleExceptionMode.MatchOracle,
            Inputs = static (_, _) => new DiffBuffers().Out<int>("o", 2, OutputPolicy.Exact),
            Launch = (b, s) => K.LaunchArrayCallKernel(
                new ArrayCallHolder { Data = source == S.Field ? create() : [1, 2], Tag = 3 },
                source == S.Field ? [1, 2] : create(),
                source,
                call,
                b.Get<int>("o"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX),
            OracleIsLaunch = true,
        };

    private static string SourceName(int source) => source switch
    {
        S.Field => "Field",
        S.Local => "Local",
        _ => "Parameter",
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
        _ => "CopyRangeTo",
    };
}
