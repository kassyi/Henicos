#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Extracts legacy state-machine frame symbols (<see cref="LoweredStateMachine.ContextFields"/> and copied
/// parameters) to classify <c>FrameVariableOnLegacyPath</c> in the corpus agreement test of the null and call
/// decisions (#1561).
/// </summary>
internal static class IrNullAgreementFrameTracker
{
    /// <summary>
    /// Collects the locals hoisted into the legacy context and the copied parameters of <paramref name="symbol"/>.
    /// </summary>
    public static HashSet<ISymbol> CollectLegacyFrameSymbols(IMethodSymbol symbol, SemanticModel model)
    {
        var result = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not MethodDeclarationSyntax declaration ||
            !HasValidTypePath(symbol.ContainingType) ||
            HasCyclicStructParameter(symbol))
        {
            return result;
        }

        var isKernel = symbol.HasAttribute(KnownAttributes.GpuKernel);
        var plan = MethodEmissionPlan.Create(declaration, model, isKernel);
        if (plan.Lowered is not { } lowered)
        {
            return result;
        }

        foreach (var field in lowered.ContextFields)
        {
            if (LoweringIdentity.ForDeclaration(field, model).Symbol is { } hoisted)
            {
                result.Add(hoisted);
            }
        }

        foreach (var copied in plan.GetCopiedParameters(lowered))
        {
            result.Add(copied);
        }

        return result;
    }

    private static bool HasValidTypePath(INamedTypeSymbol? type)
    {
        for (ISymbol? current = type;
             current is not null and not INamespaceSymbol { IsGlobalNamespace: true };
             current = current.ContainingSymbol)
        {
            if (current is INamedTypeSymbol { TypeKind: TypeKind.Error } || string.IsNullOrEmpty(current.Name))
            {
                return false;
            }
        }

        return type is not null;
    }

    private static bool HasCyclicStructParameter(IMethodSymbol symbol)
    {
        var visiting = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var parameter in symbol.Parameters)
        {
            if (IsCyclicStruct(parameter.Type, visiting))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCyclicStruct(ITypeSymbol type, HashSet<ITypeSymbol> visiting)
    {
        var unwrapped = type.UnwrapNullable();
        if (unwrapped is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named ||
            named.ContainingNamespace.IsSystemOrNumerics())
        {
            return false;
        }

        // The definition, not the constructed type: a generic struct whose field nests its own type argument
        // (S<T> holding S<S<T>>) constructs a new type at every level and would never repeat
        if (!visiting.Add(named.OriginalDefinition))
        {
            return true;
        }

        foreach (var member in named.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field && IsCyclicStruct(field.Type, visiting))
            {
                return true;
            }
        }

        visiting.Remove(named.OriginalDefinition);
        return false;
    }

    /// <summary>
    /// Records <paramref name="expression"/> as <c>FrameVariableOnLegacyPath</c> when it references a symbol in
    /// <paramref name="frameSymbols"/>.
    /// </summary>
    public static bool TryRecordFrameVariable(ExpressionSyntax expression, SemanticModel model,
        HashSet<ISymbol> frameSymbols, string where, IrNullAgreementCounts counts)
    {
        if (frameSymbols.Count == 0)
        {
            return false;
        }

        var current = expression;
        while (true)
        {
            if (current is ParenthesizedExpressionSyntax parenthesized)
            {
                current = parenthesized.Expression;
            }
            else if (current is PostfixUnaryExpressionSyntax suppressed &&
                     suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                current = suppressed.Operand;
            }
            else if (current is InvocationExpressionSyntax { ArgumentList.Arguments: [{ } only] } call &&
                     model.GetSymbol(call) is IMethodSymbol method &&
                     Compiler.Transpiler.Helpers.NullChecks.NonNullProof.IsNotNullMethod(method))
            {
                current = only.Expression;
            }
            else
            {
                break;
            }
        }

        if (current is IdentifierNameSyntax &&
            model.GetSymbol(current) is { } sym &&
            frameSymbols.Contains(sym.OriginalDefinition))
        {
            counts.FrameVariables.Add($"{where}: {sym.Name} (IR=proven/None, Legacy=unproven/Checked)");
            return true;
        }

        return false;
    }
}
