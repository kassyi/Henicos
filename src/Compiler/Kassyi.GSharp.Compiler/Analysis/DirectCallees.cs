using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Collects the direct callees of a set of syntax bodies from their Roslyn operations, together with the syntax node of
/// each call site: invocations, constructors, property/indexer accessors (chosen by read/write use), user-defined
/// operators and conversions, method groups, <c>using</c> <c>Dispose</c>, collection builders, <c>fixed</c>
/// <c>GetPinnableReference</c>, operator macro targets, truth operators, and constrained interface members.
/// </summary>
internal static class DirectCallees
{
    /// <summary>
    /// The callees with code of their own (a body in <paramref name="compilation"/>, an export record in another
    /// assembly, or an interface member) that <paramref name="bodies"/> call or reference directly, outside calls that
    /// C# removes (<see cref="RemovedCalls"/>).
    /// </summary>
    public static ImmutableArray<CalleeSite> Of(IReadOnlyList<SyntaxNode> bodies, TypeArgumentMap typeArguments,
        Compilation compilation)
    {
        var called = ImmutableArray.CreateBuilder<CalleeSite>();
        AddBodyCalls(bodies, typeArguments, compilation, called);
        var callees = ImmutableArray.CreateBuilder<CalleeSite>();
        foreach (var (method, site) in called)
        {
            var function = method.GetConstructedReducedFrom() ?? method;
            var definition = DeviceFunctionEffects.GetDefinition(function);
            if (DeviceFunctionBodies.TryGet(definition, compilation, out _) ||
                (DeviceExportTable.IsOutside(definition, compilation) &&
                 (DeviceExportTable.TryFind(definition, out _) || TryFindImportedStruct(function, out _))) ||
                definition.ContainingType is { TypeKind: TypeKind.Interface })
            {
                callees.Add(new CalleeSite(function, site));
            }
        }

        return callees.ToImmutable();
    }

    /// <summary>
    /// Whether <paramref name="site"/> is a method-group reference rather than a call (for analyses that inspect only
    /// actual calls and member accesses).
    /// </summary>
    internal static bool IsMethodGroupSite(SyntaxNode site, IMethodSymbol callee, SemanticModel model) =>
        callee.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension &&
        site is not (InvocationExpressionSyntax or CollectionExpressionSyntax or UsingStatementSyntax
            or LocalDeclarationStatementSyntax or VariableDeclaratorSyntax) &&
        model.GetOperation(site) is IMethodReferenceOperation
            or IDelegateCreationOperation { Target: IMethodReferenceOperation }
            or IConversionOperation { Operand: IMethodReferenceOperation };

