#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class MarshalingNullAndEmptyDifferentialTests
{
    private static partial OutputPolicy GetAfterThrowPolicy(int[]? optPrimInit) =>
        optPrimInit is null ? OutputPolicy.Ignore : OutputPolicy.Exact;

    private static partial ExceptionPolicy GetStructFieldNullDereferenceExceptionPolicy() =>
        new(typeof(NullReferenceException), DiffPaths.CheckedPaths())
        {
            SourceFile = nameof(MarshalingAliasingDifferentialKernels),
            Line = 505,
            Column = 25,
        };
}
