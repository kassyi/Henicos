using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Decides what a call in GPU code, or a method group that device code names, calls (<see cref="CallTargetPlan"/>):
/// the target must be removed by C#, synthesized by lowering, emitted, mapped by the dialect or written by a rule; any
/// other target is a failure, GSH0107 for a method of the compilation and GSH0109 for a method of another assembly. An
/// intrinsic that the dialect declares it does not implement is a warning (GSH0054) and a runtime failure.
/// An invocation that binds to no method is decided by <see cref="ClassifyUntyped"/>.
/// </summary>
/// <remarks>[WHY] A closed list; see <see cref="ClassifyMethod"/> and <see cref="Classify"/>.</remarks>
internal static class CallTargetClassifier
{
    /// <summary>The reason given for a static method without the attribute.</summary>
    public const string MissingAttribute =
        "only [GpuDeviceFunction] methods are compiled for the GPU; add [GpuDeviceFunction] to it";

    /// <summary>The reason given for an interface or abstract method.</summary>
    public const string ThroughInterface =
        "calls through an interface or an abstract method are not supported in GPU code; call a static " +
        "[GpuDeviceFunction] method of the concrete type instead";

    /// <summary>The GSH0003 text for an invocation that binds to no method (<see cref="ClassifyUntyped"/>).</summary>
    public const string UnboundCall = "call that binds to no method at compile time";

    /// <summary>The GSH0003 text for a call through a function pointer (<see cref="ClassifyUntyped"/>).</summary>
    public const string FunctionPointerCall = "call through a function pointer";

    private const string InternalNamePrefix = "__gsharp_";

    private const string NameofKeyword = "nameof";

    // The types whose members a generic math member of a numeric type has the same meaning as
    private static readonly string[] s_mathTypeNames =
        ["System." + TypeConstants.MathType, "System." + TypeConstants.MathFType];

    /// <summary>
    /// The GSH0003 text for an instance <c>[GpuDeviceFunction]</c> method of a struct used as a method group.
    /// </summary>
    public const string InstanceDeviceFunctionMethodGroup =
        "method group of an instance method of a struct (the delegate would hold a boxed copy of the receiver)";

    /// <summary>
    /// Whether <paramref name="method"/> is an unreduced instance device function emitted as a global C++ function,
    /// which cannot form a delegate without boxing a copy of the struct receiver (GSH0003).
    /// </summary>
    public static bool IsUnsupportedInstanceMethodGroup(IMethodSymbol method, ITranspilerDialect dialect) =>
        method is { IsStatic: false, ReducedFrom: null } &&
        CppNameResolver.IsEmittedDeviceFunction(method, dialect);

    /// <summary>
    /// Classifies a call from symbol and tree information alone, returning <see langword="null"/> when syntax-specific
    /// rules (<see cref="Classify"/>) are needed.
    /// </summary>
    /// <remarks>
    /// [WHY] A closed list, as Roslyn binds every name before it generates code and ComputeSharp accepts only
    /// intrinsics and methods with source: the standard call emission writes the C# name of the callee, which no
    /// dialect defines for a method it does not map, and the native build would fail without a location in the user's
    /// source. The transpiler fails the calls that reach the standard emission from this plan, and the analyzer
    /// (<c>CallTargetRule</c>) reports GSH0109 from the same plan for the project's dialect, so the decision exists
    /// once. The rules that write a call before the standard emission are classified here as well, by the test each
    /// rule claims its calls with, so that the analyzer, which sees the calls as written, does not report them.
    /// </remarks>
    internal static CallTargetPlan? ClassifyMethod(IMethodSymbol called, SyntaxTree? userTree, Compilation compilation,
        ITranspilerDialect dialect)
    {
        if (userTree is null)
        {
            return new CallTargetPlan(CallTargetKind.Synthesized);
        }

        var method = (called.ReducedFrom ?? called).OriginalDefinition;
        if (method.Name.StartsWith(InternalNamePrefix, StringComparison.Ordinal))
        {
            return new CallTargetPlan(CallTargetKind.Synthesized);
        }

        if (method.MethodKind != MethodKind.Ordinary)
        {
            return new CallTargetPlan(CallTargetKind.NotOrdinary);
        }

        if (RemovedCalls.IsRemoved(method, userTree))
        {
            return new CallTargetPlan(CallTargetKind.Removed);
        }

        if (GetMappedKind(called, method, compilation, dialect) is { } mapped)
        {
            // [WHY] The native API YAML declares the intrinsics a dialect cannot reproduce (mbarrier, WMMA on the CPU):
            // the dialect's prelude does not define them, so the call becomes a runtime failure
            return mapped == CallTargetKind.DialectIntrinsic && !DialectCallSupport.ImplementsIntrinsic(method, dialect)
                ? new CallTargetPlan(CallTargetKind.UnsupportedIntrinsic, GetDisplayName(method), Dialect: dialect.Name)
                : new CallTargetPlan(mapped);
        }

        return null;
    }

