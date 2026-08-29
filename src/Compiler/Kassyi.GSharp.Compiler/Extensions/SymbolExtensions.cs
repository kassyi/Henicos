namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Provides zero-allocation extension methods for Roslyn symbol inspections and namespace matching.</summary>
internal static class SymbolExtensions
{
    public static bool IsSystemNamespace(this INamespaceSymbol? ns) =>
        ns is { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true };

    public static bool IsSystemOrNumerics(this INamespaceSymbol? ns) =>
        ns is { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
            or
            {
                Name: TypeConstants.NumericsNamespace,
                ContainingNamespace:
                { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
            };

    public static bool IsSystemThreading(this INamespaceSymbol? ns) =>
        ns is
        {
            Name: TypeConstants.ThreadingNamespace,
            ContainingNamespace:
            { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
        };

    public static bool IsCoreNamespace(this INamespaceSymbol? ns) =>
        ns is
        {
            Name: "Core",
            ContainingNamespace:
            { Name: "GSharp", ContainingNamespace: { Name: "Kassyi", ContainingNamespace.IsGlobalNamespace: true } }
        };

    public static bool IsSystemNumericsVector(this ITypeSymbol? typeSymbol) =>
        typeSymbol is
        {
            Name: TypeConstants.Vector2Type or TypeConstants.Vector3Type or TypeConstants.Vector4Type,
            ContainingNamespace:
            {
                Name: TypeConstants.NumericsNamespace,
                ContainingNamespace:
                { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
            }
        };

    public static bool IsGpuBigIntRef(this ITypeSymbol? typeSymbol) =>
        typeSymbol is
        {
            Name: TypeConstants.GpuBigIntRefType,
            ContainingNamespace:
            {
                Name: "Core",
                ContainingNamespace:
                { Name: "GSharp", ContainingNamespace: { Name: "Kassyi", ContainingNamespace.IsGlobalNamespace: true } }
            }
        };
}
