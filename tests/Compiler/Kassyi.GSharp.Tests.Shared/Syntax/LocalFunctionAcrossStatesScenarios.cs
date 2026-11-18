using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The scenarios of <see cref="LocalFunctionAcrossStatesKernels"/>; the native and managed classes share them.
/// </summary>
internal static class LocalFunctionAcrossStatesScenarios
{
    public static void Run(Action<Action> run, Action<int[]> launch, Func<int, int> expected)
    {
        int[] data = [1, 2, 3, 4];

        run(() => launch(data));

        Assert.Equal([expected(1), expected(2), expected(3), expected(4)], data);
    }
}