    /// <summary>
    /// What <paramref name="call"/> of <paramref name="called"/> calls in <paramref name="dialect"/>: an invocation, or
    /// a method group that device code names (<see cref="MethodGroupReference"/>), in the tree of
    /// <paramref name="model"/> or a lowered copy of it.
    /// </summary>
    public static CallTargetPlan Classify(IMethodSymbol called, SyntaxNode call, SemanticModel model,
        ITranspilerDialect dialect)
    {
        // [WHY] A call copied into a lowered tree (an inlined iterator or foreach body, a state machine) is decided as
        // well, on the file it was written in, which its original-span annotation names; a call that lowering
        // synthesized has no written call to report
        var userTree = RemovedCalls.GetUserTree(call, model);
        if (ClassifyMethod(called, userTree, model.Compilation, dialect) is { } plan)
        {
            return plan;
        }

        var method = (called.ReducedFrom ?? called).OriginalDefinition;
        if (GpuMacroClassifier.Classify(method) is { Kind: not GpuMacroKind.None } macro)
        {
            return MacroCallTarget.Classify(macro, method, call);
        }

        if (IsClaimedByRule(called, method, call))
        {
            return new CallTargetPlan(CallTargetKind.ClaimedByRule);
        }

        if (ArrayViewMethods.IsViewMember(method))
        {
            return new CallTargetPlan(CallTargetKind.ArrayViewMember);
        }

        if (IsDeclaredIn(method, model.Compilation))
        {
            return new CallTargetPlan(CallTargetKind.Unemitted, GetDisplayName(method), GetUnemittedReason(method));
        }

        return Unmapped(method, dialect, GetAlternativeDetail(method, model.Compilation, dialect));
    }

    /// <summary>
    /// Whether standard call emission converts primitive arguments of <paramref name="symbol"/> to their parameter
    /// types under <paramref name="dialect"/>.
    /// </summary>
    internal static bool ConvertsArguments(IMethodSymbol symbol, ITranspilerDialect dialect) =>
        dialect.TryMapIntrinsic(symbol, out _) &&
        !symbol.HasAttribute(KnownAttributes.CudaIntrinsic) &&
        symbol.ContainingType?.HasAttribute(KnownAttributes.CudaIntrinsicPattern) != true;

    /// <summary>
    /// Which category of <c>TranspilerRuleRegistry.InvocationRules</c> claims a call of <paramref name="called"/> (a
    /// superset of the rules that match syntax).
    /// </summary>
    internal static InvocationRuleClaim ClaimOf(IMethodSymbol called, bool isMemberAccessCall, Compilation compilation)
    {
        if (BclIntrinsicMapBuilder.GetOrBuildMap(compilation).ContainsKey(called.OriginalDefinition))
        {
            return InvocationRuleClaim.BclIntrinsicMap;
        }

        var method = (called.ReducedFrom ?? called).OriginalDefinition;
        if (GpuMacroClassifier.Classify(method).Kind != GpuMacroKind.None ||
            MathAbsTranspilerRule.Claims(method) ||
            IsClaimedBySymbol(called, method, isMemberAccessCall))
        {
            return InvocationRuleClaim.Other;
        }

        return InvocationRuleClaim.None;
    }

