using Kassyi.GSharp.Compiler.Models;
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
/// <see cref="Aot.DeviceImportAssembler.ExtendReach"/>. A member of another assembly's struct that has no record of its
/// own (an accessor, <c>Dispose</c>) is named by its struct's record, which stands for every member of the struct.
/// That name (<c>__gsharp_ty_*</c>) can then appear on the suppression line; it never equals the function of a ptxas
/// line, so it silences nothing.
/// </para>
/// <para>
/// A generic struct whose constructor constructs the same struct with a type argument that contains its own
/// (<c>Deep&lt;T&gt;</c> creating <c>Deep&lt;Wrap&lt;T&gt;&gt;</c>) would give a new construction at every step:
/// the walk stops at an expansive call (one whose type arguments, as written in the caller, hold one of the caller's
/// type parameters inside another type; ECMA-335 II.9.2) to a definition that is already on the path. A program that
/// does this cannot be emitted anyway (#2199). A finite one (<c>C&lt;T&gt;</c> creating
/// <c>C&lt;Wrap&lt;int&gt;&gt;</c>) is walked whole.
/// </para>
/// </remarks>
internal static class DeviceFunctionReach
{
    // [WHY] The direct callees of each function are found once per compilation; the walk of each kernel reuses them.
    private static readonly PerCompilationSymbolCache<IMethodSymbol, ImmutableArray<IMethodSymbol>> s_callees = new();

    // [WHY] Which callees of a generic definition grow its type arguments, found once per definition
    private static readonly PerCompilationSymbolCache<IMethodSymbol, ImmutableArray<IMethodSymbol>> s_expansiveCallees =
        new();

    /// <summary>
    /// The names of the functions that <paramref name="kernel"/> (declared by <paramref name="declaration"/>) reaches,
    /// sorted, without repeats.
    /// </summary>
    public static EquatableArray<string> GetReachedFunctionNames(IMethodSymbol kernel,
        MethodDeclarationSyntax declaration, Compilation compilation)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<ReachStep>();
        var body = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody;
        if (body != null)
        {
            CollectCallees([body], TypeArgumentMap.Of(kernel), compilation)
                .AsStackEnumerable()
                .Do(pending, static (callee, stack) => stack.Push(new ReachStep(callee, null)));
        }

