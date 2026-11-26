#nullable enable
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Cpu.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class StackScopeTests
{
#if GSHARP_MANAGED_STACK
    private const bool BuiltWithManagedStack = true;
#else
    private const bool BuiltWithManagedStack = false;
#endif

    private static partial bool RunsOnAStack(DiffPath path) => path switch
    {
        DiffPath.Native => NativeStackTests.IsStackBuild(),
        DiffPath.Managed => BuiltWithManagedStack,
        _ => false
    };

    static partial void AssertManagedStackLaunch(DiffPathRunner path, Action launch)
    {
        long launches = 0;
        path.Run(() =>
        {
            long before = ManagedStackRunner.LaunchCountOnThisThread;
            launch();
            launches = ManagedStackRunner.LaunchCountOnThisThread - before;
        });
        if (path.Path == DiffPath.Managed)
        {
            Assert.Equal(1L, launches);
        }
    }
}
