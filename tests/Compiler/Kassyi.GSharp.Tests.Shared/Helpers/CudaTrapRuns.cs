#nullable enable
using Kassyi.GSharp.Tests.Shared.Differential;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Runs a test that raises a real GPU fault in a child process, only when <see cref="CudaTrapGate.IsEnabled"/>, paced
/// by <see cref="CudaTrapGate.RunPaced{T}"/>.
/// </summary>
public static class CudaTrapRuns
{
    /// <summary>Skips the test unless <see cref="CudaTrapGate.IsEnabled"/>.</summary>
    public static void SkipUnlessEnabled() =>
        Skip.IfNot(CudaTrapGate.IsEnabled, "This test " + CudaTrapGate.SkipReason + ".");

    /// <summary>
    /// Runs <paramref name="testMethodName"/> of <paramref name="testClass"/> in a child process through
    /// <see cref="IsolatedTestProcess.Run"/>, paced by <see cref="CudaTrapGate.RunPaced{T}"/>; skips the test unless
    /// <see cref="CudaTrapGate.IsEnabled"/>.
    /// </summary>
    public static void RunIsolated(Type testClass, string testMethodName)
    {
        SkipUnlessEnabled();
        CudaTrapGate.RunPaced(() =>
        {
            IsolatedTestProcess.Run(testClass, testMethodName);
            return 0;
        });
    }
}
