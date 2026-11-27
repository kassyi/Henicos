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
/// the walk stops at a call that makes a new construction at every turn of a cycle (<see cref="GenericExpansion"/>,
/// the rule of ECMA-335 II.9.2) to a definition that is already on the path. A program that does this cannot be
/// emitted anyway (#2199). A finite one (<c>C&lt;T&gt;</c> creating <c>C&lt;Wrap&lt;int&gt;&gt;</c>, or
/// <c>D&lt;T&gt;</c> creating <c>E&lt;int&gt;</c>, which creates <c>D&lt;Wrap&lt;U&gt;&gt;</c>) is walked whole.
/// </para>
/// </remarks>
internal static class DeviceFunctionReach
{
    /// <summary>
    /// The name that stands for functions the walk cannot name: a member of an interface reached through a type
    /// parameter whose type argument has no implementation of it in the compilation (a default interface member). It
    /// never equals a function of a ptxas line, and <see cref="Aot.SpillSuppressionPlan"/> lists no function when a
    /// kernel without the pragma reaches it.
    /// </summary>
    public const string UnknownFunctions = "*";

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
        var pending = new Stack<ReachStep>();
        var body = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody;
        if (body != null)
        {
            DirectCallees.Of([body], TypeArgumentMap.Of(kernel), compilation)
                .AsStackEnumerable()
                .Do(pending, static (site, stack) => stack.Push(new ReachStep(site.Callee, null)));
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
        {
            if (IsDefaultStructConstructor(f) ||
                !DeviceFunctionBodies.TryGet(DeviceFunctionEffects.GetDefinition(f), compilation, out var bodies))
            {
                return ImmutableArray<IMethodSymbol>.Empty;
            }

            var sites = DirectCallees.Of(bodies, TypeArgumentMap.Of(f), compilation);
            var callees = ImmutableArray.CreateBuilder<IMethodSymbol>(sites.Length);
            sites
                .AsStackEnumerable()
                .Select(static site => site.Callee)
                .AddTo(callees);
            return callees.ToImmutable();
        });

    // The name ptxas gives the function when it is a C++ function of its own; null for a struct member, a local
    // function, an operator macro (whose body never runs) or the parameterless constructor of a struct (default(T)).
    // A member of another assembly's struct without a record of its own is named by the struct's record, through which
    // DeviceImportAssembler.ExtendReach reaches what the struct's members call.
    private static string? GetEmittedName(IMethodSymbol function, Compilation compilation)
    {
        var definition = DeviceFunctionEffects.GetDefinition(function);
        if (definition.ContainingType is { TypeKind: TypeKind.Interface })
        {
            return UnknownFunctions;
        }

        if (DeviceExportTable.IsOutside(definition, compilation) && !DeviceExportTable.TryFind(definition, out _) &&
            DirectCallees.TryFindImportedStruct(function, out var structRecord))
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

    // The implicit parameterless constructor of a struct is default(T) and runs nothing
    private static bool IsDefaultStructConstructor(IMethodSymbol function) =>
        DeviceFunctionBodies.IsDefaultStructConstructor(function);

    // One function of the walk and the step that called it
    private sealed class ReachStep(IMethodSymbol function, ReachStep? caller)
    {
        public IMethodSymbol Function { get; } = function;

        public ReachStep? Caller { get; } = caller;
    }

    // Whether the step stops polymorphic recursion: the edge from its caller lies on a cycle of the type parameters
    // that makes a new construction at every turn (GenericExpansion), and a function on the path to it is a
    // construction of the same definition, so each definition is walked at least once
    private static bool StopsPolymorphicRecursion(ReachStep step, Compilation compilation)
    {
        if (step.Caller is not { } caller)
        {
            return false;
        }

        var definition = GenericExpansion.GetCanonicalDefinition(step.Function);
        if (!GenericExpansion.GetExpandingCallees(GenericExpansion.GetCanonicalDefinition(caller.Function), compilation)
                .AsStackEnumerable()
                .Any(definition, static (callee, own) => callee.IsEqualTo(own)))
        {
            return false;
        }

        for (var enclosing = caller; enclosing != null; enclosing = enclosing.Caller)
        {
            if (GenericExpansion.GetCanonicalDefinition(enclosing.Function).IsEqualTo(definition))
            {
                return true;
            }
        }

        return false;
    }
}
