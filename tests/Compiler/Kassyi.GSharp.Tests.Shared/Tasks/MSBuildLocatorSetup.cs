#nullable enable
using Microsoft.Build.Locator;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

/// <summary>Registers the installed MSBuild once for the tests that create the G# build tasks.</summary>
/// <remarks>
/// The test projects reference <c>Microsoft.Build.Framework</c> and <c>Microsoft.Build.Utilities.Core</c> without their
/// runtime assets, so the MSBuild assemblies load only from the instance <see cref="MSBuildLocator"/> registers.
/// <see cref="MSBuildLocator.RegisterDefaults"/> throws <see cref="InvalidOperationException"/> when an instance is
/// already registered, when MSBuild assemblies were loaded before the call, or when no MSBuild is installed. The lock
/// and <see cref="MSBuildLocator.IsRegistered"/> rule out the first (test classes run their static constructors in
/// parallel); the other two leave the tasks without MSBuild, so the exception is not caught and the test class fails
/// with the reason.
/// </remarks>
internal static class MSBuildLocatorSetup
{
    private static readonly Lock s_gate = new();

    public static void EnsureRegistered()
    {
        lock (s_gate)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }
    }
}