    /// <summary>
    /// What <paramref name="call"/>, an invocation that binds to no method, is in the tree of <paramref name="model"/>
    /// or a lowered copy of it: a call that lowering synthesized (a prelude helper, <c>__gsharp_*</c>), which is
    /// written as it is; <c>nameof</c>, whose value reaches here only where it stays a string (GSH0002); a call C#
    /// cannot bind, which C# reports; any other (a call through a function pointer, a call bound at run time) is a
    /// failure (GSH0003), never a name written as it is.
    /// </summary>
    /// <remarks>
    /// [WHY] In the user's code only <c>nameof</c> binds to no method, and its value is a constant: where the value is
    /// not a string (<c>nameof(T).Length</c>, <c>nameof(T) == "T"</c>) the rule of the enclosing expression writes the
    /// value and never visits the <c>nameof</c>.
    /// </remarks>
    public static CallTargetPlan ClassifyUntyped(InvocationExpressionSyntax call, SemanticModel model)
    {
        // A call copied into a lowered tree is decided on the call written in the user's file; a call that lowering
        // synthesized (a prelude helper, __gsharp_*) has no original span, or one that is not an invocation
        if (!model.TryGetOriginalNode(call, out var original, out var writtenModel) ||
            original is not InvocationExpressionSyntax written)
        {
            return new CallTargetPlan(CallTargetKind.Synthesized);
        }

        if (written.Expression is IdentifierNameSyntax { Identifier.ValueText: NameofKeyword } &&
            writtenModel.GetConstantValue(written) is { HasValue: true, Value: string })
        {
            return new CallTargetPlan(CallTargetKind.NameofString);
        }

        // A call that does not bind in C# (no such name, no applicable overload) is the C# compiler's error
        var symbolInfo = writtenModel.GetSymbolInfo(written);
        if (symbolInfo.CandidateReason is not (CandidateReason.None or CandidateReason.LateBound) ||
            writtenModel.GetTypeInfo(written).Type is null or { TypeKind: TypeKind.Error })
        {
            return new CallTargetPlan(CallTargetKind.NotCSharp);
        }

        var detail = writtenModel.GetTypeInfo(written.Expression).Type is IFunctionPointerTypeSymbol
            ? FunctionPointerCall
            : UnboundCall;
        return new CallTargetPlan(CallTargetKind.Unbound, Detail: detail);
    }

    /// <summary>
    /// Whether <paramref name="reference"/> (a method name or its enclosing member access) is the target of an
    /// invocation expression (<c>F(...)</c> or <c>T.F(...)</c>).
    /// </summary>
    public static bool IsCalledReference(ExpressionSyntax reference) =>
        (reference.Parent is InvocationExpressionSyntax invocation && invocation.Expression == reference) ||
        (reference.Parent is MemberAccessExpressionSyntax access && access.Name == reference &&
         access.Parent is InvocationExpressionSyntax outer && outer.Expression == access);

    /// <summary>The name of <paramref name="called"/> in the GSH0107 message: <c>Type.Method</c>.</summary>
    public static string GetDisplayName(IMethodSymbol called)
    {
        var method = (called.ReducedFrom ?? called).OriginalDefinition;
        return method.ContainingType is { } type ? type.Name + "." + method.Name : method.Name;
    }

    // The kind of a method that has a definition on the device, or null
    private static CallTargetKind? GetMappedKind(IMethodSymbol called, IMethodSymbol method, Compilation compilation,
        ITranspilerDialect dialect)
    {
        if (method.HasAttribute(KnownAttributes.GpuDeviceFunction) || method.HasAttribute(KnownAttributes.GpuKernel))
        {
            return CallTargetKind.DeviceFunction;
        }

        if (method.HasAttribute(KnownAttributes.GpuOperatorMacro))
        {
            return CallTargetKind.OperatorMacro;
        }

        if (IsDialectIntrinsic(called, method, dialect))
        {
            return CallTargetKind.DialectIntrinsic;
        }

        return BclIntrinsicMapBuilder.GetOrBuildMap(compilation).ContainsKey(method)
            ? CallTargetKind.BclIntrinsicMap
            : null;
    }

    private static bool IsDialectIntrinsic(IMethodSymbol called, IMethodSymbol method, ITranspilerDialect dialect) =>
        method.HasAttribute(KnownAttributes.CudaIntrinsic) ||
        method.HasAttribute(KnownAttributes.CudaImport) ||
        method.ContainingType?.HasAttribute(KnownAttributes.CudaIntrinsicPattern) == true ||
        dialect.TryMapIntrinsic(called, out _);

