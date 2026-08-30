using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Diagnostics.Rules;

/// <summary>
/// Diagnostic rule that disallows SharedMemory&lt;T&gt; instantiation where T implements IDisposable or has a parameterless Dispose method.
/// </summary>
internal sealed class SharedMemoryForbiddenDisposableRule
{
    public static readonly SharedMemoryForbiddenDisposableRule Instance = new();

    private SharedMemoryForbiddenDisposableRule()
    {
    }

    public void Analyze(BaseObjectCreationExpressionSyntax node, ref ValidationContext context)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(node);
        var typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;

        if (typeSymbol is not INamedTypeSymbol namedTypeSymbol)
        {
            return;
        }

        if (namedTypeSymbol.Name != TypeConstants.SharedMemoryType ||
            namedTypeSymbol.ContainingNamespace?.ToDisplayString() != TypeConstants.CoreNamespace ||
            namedTypeSymbol.TypeArguments.Length != 1)
        {
            return;
        }

        var elementType = namedTypeSymbol.TypeArguments[0];
        if (IsForbiddenDisposableType(elementType))
        {
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.SharedMemoryDisposableNotSupported,
                node.GetLocation(),
                elementType.ToDisplayString());

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsForbiddenDisposableType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is ITypeParameterSymbol typeParameter)
        {
            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                if (IsForbiddenDisposableType(constraint))
                {
                    return true;
                }
            }
            return false;
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (IsIDisposableInterface(namedType))
        {
            return true;
        }

        foreach (var iface in namedType.AllInterfaces)
        {
            if (IsIDisposableInterface(iface))
            {
                return true;
            }
        }

        var current = namedType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers("Dispose"))
            {
                if (member is IMethodSymbol method && method.Parameters.Length == 0)
                {
                    return true;
                }
            }
            current = current.BaseType;
        }

        return false;
    }

    private static bool IsIDisposableInterface(INamedTypeSymbol type)
    {
        return type.ToDisplayString() == "System.IDisposable" ||
               (type.Name == "IDisposable" && type.ContainingNamespace?.ToDisplayString() == "System");
    }
}
