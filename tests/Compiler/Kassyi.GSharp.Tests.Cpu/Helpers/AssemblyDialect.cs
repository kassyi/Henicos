namespace Kassyi.GSharp.Tests.Shared.Helpers;

internal static class AssemblyDialect
{
    public static ITranspilerDialect Current { get; } = new StandardCppDialect();
    public static DialectCapabilities Capabilities => Current.Capabilities;
}
