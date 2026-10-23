using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// Layer 1 of the ownership model (memory safety guide, 11.2): a disposable resource that a method creates must be
/// disposed by that method or handed to a new owner.
/// </summary>
/// <remarks>
/// A resource is disposable under the same test the field checks of <see cref="DisposableOwnershipAnalyzer"/> use
/// (<see cref="DisposableAnalyzerHelper.IsDisposable"/>). Only a resource that is created in place is tracked: a
/// <c>new</c> expression that is discarded (GSH0067) or the initializer of a local (GSH0068). A local is accepted when
/// the method disposes it (<c>Dispose()</c>, <c>?.Dispose()</c>, <c>using</c>) or when any reference to it can move
/// the ownership: an argument of a constructor (<c>new T(..., x, ...)</c>, structs included), a
/// <c>[TakesOwnership]</c> or <c>ref</c>/<c>out</c> argument, a <c>return</c>, an assignment, a capture by a lambda or
/// local function, or any other use that is not a plain member access or a borrowing argument. Treating a constructor
/// argument as a move follows Roslyn's CA2000 (<c>dispose_ownership_transfer_at_constructor</c>): the new object is
/// assumed to keep the resource.
/// The check is not flow-sensitive: one disposal anywhere in the method satisfies it, so a disposal that only some
/// paths reach is not reported. The analyzer prefers a missed leak to a false report.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class LocalDisposableOwnershipAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.DiscardedDisposableAllocation,
            GSharpDiagnostics.UndisposedDisposableLocal
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationBlockAction(AnalyzeOperationBlock);
    }

    private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context)
    {
        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                switch (operation)
                {
                    case IExpressionStatementOperation statement:
                        AnalyzeDiscard(context, statement);
                        break;
                    case IVariableDeclaratorOperation declarator:
                        AnalyzeLocal(context, declarator);
                        break;
                }
            }
        }
    }

    // new X();  or  _ = new X();
    private static void AnalyzeDiscard(OperationBlockAnalysisContext context, IExpressionStatementOperation statement)
    {
        var expression = statement.Operation is ISimpleAssignmentOperation { Target: IDiscardOperation } assignment
            ? assignment.Value
            : statement.Operation;

        if (GetOwnedCreation(expression) is { } creation)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.DiscardedDisposableAllocation,
                statement.Syntax.GetLocation(),
                creation.Type?.Name));
        }
    }

    // var x = new X();  without using, Dispose() or a move of the ownership
    private static void AnalyzeLocal(OperationBlockAnalysisContext context, IVariableDeclaratorOperation declarator)
    {
        if (GetOwnedCreation(declarator.Initializer?.Value) is null || IsUsingResource(declarator))
        {
            return;
        }

        var local = declarator.Symbol;
        if (local.IsRef)
        {
            return;
        }

        var declaringFunction = GetEnclosingFunction(declarator);
        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (operation is ILocalReferenceOperation reference &&
                    SymbolEqualityComparer.Default.Equals(reference.Local, local) &&
                    DisposesOrMoves(reference, declaringFunction))
                {
                    return;
                }
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            GSharpDiagnostics.UndisposedDisposableLocal,
            declarator.Syntax.GetLocation(),
            local.Name));
    }

    private static IObjectCreationOperation? GetOwnedCreation(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation is IObjectCreationOperation creation && DisposableAnalyzerHelper.IsDisposable(creation.Type)
            ? creation
            : null;
    }

    // using (var x = ...) { }  and  using var x = ...;
    private static bool IsUsingResource(IVariableDeclaratorOperation declarator) =>
        declarator.Parent?.Parent?.Parent is IUsingOperation or IUsingDeclarationOperation;

    // Whether this reference disposes the local or may hand its ownership to someone else
    private static bool DisposesOrMoves(ILocalReferenceOperation reference, IOperation? declaringFunction)
    {
        // A lambda or local function that touches the local may dispose it or keep it alive
        if (!ReferenceEquals(GetEnclosingFunction(reference), declaringFunction))
        {
            return true;
        }

        IOperation current = reference;
        while (current.Parent is IConversionOperation conversion)
        {
            current = conversion;
        }

        switch (current.Parent)
        {
            // x.Dispose() is disposal; any other member call only uses the resource
            case IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current):
                return IsDisposeMethod(invocation.TargetMethod);
            case IPropertyReferenceOperation property when ReferenceEquals(property.Instance, current):
            case IFieldReferenceOperation field when ReferenceEquals(field.Instance, current):
                return false;
            // x?.Dispose()
            case IConditionalAccessOperation conditional when ReferenceEquals(conditional.Operation, current):
                return CallsDispose(conditional.WhenNotNull);
            // new T(x) moves the resource into the new object
            case IArgumentOperation { Parent: IObjectCreationOperation }:
                return true;
            // f(x) borrows the resource unless the parameter takes it
            case IArgumentOperation argument:
                return argument.Parameter is not { RefKind: RefKind.None } parameter ||
                       parameter.HasAttribute(KnownAttributes.TakesOwnership);
            default:
                return true;
        }
    }

    private static bool CallsDispose(IOperation operation)
    {
        foreach (var descendant in operation.DescendantsAndSelf())
        {
            if (descendant is IInvocationOperation invocation && IsDisposeMethod(invocation.TargetMethod))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDisposeMethod(IMethodSymbol method) =>
        method.Name is "Dispose" or "DisposeAsync";

    private static IOperation? GetEnclosingFunction(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return current;
            }
        }

        return null;
    }
}