        while (pending.Count > 0)
        {
            var step = pending.Pop();
            if (visited.Contains(step.Function) || StopsPolymorphicRecursion(step, compilation))
            {
                continue;
            }

            visited.Add(step.Function);
            if (GetEmittedName(step.Function, compilation) is { } name)
            {
                names.Add(name);
            }

            GetDirectCallees(step.Function, compilation)
                .AsStackEnumerable()
                .Do((pending, step), static (callee, state) => state.pending.Push(new ReachStep(callee, state.step)));
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
    // functions and struct members of another assembly that recorded them or their struct
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
                (DeviceExportTable.IsOutside(definition, compilation) &&
                 (DeviceExportTable.TryFind(definition, out _) || TryFindImportedStruct(function, out _))))
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
            case IUsingOperation usingOperation:
                AddUsingDisposes(usingOperation, compilation, called);
                break;
            case IUsingDeclarationOperation declaration:
                AddUsingDeclarationDisposes(declaration, compilation, called);
                break;
            case ICollectionExpressionOperation { ConstructMethod: { } builder }:
                called.Add(typeArguments.Substitute(builder));
                break;
            case IVariableDeclaratorOperation declarator:
                AddFixedPinnable(declarator, compilation, called);
                break;
            case IBinaryOperation binary:
                AddMacroTarget(binary.OperatorMethod, compilation, called);
                AddTruthTest(binary, typeArguments, called);
                break;
            case IUnaryOperation unary:
                AddMacroTarget(unary.OperatorMethod, compilation, called);
                break;
        }
    }

    // using (var r = ...): the Dispose of each local; using (r): the Dispose of the resource's type
    private static void AddUsingDisposes(IUsingOperation usingOperation, Compilation compilation,
        ImmutableArray<IMethodSymbol>.Builder called)
    {
        if (!usingOperation.Locals.IsEmpty)
        {
            usingOperation.Locals
                .AsStackEnumerable()
                .Do((compilation, called), static (local, state) => AddDispose(local.Type, state.compilation,
                    state.called));
        }
        else if (usingOperation.Resources is { } resource)
        {
            AddDispose(resource.Type, compilation, called);
        }
    }

    // using var r = ...: the Dispose of each declared variable
    private static void AddUsingDeclarationDisposes(IUsingDeclarationOperation declaration, Compilation compilation,
        ImmutableArray<IMethodSymbol>.Builder called) =>
        declaration.DeclarationGroup.Declarations
            .AsStackEnumerable()
            .SelectMany(static group => group.Declarators
                .AsStackEnumerable()
            ).Do((compilation, called), static (declarator, state) => AddDispose(declarator.Symbol.Type,
                state.compilation, state.called));

    private static void AddDispose(ITypeSymbol? type, Compilation compilation,
        ImmutableArray<IMethodSymbol>.Builder called)
    {
        if (type is INamedTypeSymbol named && UsingTranspiler.FindDisposeMethod(named, compilation) is { } dispose)
        {
            called.Add(dispose);
        }
    }

    // fixed (int* p = e): the GetPinnableReference that C# calls for e
    // [WHY] Roslyn does not make the operation of a fixed statement public: its declarators are read
    private static void AddFixedPinnable(IVariableDeclaratorOperation declarator, Compilation compilation,
        ImmutableArray<IMethodSymbol>.Builder called)
    {
        if (declarator.Syntax is VariableDeclaratorSyntax
            {
                Parent.Parent: FixedStatementSyntax, Initializer.Value: { } initializer
            } &&
            PinnableReferenceMethod.Find(initializer, compilation.GetCachedSemanticModel(initializer.SyntaxTree)) is
                { } pinnable)
        {
            called.Add(pinnable);
        }
    }

    // The operator true / false that && and || test (the UnaryOperatorMethod of the operation, which Roslyn does not
    // make public), found as the operator macro expansion finds it
    private static void AddTruthTest(IBinaryOperation binary, TypeArgumentMap typeArguments,
        ImmutableArray<IMethodSymbol>.Builder called)
    {
        if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr &&
            OperatorMacroSyntax.FindTest(binary) is { } test)
        {
            called.Add(typeArguments.Substitute(test));
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
    // function, an operator macro (whose body never runs) or the parameterless constructor of a struct (default(T)).
    // A member of another assembly's struct without a record of its own is named by the struct's record, through which
    // DeviceImportAssembler.ExtendReach reaches what the struct's members call.
    private static string? GetEmittedName(IMethodSymbol function, Compilation compilation)
    {
        var definition = DeviceFunctionEffects.GetDefinition(function);
        if (DeviceExportTable.IsOutside(definition, compilation) && !DeviceExportTable.TryFind(definition, out _) &&
            TryFindImportedStruct(function, out var structRecord))
        {
            return structRecord.Record.Names.CppName;
        }

        return definition switch
        {
            _ when IsDefaultStructConstructor(definition) => null,
            { MethodKind: MethodKind.Constructor } => GlobalCppNames.GetConstructorName(definition),
            { MethodKind: MethodKind.Conversion } => GlobalCppNames.GetFunctionName(definition),
            { MethodKind: MethodKind.UserDefinedOperator }
                when OperatorMacroSyntax.GetMacroAttribute(definition) == null =>
                GlobalCppNames.GetFunctionName(definition),
            { MethodKind: MethodKind.Ordinary, IsStatic: true } => GlobalCppNames.GetFunctionName(definition),
            _ => null
        };
    }

    // The record of the struct of another assembly that declares function (as constructed)
    private static bool TryFindImportedStruct(IMethodSymbol function, out ImportedDeviceRecord structRecord)
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

    // The implicit parameterless constructor of a struct is default(T) and runs nothing
    private static bool IsDefaultStructConstructor(IMethodSymbol function) =>
        function is { IsImplicitlyDeclared: true, Parameters.Length: 0, MethodKind: MethodKind.Constructor } &&
        function.ContainingType.IsValueType;

    // One function of the walk and the step that called it
    private sealed class ReachStep(IMethodSymbol function, ReachStep? caller)
    {
        public IMethodSymbol Function { get; } = function;

        public ReachStep? Caller { get; } = caller;
    }

    // Whether the step stops polymorphic recursion: the edge from its caller is expansive (ExpansiveCallees) and a
    // function on the path to it is a construction of the same definition. Only an expansive edge makes a new type at
    // every turn of a cycle (ECMA-335 II.9.2), so a finite program (C<T> creating C<Wrap<int>>) is walked whole
    private static bool StopsPolymorphicRecursion(ReachStep step, Compilation compilation)
    {
        if (step.Caller is not { } caller)
        {
            return false;
        }

        var definition = DeviceFunctionEffects.GetDefinition(step.Function);
        if (!GetExpansiveCallees(DeviceFunctionEffects.GetDefinition(caller.Function), compilation)
                .AsStackEnumerable()
                .Any(definition, static (callee, own) => callee.IsEqualTo(own)))
        {
            return false;
        }

        for (var enclosing = caller; enclosing != null; enclosing = enclosing.Caller)
        {
            if (DeviceFunctionEffects.GetDefinition(enclosing.Function).IsEqualTo(definition))
            {
                return true;
            }
        }

        return false;
    }

    // The definitions that the unsubstituted body of definition calls with a type argument that holds one of
    // definition's own type parameters inside another type (Deep<Wrap<T>> written in Deep<T>): an expansive edge
    private static ImmutableArray<IMethodSymbol> GetExpansiveCallees(IMethodSymbol definition,
        Compilation compilation) =>
        s_expansiveCallees.For(compilation).GetOrAdd(definition, d =>
        {
            var parameters = GetTypeParameters(d);
            if (parameters.Count == 0 || !DeviceFunctionBodies.TryGet(d, compilation, out var bodies))
            {
                return ImmutableArray<IMethodSymbol>.Empty;
            }

            var called = ImmutableArray.CreateBuilder<IMethodSymbol>();
            foreach (var body in bodies)
            {
                if (compilation.ContainsSyntaxTree(body.SyntaxTree) &&
                    compilation.GetCachedSemanticModel(body.SyntaxTree).GetOperation(body) is { } operation)
                {
                    AddCalls(operation, TypeArgumentMap.None, compilation, called);
                }
            }

            var expansive = ImmutableArray.CreateBuilder<IMethodSymbol>();
            called
                .AsStackEnumerable()
                .Select(static method => method.GetConstructedReducedFrom() ?? method)
                .Where(parameters, static (callee, own) => GetTypeArguments(callee)
                    .AsStackEnumerable()
                    .Any(own, static (argument, typeParameters) => typeParameters
                        .AsStackEnumerable()
                        .Any(argument, static (parameter, written) => ContainsStrictly(written, parameter))
                    )
                ).Select(static callee => DeviceFunctionEffects.GetDefinition(callee))
                .AddTo(expansive);
            return expansive.ToImmutable();
        });

    // The type parameters of the method and of each type that contains it
    private static List<ITypeSymbol> GetTypeParameters(IMethodSymbol definition)
    {
        var parameters = new List<ITypeSymbol>(definition.TypeParameters);
        for (var type = definition.ContainingType; type != null; type = type.ContainingType)
        {
            parameters.AddRange(type.TypeParameters);
        }

        return parameters;
    }

    // The type arguments of the method and of each type that contains it, innermost first
    private static List<ITypeSymbol> GetTypeArguments(IMethodSymbol function)
    {
        var arguments = new List<ITypeSymbol>(function.TypeArguments);
        for (var type = function.ContainingType; type != null; type = type.ContainingType)
        {
            arguments.AddRange(type.TypeArguments);
        }

        return arguments;
    }

    // Whether inner is a type argument, array element or pointed-at type of outer, at any depth
    private static bool ContainsStrictly(ITypeSymbol outer, ITypeSymbol inner) => outer switch
    {
        INamedTypeSymbol named => named.TypeArguments
            .AsStackEnumerable()
            .Any(inner, static (argument, target) => argument.IsEqualTo(target) || ContainsStrictly(argument, target)),
        IArrayTypeSymbol array => array.ElementType.IsEqualTo(inner) || ContainsStrictly(array.ElementType, inner),
        IPointerTypeSymbol pointer =>
            pointer.PointedAtType.IsEqualTo(inner) || ContainsStrictly(pointer.PointedAtType, inner),
        _ => false
    };
}