    internal static void AddBodyCalls(IReadOnlyList<SyntaxNode> bodies, TypeArgumentMap typeArguments,
        Compilation compilation, ImmutableArray<CalleeSite>.Builder called)
    {
        var scratch = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var body in bodies)
        {
            if (compilation.ContainsSyntaxTree(body.SyntaxTree) &&
                compilation.GetCachedSemanticModel(body.SyntaxTree).GetOperation(body) is { } operation)
            {
                AddCalls(operation, typeArguments, compilation, called, scratch);
            }
        }
    }

    /// <summary>
    /// The methods that the bodies of <paramref name="definition"/> call as written, with their type parameters not
    /// replaced (extension methods in their static form); empty when it has no body in the compilation.
    /// </summary>
    internal static ImmutableArray<IMethodSymbol> GetWrittenCallees(IMethodSymbol definition, Compilation compilation)
    {
        if (!DeviceFunctionBodies.TryGet(definition, compilation, out var bodies))
        {
            return ImmutableArray<IMethodSymbol>.Empty;
        }

        var called = ImmutableArray.CreateBuilder<CalleeSite>();
        AddBodyCalls(bodies, TypeArgumentMap.None, compilation, called);
        var written = ImmutableArray.CreateBuilder<IMethodSymbol>(called.Count);
        called
            .AsStackEnumerable()
            .Select(static site => site.Callee.GetConstructedReducedFrom() ?? site.Callee)
            .AddTo(written);
        return written.ToImmutable();
    }

    // The calls of operation and of the operations that run inside it; the argument of nameof and calls that C# removes
    // are not run
    private static void AddCalls(IOperation operation, TypeArgumentMap typeArguments, Compilation compilation,
        ImmutableArray<CalleeSite>.Builder called, ImmutableArray<IMethodSymbol>.Builder scratch)
    {
        if (operation is INameOfOperation)
        {
            return;
        }

        if (operation is IInvocationOperation invocation &&
            RemovedCalls.IsRemoved(invocation.TargetMethod, invocation.Syntax.SyntaxTree))
        {
            return;
        }

        if (!AddConstrainedCalls(operation, typeArguments, compilation, called, scratch))
        {
            scratch.Clear();
            OperationCalls.Add(operation, typeArguments, scratch);
            foreach (var method in scratch)
            {
                called.Add(new CalleeSite(method, operation.Syntax));
            }
        }

        AddCallsWithoutOperation(operation, typeArguments, compilation, called);
        foreach (var child in operation.ChildOperations)
        {
            AddCalls(child, typeArguments, compilation, called, scratch);
        }
    }

    // A member of an interface reached through a type parameter (default(T).Value on T : IHas, T.Create(), a + b on
    // T : IAdditionOperators): the call runs the implementation of the type argument, found as the walk has replaced
    // the type parameter. A member that the type argument does not implement in the compilation (a default interface
    // member) stays the interface member, named UnknownFunctions; one of a type outside the compilation (int) runs no
    // device code of the compilation and adds nothing. False when operation is no such call
    private static bool AddConstrainedCalls(IOperation operation, TypeArgumentMap typeArguments,
        Compilation compilation, ImmutableArray<CalleeSite>.Builder called,
        ImmutableArray<IMethodSymbol>.Builder scratch)
    {
        if (GetConstrainedReceiver(operation) is not { } receiver)
        {
            return false;
        }

        scratch.Clear();
        OperationCalls.Add(operation, TypeArgumentMap.None, scratch);
        var concrete = typeArguments.Substitute(receiver);
        foreach (var member in scratch)
        {
            if (FindImplementation(typeArguments.Substitute(member), concrete, compilation) is { } implementation)
            {
                called.Add(new CalleeSite(implementation, operation.Syntax));
            }
        }

        return true;
    }

    /// <summary>
    /// The type parameter through which <paramref name="operation"/> calls a member of an interface, or null when it
    /// calls none that way: the constrained type of a static member or operator, or the receiver of an instance member.
    /// </summary>
    internal static ITypeParameterSymbol? GetConstrainedReceiver(IOperation operation) => operation switch
    {
        IInvocationOperation invocation when IsInterfaceMember(invocation.TargetMethod) =>
            AsTypeParameter(invocation.ConstrainedToType ?? invocation.Instance?.Type),
        IPropertyReferenceOperation property when IsInterfaceMember(property.Property) =>
            AsTypeParameter(property.ConstrainedToType ?? property.Instance?.Type),
        IBinaryOperation binary => AsTypeParameter(binary.ConstrainedToType),
        IUnaryOperation unary => AsTypeParameter(unary.ConstrainedToType),
        ICompoundAssignmentOperation compound => AsTypeParameter(compound.ConstrainedToType),
        IIncrementOrDecrementOperation increment => AsTypeParameter(increment.ConstrainedToType),
        IConversionOperation conversion => AsTypeParameter(conversion.ConstrainedToType),
        _ => null
    };

    // The record of the struct of another assembly that declares function (as constructed)
    internal static bool TryFindImportedStruct(IMethodSymbol function, out ImportedDeviceRecord structRecord)
    {
        if (function.ContainingType is { TypeKind: TypeKind.Struct } type &&
            DeviceExportTable.TryFind(type, out structRecord) &&
            structRecord.Record.Kind == DeviceExportRecord.StructKind)
        {
            return true;
        }

        structRecord = default;
        return false;
    }

    private static bool IsInterfaceMember(ISymbol member) => member.ContainingType is { TypeKind: TypeKind.Interface };

    private static ITypeParameterSymbol? AsTypeParameter(ITypeSymbol? type) => type as ITypeParameterSymbol;

    // The implementation of member in type; member itself when type is of the compilation and does not implement it
    // with a member of its own (a default interface member, or a type parameter not replaced); null for a type outside
    // the compilation without one
    private static IMethodSymbol? FindImplementation(IMethodSymbol member, ITypeSymbol type, Compilation compilation)
    {
        // [WHY] FindImplementationForInterfaceMember requires the unconstructed generic method (ConstructedFrom keeps
        // any constructed generic interface type such as IActionOf<BarrierAction>); construct the implementation with
        // member's type arguments afterwards.
        var lookupMember = member.IsGenericMethod ? member.ConstructedFrom : member;
        if (type is INamedTypeSymbol named && named.FindImplementationForInterfaceMember(lookupMember) is IMethodSymbol
            {
                ContainingType.TypeKind: not TypeKind.Interface
            } implementation)
        {
            return member.IsGenericMethod &&
                   implementation.IsGenericMethod &&
                   implementation.TypeParameters.Length == member.TypeArguments.Length
                ? implementation.ConstructedFrom.Construct(CopyTypeArguments(member.TypeArguments))
                : implementation;
        }

        return type is INamedTypeSymbol outside && DeviceExportTable.IsOutside(outside, compilation) ? null : member;
    }

    private static ITypeSymbol[] CopyTypeArguments(ImmutableArray<ITypeSymbol> types)
    {
        var copy = new ITypeSymbol[types.Length];
        types.CopyTo(copy);
        return copy;
    }

    // The calls that C# makes for operation without an operation of their own (OperationCalls does not list them)
    private static void AddCallsWithoutOperation(IOperation operation, TypeArgumentMap typeArguments,
        Compilation compilation, ImmutableArray<CalleeSite>.Builder called)
    {
        switch (operation)
        {
            case IMethodReferenceOperation reference:
                called.Add(new CalleeSite(typeArguments.Substitute(reference.Method), reference.Syntax));
                break;
            case IUsingOperation usingOperation:
                AddUsingDisposes(usingOperation, typeArguments, compilation, called);
                break;
            case IUsingDeclarationOperation declaration:
                AddUsingDeclarationDisposes(declaration, typeArguments, compilation, called);
                break;
            case ICollectionExpressionOperation { ConstructMethod: { } builder } collection:
                called.Add(new CalleeSite(typeArguments.Substitute(builder), collection.Syntax));
                break;
            case IVariableDeclaratorOperation declarator:
                AddFixedPinnable(declarator, typeArguments, compilation, called);
                break;
            case IBinaryOperation binary:
                AddMacroTarget(binary.OperatorMethod, binary.Syntax, typeArguments, compilation, called);
                AddTruthTest(binary, typeArguments, called);
                break;
            case IUnaryOperation unary:
                AddMacroTarget(unary.OperatorMethod, unary.Syntax, typeArguments, compilation, called);
                break;
        }
    }

    // using (var r = ...): the Dispose of each local; using (r): the Dispose of the resource's type
    private static void AddUsingDisposes(IUsingOperation usingOperation, TypeArgumentMap typeArguments,
        Compilation compilation, ImmutableArray<CalleeSite>.Builder called)
    {
        if (!usingOperation.Locals.IsEmpty)
        {
            usingOperation.Locals
                .AsStackEnumerable()
                .Do((usingOperation.Syntax, typeArguments, compilation, called), static (local, state) => AddDispose(
                    local.Type, state.Syntax, state.typeArguments, state.compilation, state.called));
        }
        else if (usingOperation.Resources is { } resource)
        {
            AddDispose(resource.Type, usingOperation.Syntax, typeArguments, compilation, called);
        }
    }

    // using var r = ...: the Dispose of each declared variable
    private static void AddUsingDeclarationDisposes(IUsingDeclarationOperation declaration,
        TypeArgumentMap typeArguments, Compilation compilation, ImmutableArray<CalleeSite>.Builder called) =>
        declaration.DeclarationGroup.Declarations
            .AsStackEnumerable()
            .SelectMany(static group => group.Declarators
                .AsStackEnumerable()
            ).Do((declaration.Syntax, typeArguments, compilation, called), static (declarator, state) => AddDispose(
                declarator.Symbol.Type, state.Syntax, state.typeArguments, state.compilation, state.called));

    // The Dispose of type as the walk has replaced its type parameters (using (var r = new T()) runs the Dispose of
    // the type argument)
    private static void AddDispose(ITypeSymbol? type, SyntaxNode site, TypeArgumentMap typeArguments,
        Compilation compilation, ImmutableArray<CalleeSite>.Builder called)
    {
        if (type is not null && typeArguments.Substitute(type) is INamedTypeSymbol named &&
            UsingDisposeLookup.Find(named, compilation) is { } dispose)
        {
            called.Add(new CalleeSite(dispose, site));
        }
    }

    // fixed (int* p = e): the GetPinnableReference that C# calls for e
    // [WHY] Roslyn does not make the operation of a fixed statement public: its declarators are read
    private static void AddFixedPinnable(IVariableDeclaratorOperation declarator, TypeArgumentMap typeArguments,
        Compilation compilation, ImmutableArray<CalleeSite>.Builder called)
    {
        if (declarator.Syntax is VariableDeclaratorSyntax
            {
                Parent.Parent: FixedStatementSyntax, Initializer.Value: { } initializer
            } &&
            PinnableReferenceLookup.Find(initializer, compilation.GetCachedSemanticModel(initializer.SyntaxTree)) is
                { } pinnable)
        {
            called.Add(new CalleeSite(
                typeArguments.Substitute(pinnable.GetConstructedReducedFrom() ?? pinnable),
                declarator.Syntax));
        }
    }

    // The operator true / false that && and || test (the UnaryOperatorMethod of the operation, which Roslyn does not
    // make public), found as the operator macro expansion finds it
    private static void AddTruthTest(IBinaryOperation binary, TypeArgumentMap typeArguments,
        ImmutableArray<CalleeSite>.Builder called)
    {
        if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr &&
            OperatorMacroSyntax.FindTest(binary) is { } test)
        {
            called.Add(new CalleeSite(typeArguments.Substitute(test), binary.Syntax));
        }
    }

    private static void AddMacroTarget(IMethodSymbol? operatorMethod, SyntaxNode site, TypeArgumentMap typeArguments,
        Compilation compilation, ImmutableArray<CalleeSite>.Builder called)
    {
        if (operatorMethod != null &&
            OperatorMacroResolver.TryGetTarget(typeArguments.Substitute(operatorMethod), compilation, out var target))
        {
            called.Add(new CalleeSite(typeArguments.Substitute(target), site));
        }
    }
}
