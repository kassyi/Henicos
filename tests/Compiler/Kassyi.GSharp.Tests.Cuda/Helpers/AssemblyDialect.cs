namespace Kassyi.GSharp.Tests.Shared.Helpers;

internal static class AssemblyDialect
{
    public static ITranspilerDialect Current { get; } = new GSharpCudaDialect();
    public static DialectCapabilities Capabilities => Current.Capabilities;
}
