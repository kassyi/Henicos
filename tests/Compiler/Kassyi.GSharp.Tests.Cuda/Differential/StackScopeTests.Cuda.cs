#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class StackScopeTests
{
    private static partial bool RunsOnAStack(DiffPath path) => path == DiffPath.Cuda;

    static partial void AssertManagedStackLaunch(DiffPathRunner path, Action launch) =>
        path.Run(launch);
}
