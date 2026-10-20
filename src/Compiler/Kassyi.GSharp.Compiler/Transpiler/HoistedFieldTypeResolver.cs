using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#pragma warning disable RS0030

namespace Kassyi.GSharp.Compiler.Transpiler;

internal static class HoistedFieldTypeResolver
{
    public static ITypeSymbol Resolve(VariableDeclaratorSyntax field, SemanticModel semanticModel, string kernelName)
    {
        var declaredSymbol = semanticModel.GetDeclaredSymbol<ILocalSymbol>(field);
        var type1 = declaredSymbol?.Type;

        ITypeSymbol? type2 = null;
        if (field.Initializer != null)
        {
            type2 = semanticModel.GetTypeSymbol(field.Initializer.Value);
        }

        // Test candidate 1
        if (type1 != null && IsValidType(type1, out var typeParam1) && type1.TypeKind != TypeKind.Error)
        {
            return type1;
        }

        // Test candidate 2
        if (type2 != null && IsValidType(type2, out var typeParam2) && type2.TypeKind != TypeKind.Error)
        {
            return type2;
        }

        var fieldName = field.Identifier.ValueText;
        var origLocation = semanticModel.GetOriginalLocation(field);

        string locationText;
        if (origLocation != null && origLocation.IsInSource)
        {
            var lineSpan = origLocation.GetLineSpan();
            locationText = $"{lineSpan.Path}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1})";
        }
        else
        {
            locationText = "not mapped (the node was synthesized by lowering)";
        }

        var declaredTypeText = (field.Parent as VariableDeclarationSyntax)?.Type.ToFullString().Trim() ?? "";
        var initializerText = field.Initializer?.Value.ToFullString().Trim() ?? "<none>";
        var lookup1Text = declaredSymbol?.ToDisplayString() ?? "not found";

        string lookup2Text;
        if (field.Initializer == null || type2 == null)
        {
            lookup2Text = "<none>";
        }
        else if (type2.TypeKind == TypeKind.Error)
        {
            lookup2Text = $"error type '{type2.ToDisplayString()}'";
        }
        else
        {
            lookup2Text = type2.ToDisplayString();
        }

        string reason;
        if (type1 != null)
        {
            if (type1.TypeKind == TypeKind.Error)
            {
                reason = "the type is an error type";
            }
            else if (ContainsTypeParameter(type1, out var tp))
            {
                reason = $"the type contains the type parameter '{tp!.ToDisplayString()}'; generic kernels are not supported";
            }
            else
            {
                reason = "no lookup produced a type";
            }
        }
        else if (type2 != null)
        {
            if (type2.TypeKind == TypeKind.Error)
            {
                reason = "the type is an error type";
            }
            else if (ContainsTypeParameter(type2, out var tp))
            {
                reason = $"the type contains the type parameter '{tp!.ToDisplayString()}'; generic kernels are not supported";
            }
            else
            {
                reason = "no lookup produced a type";
            }
        }
        else
        {
            reason = "no lookup produced a type";
        }

        var message =
            $"G# internal compiler error: cannot determine the type of the hoisted state-machine field '{fieldName}' in kernel '{kernelName}'.\n" +
            $"Original location: {locationText}\n" +
            $"Declared type: {declaredTypeText}\n" +
            $"Initializer: {initializerText}\n" +
            $"Lookup 1, declared local symbol: {lookup1Text}\n" +
            $"Lookup 2, initializer type: {lookup2Text}\n" +
            $"Reason: {reason}\n" +
            "Please report this at https://github.com/kassyi/Henicos/issues and include the kernel source and this message.";

        throw new InvalidOperationException(message);
    }

    private static bool IsValidType(ITypeSymbol type, out ITypeParameterSymbol? typeParam)
    {
        return !ContainsTypeParameter(type, out typeParam);
    }

    private static bool ContainsTypeParameter(ITypeSymbol? type, out ITypeParameterSymbol? typeParam)
    {
        if (type == null)
        {
            typeParam = null;
            return false;
        }

        if (type is ITypeParameterSymbol tp)
        {
            typeParam = tp;
            return true;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return ContainsTypeParameter(arrayType.ElementType, out typeParam);
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            return ContainsTypeParameter(pointerType.PointedAtType, out typeParam);
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var arg in namedType.TypeArguments)
            {
                if (ContainsTypeParameter(arg, out typeParam))
                {
                    return true;
                }
            }

            if (namedType.ContainingType != null)
            {
                return ContainsTypeParameter(namedType.ContainingType, out typeParam);
            }
        }

        typeParam = null;
        return false;
    }
}
