using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The C++ names of the functions that a kernel reaches, directly or through other functions, for the spill warnings
/// that ptxas reports per function (<see cref="Aot.SpillSuppressionPlan"/>).
/// </summary>
/// <remarks>
/// <para>
/// The calls are read from the operations of each body (<see cref="OperationCalls"/>, as
/// <see cref="ContextInferenceEngine"/> does), with the calls that C# makes without one: a method group converted to a
/// delegate, the <c>Dispose</c> of a <c>using</c>, the builder of a collection expression, the
/// <c>GetPinnableReference</c> of a <c>fixed</c> statement, the target of a <c>[GpuOperatorMacro]</c> operator and the
/// <c>operator true</c> / <c>operator false</c> that <c>&amp;&amp;</c> and <c>||</c> test. Lambdas and local functions
/// are read where they are written. The bodies are those of <see cref="DeviceFunctionBodies"/> (a device function, a
/// member of an emitted struct, a local function; a constructor with the member initializers of its struct).
/// </para>
/// <para>
/// Only the functions that ptxas names as functions of their own get a name: device functions, extension methods,
/// user-defined conversions and truth operators, constructors. A member of an emitted struct (an accessor,
/// <c>Dispose</c>) is a C++ member, which ptxas names in a nested form that the build task never matches, and a local
/// function or lambda counts as the function it is written in; their callees are followed all the same. A function of
/// another assembly is named here; what it reaches in its own assembly is added from its record by
/// <see cref="Aot.DeviceImportAssembler.ExtendReach"/>.
/// </para>
/// </remarks>
internal static class DeviceFunctionReach
{
    // [WHY] The direct callees of each function are found once per compilation; the walk of each kernel reuses them.
    private static readonly PerCompilationSymbolCache<IMethodSymbol, ImmutableArray<IMethodSymbol>> s_callees = new();

    /// <summary>
    /// The names of the functions that <paramref name="kernel"/> (declared by <paramref name="declaration"/>) reaches,
    /// sorted, without repeats.
    /// </summary>
    public static EquatableArray<string> GetReachedFunctionNames(IMethodSymbol kernel,
        MethodDeclarationSyntax declaration, Compilation compilation)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IMethodSymbol>();
        var body = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody;
        if (body != null)
        {
            CollectCallees([body], TypeArgumentMap.Of(kernel), compilation)
                .AsStackEnumerable()
                .Do(pending, static (callee, stack) => stack.Push(callee));
        }

        while (pending.Count > 0)
        {
            var function = pending.Pop();
            if (!visited.Add(function))
            {
                continue;
            }

            if (GetEmittedName(DeviceFunctionEffects.GetDefinition(function)) is { } name)
            {
                names.Add(name);
            }

            GetDirectCallees(function, compilation)
                .AsStackEnumerable()
                .Do(pending, static (callee, stack) => stack.Push(callee));
        }

