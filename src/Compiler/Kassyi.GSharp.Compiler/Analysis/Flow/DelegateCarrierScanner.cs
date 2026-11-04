using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Scans a method body to discover delegate carrier locals and inspect delegate creation bodies.
/// </summary>
internal static class DelegateCarrierScanner
{
    /// <summary>
    /// Finds the delegate carrier locals of method: delegate locals only run or assigned,
    /// not referenced in nested functions.
    /// </summary>
    public static void ScanCarriers(SemanticModel model, SyntaxNode method, HashSet<ILocalSymbol> carriers)
    {
        var candidates = ImmutableArray.CreateBuilder<ILocalSymbol>();
        var rejected = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        foreach (var node in method.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax declarator
                    when model.GetDeclaredSymbol<ILocalSymbol>(declarator) is { IsRef: false, Type.TypeKind: TypeKind.Delegate } local:
                    candidates.Add(local);
                    break;
                case IdentifierNameSyntax name
                    when model.GetSymbol(name) is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } local &&
                         (!IsRunOrAssigned(name) || IsInsideFunction(name, method, model)):
                    rejected.Add(local);
                    break;
            }
        }

        foreach (var candidate in candidates)
        {
            if (!rejected.Contains(candidate))
            {
                carriers.Add(candidate);
            }
        }
    }

    /// <summary>Whether the identifier is invoked or assigned as a simple assignment target.</summary>
    public static bool IsRunOrAssigned(IdentifierNameSyntax name) => name.Parent switch
    {
        InvocationExpressionSyntax invocation => invocation.Expression == name,
        MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke", Parent: InvocationExpressionSyntax } access =>
            access.Expression == name,
        AssignmentExpressionSyntax assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Left == name,
        _ => false
    };

    /// <summary>Whether node lies in a lambda of method that does not run in place, or in a local function of method.</summary>
    public static bool IsInsideFunction(SyntaxNode node, SyntaxNode method, SemanticModel model)
    {
        for (var ancestor = node.Parent; ancestor != null && ancestor != method; ancestor = ancestor.Parent)
        {
            if (ancestor is LocalFunctionStatementSyntax ||
                (ancestor is AnonymousFunctionExpressionSyntax && !RunsInPlace(ancestor, model)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether lambda runs within the call it is passed to and is never stored: macro body or StackLinq operator argument.
    /// </summary>
    public static bool RunsInPlace(SyntaxNode lambda, SemanticModel model) =>
        GpuMacroClassifier.ClassifyBody(lambda, model).HasBody ||
        (lambda.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } } &&
         model.GetSymbol(call) is IMethodSymbol method &&
         StackLinqHelper.IsStackLinqMethod(method));

    /// <summary>The lambda or local function that makes the delegate value: its syntax, or null for any other value.</summary>
    public static SyntaxNode? GetDelegateBody(IOperation value) => value switch
    {
        IFlowAnonymousFunctionOperation lambda => lambda.Syntax,
        IDelegateCreationOperation creation => GetDelegateBody(creation.Target),
        IConversionOperation conversion => GetDelegateBody(conversion.Operand),
        IMethodReferenceOperation { Method: { MethodKind: MethodKind.LocalFunction } method }
            when method.OriginalDefinition.TryGetDeclaringSyntax(out LocalFunctionStatementSyntax? localFunction) =>
            localFunction,
        _ => null
    };
}
