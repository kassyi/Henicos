using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// Finds the methods that one operation runs or references: the target of a call, a method group or the operand of
/// <c>&amp;</c>, and the methods C# calls for the syntax of an operator, a conversion, a property or event access, a
/// <c>foreach</c>, a deconstruction, a pattern or an <c>await</c>, as far as Roslyn shows them.
/// </summary>
/// <remarks>
/// The <c>Dispose</c> of a <c>using</c>, the <c>GetPinnableReference</c> of a <c>fixed</c>, the <c>EnterScope</c> of a
/// <c>lock</c> and the builder and <c>Add</c> calls of a collection expression have no public form in Roslyn 4.8, so a
/// rule that needs them checks the declaration of the method instead. The operations to register for are
/// <see cref="Kinds"/>.
/// </remarks>
internal static class OperationCallees
{
    /// <summary>The operation kinds that <see cref="FindFirst"/> looks into.</summary>
    public static ImmutableArray<OperationKind> Kinds { get; } = ImmutableArray.Create(
        OperationKind.Invocation,
        OperationKind.MethodReference,
        OperationKind.Binary,
        OperationKind.Unary,
        OperationKind.Conversion,
        OperationKind.CompoundAssignment,
        OperationKind.Increment,
        OperationKind.Decrement,
        OperationKind.PropertyReference,
        OperationKind.EventAssignment,
        OperationKind.Loop,
        OperationKind.DeconstructionAssignment,
        OperationKind.RecursivePattern,
        OperationKind.ListPattern,
        OperationKind.SlicePattern,
        OperationKind.Await
    );

    /// <summary>
    /// The first method that <paramref name="operation"/> runs or references for which <paramref name="match"/> holds,
    /// as its original definition (an extension method in its static form), with the syntax to report at.
    /// </summary>
    public static OperationCallee? FindFirst(IOperation operation, Func<IMethodSymbol, bool> match)
    {
        var syntax = operation.Syntax;
        var method = operation switch
        {
            IInvocationOperation invocation => Match(invocation.TargetMethod, match),
            IMethodReferenceOperation reference => Match(reference.Method, match),
            IBinaryOperation binary => Match(binary.OperatorMethod, match),
            IUnaryOperation unary => Match(unary.OperatorMethod, match),
            IConversionOperation conversion => Match(conversion.OperatorMethod, match),
            ICompoundAssignmentOperation compound => Match(compound.OperatorMethod, match),
            IIncrementOrDecrementOperation increment => Match(increment.OperatorMethod, match),
            IPropertyReferenceOperation property => FindAccessor(property, match),
            IEventAssignmentOperation { EventReference: IEventReferenceOperation reference } assignment =>
                Match(assignment.Adds ? reference.Event.AddMethod : reference.Event.RemoveMethod, match),
            IRecursivePatternOperation pattern => MatchMember(pattern.DeconstructSymbol, match),
            IListPatternOperation list =>
                MatchMember(list.LengthSymbol, match) ?? MatchMember(list.IndexerSymbol, match),
            ISlicePatternOperation slice => MatchMember(slice.SliceSymbol, match),
            IForEachLoopOperation { Syntax: CommonForEachStatementSyntax loop } =>
                FindInForEach(loop, operation, match),
            IDeconstructionAssignmentOperation => FindInDeconstruction(syntax, operation, match),
            IAwaitOperation { Syntax: AwaitExpressionSyntax awaitSyntax } => FindInAwait(awaitSyntax, operation, match),
            _ => null
        };

        if (method is null)
        {
            return null;
        }

        // A foreach is reported at its collection, not at the whole statement; a function pointer at the whole `&M`
        var site = (syntax, operation.Parent) switch
        {
            (CommonForEachStatementSyntax forEach, _) => forEach.Expression,
            (_, IAddressOfOperation addressOf) => addressOf.Syntax,
            _ => syntax
        };
        return new OperationCallee(method, site);
    }

    private static IMethodSymbol? Match(IMethodSymbol? method, Func<IMethodSymbol, bool> match)
    {
        var definition = (method?.ReducedFrom ?? method)?.OriginalDefinition;
        return definition is not null && match(definition) ? definition : null;
    }

