namespace Kassyi.GSharp.Tests.Shared.Helpers;

public static class CapabilityChecker
{
    public static void EnsureCapability(DialectCapabilities required)
    {
        Skip.IfNot(AssemblyDialect.Capabilities.HasFlag(required), $"Current dialect does not support {required}");
    }
}