    // The calls that a rule of the transpiler writes before the standard emission (TranspilerRuleRegistry), each by
    // the test the rule claims its calls with
    private static bool IsClaimedByRule(IMethodSymbol called, IMethodSymbol method, SyntaxNode call) =>
        IsClaimedBySymbol(called, method,
            call is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax });

    private static bool IsClaimedBySymbol(IMethodSymbol called, IMethodSymbol method, bool isMemberAccessCall) =>
        StackLinqHelper.IsStackLinqMethod(method) ||
        SpanWriteClassifier.ClassifyMethod(called) != SpanMemberKind.None ||
        NullableGetValueOrDefaultRule.Claims(method, isMemberAccessCall);

    private static string GetUnemittedReason(IMethodSymbol method) =>
        method.IsAbstract || method.ContainingType?.TypeKind == TypeKind.Interface
            ? ThroughInterface
            : MissingAttribute;

    private static CallTargetPlan Unmapped(IMethodSymbol method, ITranspilerDialect dialect, string detail) =>
        new(CallTargetKind.Unmapped, FormatMethod(method), detail, dialect.Name);

    // Gpu.SingleThread(Action): the type and the parameters, as the mapping of the dialect is per overload
    private static string FormatMethod(IMethodSymbol method) =>
        method.ContainingType is { } type
            ? IteratorTypeResolver.FormatType(type) + "." + method.FormatSignature()
            : method.FormatSignature();

    // What the dialect implements instead: the Math or MathF member with the same parameters for a generic math member
    // of a numeric type (int.Max(int, int) is Math.Max(int, int)), otherwise the mapped overloads of the same name, in
    // the type and, for a numeric type, in Math and MathF
    private static string GetAlternativeDetail(IMethodSymbol method, Compilation compilation,
        ITranspilerDialect dialect)
    {
        if (method.ContainingType is not { } owner)
        {
            return string.Empty;
        }

        var candidates = ImmutableArray.CreateBuilder<IMethodSymbol>();
        AddMappedOverloads(owner, method, compilation, dialect, candidates);
        if (owner.SpecialType.IsArithmetic())
        {
            foreach (var mathType in s_mathTypeNames)
            {
                var type = compilation.GetTypeByMetadataName(mathType);
                AddMappedOverloads(type, method, compilation, dialect, candidates);
            }

            if (candidates
                    .AsStackEnumerable()
                    .TryFirst(method, HasSameParameters, out var equivalent))
            {
                return "; use " + FormatMethod(equivalent) + " instead";
            }
        }

        return candidates.Count == 0 ? string.Empty : FormatImplemented(candidates);
    }

    // "; the dialect implements Math.Abs(double), Math.Abs(float)", in ordinal order
    private static string FormatImplemented(ImmutableArray<IMethodSymbol>.Builder candidates)
    {
        var names = ImmutableArray.CreateBuilder<string>(candidates.Count);
        candidates
            .AsStackEnumerable()
            .Select(FormatMethod)
            .AddTo(names);
        names.Sort(StringComparer.Ordinal);
        using var text = new SourceWriter();
        text.Append("; the dialect implements ");
        for (var i = 0; i < names.Count; i++)
        {
            text.AppendIf(i > 0, ", ");
            text.Append(names[i]);
        }

        return text.ToString();
    }

    private static void AddMappedOverloads(INamedTypeSymbol? type, IMethodSymbol method, Compilation compilation,
        ITranspilerDialect dialect, ImmutableArray<IMethodSymbol>.Builder candidates)
    {
        if (type is null)
        {
            return;
        }

        foreach (var member in type.GetMembers(method.Name))
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } candidate &&
                candidate.IsStatic == method.IsStatic &&
                !SymbolEqualityComparer.Default.Equals(candidate, method) &&
                GetMappedKind(candidate, candidate, compilation, dialect) is not null)
            {
                candidates.Add(candidate);
            }
        }
    }

    private static bool HasSameParameters(IMethodSymbol candidate, IMethodSymbol method) =>
        candidate.Parameters.Length == method.Parameters.Length &&
        candidate.Parameters
            .AsStackEnumerable()
            .All(method, static (parameter, m) =>
                parameter.RefKind == m.Parameters[parameter.Ordinal].RefKind &&
                SymbolEqualityComparer.Default.Equals(parameter.Type, m.Parameters[parameter.Ordinal].Type));

    private static bool IsDeclaredIn(IMethodSymbol method, Compilation compilation) =>
        method.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(compilation, static (reference, c) => c.ContainsSyntaxTree(reference.SyntaxTree));
}
