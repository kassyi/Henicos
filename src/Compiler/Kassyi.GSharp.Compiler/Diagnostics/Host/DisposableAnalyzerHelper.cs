namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

internal static class DisposableAnalyzerHelper
{
    public static bool IsSystemIDisposable(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        if (type.SpecialType == SpecialType.System_IDisposable ||
            type.OriginalDefinition.SpecialType == SpecialType.System_IDisposable)
        {
            return true;
        }

        return type is { Name: "IDisposable" } && type.ContainingNamespace.IsSystemNamespace();
    }

    public static bool IsDisposable(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        if (IsSystemIDisposable(type))
        {
            return true;
        }

        return type.AllInterfaces
            .AsStackEnumerable()
            .Any(IsSystemIDisposable);
    }

    public static bool IsGpuResourceScope(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        if (type.Name == "IGpuResourceScope")
        {
            return true;
        }

        return type.AllInterfaces
            .AsStackEnumerable()
            .Any(static i => i.Name == "IGpuResourceScope");
    }

    public static bool HasGpuResourceAttribute(ITypeSymbol? type) =>
        type?.HasAttribute(KnownAttributes.GpuResource) ?? false;
}
