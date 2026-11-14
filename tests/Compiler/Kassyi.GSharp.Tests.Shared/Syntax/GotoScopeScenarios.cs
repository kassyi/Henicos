using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="GotoScopeKernels"/>. The native and managed test classes share these
/// scenarios and differ only in how a launch runs. Each kernel writes <c>data[1] = 7</c> after its label and, when the
/// goto is not taken (<c>flag != 0</c>), the value of the jumped-over local to <c>data[0]</c>; the expected values come
/// from the same methods run as plain C#.
/// </summary>
internal static class GotoScopeScenarios
{
    public static void Run(Action<Action> run, Action<int[], int> launch, int flag, int expectedFirst)
    {
        int[] data = [10, 20];

        run(() => launch(data, flag));

        Assert.Equal([expectedFirst, 7], data);
    }
}