    // A pattern names a method (Deconstruct, Slice) or a property (Length, Count, an indexer) that C# reads
    private static IMethodSymbol? MatchMember(ISymbol? member, Func<IMethodSymbol, bool> match) => member switch
    {
        IMethodSymbol method => Match(method, match),
        IPropertySymbol property => Match(property.GetMethod, match),
        _ => null
    };

    private static IMethodSymbol? FindAccessor(IPropertyReferenceOperation reference, Func<IMethodSymbol, bool> match)
    {
        var property = reference.Property;
        var (reads, writes) = GetAccess(reference);
        return (reads ? Match(property.GetMethod, match) : null) ??
               (writes ? Match(property.SetMethod ?? property.GetMethod, match) : null);
    }

    // Which accessors C# calls for a property reference: the setter of an assignment target or a deconstruction
    // element, both for a compound assignment or an increment, the getter otherwise (a ref property has only that)
    private static (bool Reads, bool Writes) GetAccess(IOperation reference) => reference.Parent switch
    {
        ISimpleAssignmentOperation assignment when assignment.Target == reference => (false, true),
        ICompoundAssignmentOperation compound when compound.Target == reference => (true, true),
        ICoalesceAssignmentOperation coalesce when coalesce.Target == reference => (true, true),
        IIncrementOrDecrementOperation increment when increment.Target == reference => (true, true),
        ITupleOperation tuple when IsDeconstructionTarget(tuple) => (false, true),
        _ => (true, false)
    };

    private static bool IsDeconstructionTarget(ITupleOperation tuple)
    {
        IOperation current = tuple;
        while (current.Parent is ITupleOperation outer)
        {
            current = outer;
        }

        return current.Parent is IDeconstructionAssignmentOperation assignment && assignment.Target == current;
    }

    private static IMethodSymbol? FindInForEach(
        CommonForEachStatementSyntax loop,
        IOperation operation,
        Func<IMethodSymbol, bool> match)
    {
        if (operation.SemanticModel is not { } model)
        {
            return null;
        }

        var info = model.GetForEachStatementInfo(loop);
        return Match(info.GetEnumeratorMethod, match) ??
               Match(info.MoveNextMethod, match) ??
               Match(info.CurrentProperty?.GetMethod, match) ??
               Match(info.DisposeMethod, match) ??
               Match(info.CurrentConversion.MethodSymbol, match) ??
               Match(info.ElementConversion.MethodSymbol, match) ??
               (loop is ForEachVariableStatementSyntax deconstruction
                   ? FindInDeconstruction(model.GetDeconstructionInfo(deconstruction), match)
                   : null);
    }

    private static IMethodSymbol? FindInDeconstruction(
        SyntaxNode syntax,
        IOperation operation,
        Func<IMethodSymbol, bool> match) =>
        syntax is AssignmentExpressionSyntax assignment && operation.SemanticModel is { } model
            ? FindInDeconstruction(model.GetDeconstructionInfo(assignment), match)
            : null;

    // The Deconstruct methods and user-defined conversions of a deconstruction, nested ones included
    private static IMethodSymbol? FindInDeconstruction(DeconstructionInfo info, Func<IMethodSymbol, bool> match)
    {
        if ((Match(info.Method, match) ?? Match(info.Conversion?.MethodSymbol, match)) is { } found)
        {
            return found;
        }

        foreach (var nested in info.Nested)
        {
            if (FindInDeconstruction(nested, match) is { } inNested)
            {
                return inNested;
            }
        }

        return null;
    }

    private static IMethodSymbol? FindInAwait(
        AwaitExpressionSyntax awaitSyntax,
        IOperation operation,
        Func<IMethodSymbol, bool> match)
    {
        if (operation.SemanticModel is not { } model)
        {
            return null;
        }

        var info = model.GetAwaitExpressionInfo(awaitSyntax);
        return Match(info.GetAwaiterMethod, match) ??
               Match(info.IsCompletedProperty?.GetMethod, match) ??
               Match(info.GetResultMethod, match);
    }
}
