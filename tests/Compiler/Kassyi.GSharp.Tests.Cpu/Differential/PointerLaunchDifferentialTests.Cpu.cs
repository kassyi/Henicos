#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class PointerLaunchDifferentialTests
{
    private static partial Action<DiffBuffers, LaunchShape>? GetTwoLengthsLaunchPtr() => null;
    private static partial Action<DiffBuffers, LaunchShape>? GetOptionalLengthLaunchPtr() => null;
}
