namespace Kassyi.GSharp.Tests.Helpers;

public static class CapabilityChecker
{
    public static void EnsureCapability(DialectCapabilities required)
    {
        DialectCapabilities current = DialectCapabilities.None;
#if GSHARP_CUDA
        current = new Kassyi.GSharp.Compiler.Dialects.GSharpCudaDialect().Capabilities;
#elif GSHARP_CPU
        current = new Kassyi.GSharp.Compiler.Dialects.StandardCppDialect().Capabilities;
#endif
        Skip.IfNot(current.HasFlag(required), $"Current dialect does not support {required}");
    }
}