        return names.ToImmutableArray().AsEquatableArray();
    }

    private static ImmutableArray<IMethodSymbol> GetDirectCallees(IMethodSymbol function, Compilation compilation) =>
        s_callees.For(compilation).GetOrAdd(function, f =>
            !IsDefaultStructConstructor(f) &&
            DeviceFunctionBodies.TryGet(DeviceFunctionEffects.GetDefinition(f), compilation, out var bodies)
                ? CollectCallees(bodies, TypeArgumentMap.Of(f), compilation)
                : ImmutableArray<IMethodSymbol>.Empty);

    // The functions with code of their own that the bodies call: those of the compilation with a body, and the device
    // functions and struct members of another assembly that recorded them
    private static ImmutableArray<IMethodSymbol> CollectCallees(IReadOnlyList<SyntaxNode> bodies,
        TypeArgumentMap typeArguments, Compilation compilation)
    {
        var callees = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var called = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var body in bodies)
        {
            if (compilation.ContainsSyntaxTree(body.SyntaxTree) &&
                compilation.GetCachedSemanticModel(body.SyntaxTree).GetOperation(body) is { } operation)
            {
                AddCalls(operation, typeArguments, compilation, called);
            }
        }

        foreach (var method in called)
        {
            var function = method.GetConstructedReducedFrom() ?? method;
            var definition = DeviceFunctionEffects.GetDefinition(function);
            if (DeviceFunctionBodies.TryGet(definition, compilation, out _) ||
                (DeviceExportTable.IsOutside(definition, compilation) && DeviceExportTable.TryFind(definition, out _)))
            {
                callees.Add(function);
            }
        }

        return callees.ToImmutable();
    }

    // The calls of operation and of the operations that run inside it; the argument of nameof is not run
    private static void AddCalls(IOperation operation, TypeArgumentMap typeArguments, Compilation compilation,
        ImmutableArray<IMethodSymbol>.Builder called)
    {
        if (operation is INameOfOperation)
        {
            return;
        }

        OperationCalls.Add(operation, typeArguments, called);
        AddCallsWithoutOperation(operation, typeArguments, compilation, called);
        foreach (var child in operation.ChildOperations)
        {
            AddCalls(child, typeArguments, compilation, called);
        }
    }

    // The calls that C# makes for operation without an operation of their own (OperationCalls does not list them)
    private static void AddCallsWithoutOperation(IOperation operation, TypeArgumentMap typeArguments,
        Compilation compilation, ImmutableArray<IMethodSymbol>.Builder called)
    {
        switch (operation)
        {
            case IMethodReferenceOperation reference:
                called.Add(typeArguments.Substitute(reference.Method));
                break;
            case IUsingOperation { Locals.IsEmpty: false } usingOperation:
                usingOperation.Locals
                    .AsStackEnumerable()
                    .Do((compilation, called), static (local, state) => AddDispose(local.Type, state.compilation,
                        state.called));
                break;
            case IUsingOperation { Resources: { } resource }:
                AddDispose(resource.Type, compilation, called);
                break;
            case IUsingDeclarationOperation declaration:
                declaration.DeclarationGroup.Declarations
                    .AsStackEnumerable()
                    .SelectMany(static group => group.Declarators
                        .AsStackEnumerable()
                    ).Do((compilation, called), static (declarator, state) => AddDispose(declarator.Symbol.Type,
                        state.compilation, state.called));
                break;
            case ICollectionExpressionOperation { ConstructMethod: { } builder }:
                called.Add(typeArguments.Substitute(builder));
                break;
            // [WHY] Roslyn does not make the operation of a fixed statement public: its declarators are read
            case IVariableDeclaratorOperation
            {
                Syntax: VariableDeclaratorSyntax { Parent.Parent: FixedStatementSyntax, Initializer.Value: { } value }
            }:
                AddPinnableReference(value, compilation, called);
                break;
            // The operator true / false that && and || test (the UnaryOperatorMethod of the operation, which Roslyn
            // does not make public), found as the operator macro expansion finds it
            case IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr
            } conditional:
                AddMacroTarget(conditional.OperatorMethod, compilation, called);
                if (OperatorMacroSyntax.FindTest(conditional) is { } test)
                {
                    called.Add(typeArguments.Substitute(test));
                }

                break;
            case IBinaryOperation binary:
                AddMacroTarget(binary.OperatorMethod, compilation, called);
                break;
            case IUnaryOperation unary:
                AddMacroTarget(unary.OperatorMethod, compilation, called);
                break;
        }
    }

    private static void AddDispose(ITypeSymbol? type, Compilation compilation,
        ImmutableArray<IMethodSymbol>.Builder called)
    {
        if (type is INamedTypeSymbol named && UsingTranspiler.FindDisposeMethod(named, compilation) is { } dispose)
        {
            called.Add(dispose);
        }
    }

    private static void AddPinnableReference(ExpressionSyntax initializer, Compilation compilation,
        ImmutableArray<IMethodSymbol>.Builder called)
    {
        if (PinnableReferenceMethod.Find(initializer, compilation.GetCachedSemanticModel(initializer.SyntaxTree)) is
            { } pinnable)
        {
            called.Add(pinnable);
        }
    }

    private static void AddMacroTarget(IMethodSymbol? operatorMethod, Compilation compilation,
        ImmutableArray<IMethodSymbol>.Builder called)
    {
        if (OperatorMacroResolver.TryGetTarget(operatorMethod, compilation, out var target))
        {
            called.Add(target);
        }
    }

    // The name ptxas gives the function when it is a C++ function of its own; null for a struct member, a local
    // function, an operator macro (whose body never runs) or the parameterless constructor of a struct (default(T))
    private static string? GetEmittedName(IMethodSymbol function) => function switch
    {
        _ when IsDefaultStructConstructor(function) => null,
        { MethodKind: MethodKind.Constructor } => GlobalCppNames.GetConstructorName(function),
        { MethodKind: MethodKind.Conversion } => GlobalCppNames.GetFunctionName(function),
        { MethodKind: MethodKind.UserDefinedOperator } when OperatorMacroSyntax.GetMacroAttribute(function) == null =>
            GlobalCppNames.GetFunctionName(function),
        { MethodKind: MethodKind.Ordinary, IsStatic: true } => GlobalCppNames.GetFunctionName(function),
        _ => null
    };

    // The implicit parameterless constructor of a struct is default(T) and runs nothing
    private static bool IsDefaultStructConstructor(IMethodSymbol function) =>
        function is { IsImplicitlyDeclared: true, Parameters.Length: 0, MethodKind: MethodKind.Constructor } &&
        function.ContainingType.IsValueType;
}
