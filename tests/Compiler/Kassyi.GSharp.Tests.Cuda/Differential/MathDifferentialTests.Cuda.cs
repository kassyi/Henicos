#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class MathDifferentialTests
{
    private static partial PropertyResult? CheckFmaContract(int i, float k, bool matchesFma, float fmaExpected, float actual)
    {
        if (i == 0 && !matchesFma)
        {
            return PropertyResult.Fail(
                $"CUDA row 0 (k={k}): expected FMA contracted result {fmaExpected} (2^-24), got {actual}"
            );
        }

        return null;
    }
}
