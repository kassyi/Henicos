namespace Kassyi.GSharp.Compiler.Diagnostics;

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

        return type is
        {
            Name: "IDisposable", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
        };
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

        foreach (var i in type.AllInterfaces)
        {
            if (IsSystemIDisposable(i))
            {
                return true;
            }
        }

        return false;
    }
}
