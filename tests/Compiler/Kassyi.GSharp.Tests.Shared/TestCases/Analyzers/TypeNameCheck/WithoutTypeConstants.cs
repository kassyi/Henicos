using Microsoft.CodeAnalysis;

namespace UserAnalyzer;

// Not the G# compiler (no Kassyi.GSharp.Compiler.TypeConstants), so the check does not run.
internal static class NameChecks
{
    public static bool IsGpu(ITypeSymbol type) => type.Name == "Gpu";
}
