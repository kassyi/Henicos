using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

internal static partial class BoundsAndMemoryDifferentialTestHelpers
{
    private static partial ExceptionPolicy CreateNullArrayPolicy(int line, int column) =>
        new(typeof(NullReferenceException), DiffPaths.CheckedPaths())
        {
            SourceFile = nameof(BoundsAndMemoryDifferentialKernels),
            Line = line,
            Column = column,
        };
}
