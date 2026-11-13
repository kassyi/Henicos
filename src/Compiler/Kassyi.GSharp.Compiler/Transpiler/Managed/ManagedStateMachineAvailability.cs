namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Whether the managed state machine of a suspending device function can be emitted as valid C# into the partial type
/// that declares the function, how visible its frame is, and whether a caller can name that frame
/// (cross-assembly-device-code.md §3.7).
/// </summary>
/// <remarks>
/// <para>
/// [WHY] An assembly that exports its device code emits the state machines whatever its dialect, so a library that
/// built on main (CUDA, without kernels) must not stop building for them. A state machine is left out, and its record
/// says so (<c>DeviceExportRecord.ManagedStateMachine</c>, GSH0089 for a consumer on the C++ dialect), when it could not
/// compile: a containing type that is not partial, a type of its frame that the generated file cannot name (a file-local
/// type), or a suspending callee whose frame it cannot name.
/// </para>
/// <para>
/// The frame is as visible as the function (public or internal), narrowed to what the types it holds allow
/// (<see cref="GeneratedMemberAccessibility"/>): a frame that holds a private nested type of the function's class is a
/// private class nested in that class, which only that class and the types nested in it can name. Such a function also
/// gets entries as visible as the function (<c>__gsharp_number_frame_</c>, <c>__gsharp_enter_</c>), which create, number
/// and run the frame held as an <see cref="object"/>, so a caller of any class that C# lets call the function can run
/// it. The context of a kernel and
/// the frame of a device function ask <see cref="GetCallForm"/> how they hold a callee's frame; a kernel whose callee has
/// no frame at all (a file-local type) is GSH0115
/// (<see cref="GetCallFailure(IMethodSymbol, INamedTypeSymbol, Compilation)"/>), and a device function whose callee has
/// none has no state machine.
/// </para>
/// </remarks>
internal static class ManagedStateMachineAvailability
{
    private static readonly PerCompilationSymbolCache<IMethodSymbol, Accessibility> s_results = new();

    /// <summary>Whether the state machine of <paramref name="method"/> can be emitted (or, for a function of another assembly, was).</summary>
    public static bool CanEmit(IMethodSymbol method, Compilation compilation) =>
        GetFrameAccessibility(method, compilation) != Accessibility.NotApplicable;

    /// <summary>
    /// The accessibility of the frame and the state machine of <paramref name="method"/>: <c>public</c>,
    /// <c>internal</c> or <c>private</c>; <see cref="Accessibility.NotApplicable"/> when the state machine is left out.
    /// </summary>
    public static Accessibility GetFrameAccessibility(IMethodSymbol method, Compilation compilation) =>
        Get(method.OriginalDefinition, compilation, s_results.For(compilation),
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));

    /// <summary>
    /// How a managed state machine declared in <paramref name="within"/> holds and runs the frame of
    /// <paramref name="callee"/>: by its type when <paramref name="within"/> can name it, through the entries when the
    /// frame is private to a class that does not contain <paramref name="within"/>, not at all when the frame is not
    /// emitted.
    /// </summary>
    public static SuspendingCallForm GetCallForm(IMethodSymbol callee, INamedTypeSymbol within,
        Compilation compilation) =>
        ChooseCallForm(GetFrameAccessibility(callee, compilation), callee, within);

    /// <summary>
    /// The frames that a managed state machine declared in <paramref name="within"/> holds for the calls to
    /// <paramref name="callees"/>, in call order; <see langword="null"/> when one of the callees has no frame, which it
    /// returns in <paramref name="unavailable"/>.
    /// </summary>
    public static ImmutableArray<ManagedCallFrame>? PlanCallFrames(ImmutableArray<IMethodSymbol> callees,
        INamedTypeSymbol within, Compilation compilation, out IMethodSymbol? unavailable)
    {
        unavailable = null;
        var frames = ImmutableArray.CreateBuilder<ManagedCallFrame>(callees.Length);
        foreach (var callee in callees)
        {
            var form = GetCallForm(callee, within, compilation);
            if (form == SuspendingCallForm.Unavailable)
            {
                unavailable = callee;
                return null;
            }

            frames.Add(new ManagedCallFrame(callee, form == SuspendingCallForm.Entry));
        }

        return frames.MoveToImmutable();
    }

    /// <summary>
    /// Whether the state machine of <paramref name="method"/>, whose frame has <paramref name="frame"/> accessibility, has
    /// the entries through which the callers outside its class run it: the frame is private, and the function is not. The
    /// entries take the function's parameters and have its declared accessibility (public, internal, protected, protected
    /// internal or private protected), so every caller C# lets call the function can call them; a private function has no
    /// callers outside its class.
    /// </summary>
    public static bool HasEntries(IMethodSymbol method, Accessibility frame) =>
        frame == Accessibility.Private && method.DeclaredAccessibility != Accessibility.Private;

    /// <summary>
    /// Why a managed state machine declared in <paramref name="within"/> cannot hold the frame of
    /// <paramref name="callee"/>, as the message argument of GSH0115; <see langword="null"/> when it can (by the frame's
    /// type or through the entries), or when the frame is left out for a reason that another diagnostic reports (a type
    /// that is not partial, a function of another assembly whose state machine was not emitted).
    /// </summary>
    public static string? GetCallFailure(IMethodSymbol callee, INamedTypeSymbol within, Compilation compilation) =>
        GetCallFailure(callee.OriginalDefinition, within, compilation,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));

    private static string? GetCallFailure(IMethodSymbol callee, INamedTypeSymbol within, Compilation compilation,
        HashSet<IMethodSymbol> visited)
    {
        if (GetCallForm(callee, within, compilation) != SuspendingCallForm.Unavailable ||
            DeviceExportTable.IsOutside(callee, compilation) || !visited.Add(callee))
        {
            return null;
        }

        var container = callee.ContainingType;
        if (!AreContainingTypesPartial(callee) ||
            !TryGetFrameTypes(callee, compilation, out var body, out var frameTypes))
        {
            return null;
        }

        foreach (var type in frameTypes)
        {
            if (FindFileLocalType(type) is { } fileLocal)
            {
                return $"its frame would hold the file-local type '{fileLocal.Name}', which the generated code " +
                       "cannot name; declare the type without the 'file' modifier";
            }
        }

        foreach (var inner in SuspensionAnalysis.GetSuspendingCallees(body, compilation))
        {
            if (GetCallForm(inner, container, compilation) == SuspendingCallForm.Unavailable)
            {
                return GetCallFailure(inner.OriginalDefinition, container, compilation, visited) is { } reason
                    ? $"it calls '{inner.ContainingType.Name}.{inner.Name}', whose frame cannot be generated: " +
                      reason
                    : null;
            }
        }

        return null;
    }

    /// <summary>
    /// The frame types that a context or frame names for <paramref name="frames"/> (<see cref="PlanCallFrames"/>): those
    /// it holds by their type; a frame held through the entries is an <see cref="object"/>.
    /// </summary>
    public static GeneratedMemberAccessibility.NamedTypes GetCallFrames(ImmutableArray<ManagedCallFrame> frames,
        Compilation compilation)
    {
        var named = default(GeneratedMemberAccessibility.NamedTypes);
        foreach (var frame in frames)
        {
            if (!frame.ThroughEntry)
            {
                named = named.WithGenerated(frame.Callee.ContainingType,
                    GetFrameAccessibility(frame.Callee, compilation));
            }
        }

        return named;
    }

    /// <summary>Whether <paramref name="method"/> and every type that contains it are public.</summary>
    public static bool IsEffectivelyPublic(IMethodSymbol method)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            if (type.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the state machine needs an unsafe context: the function is declared unsafe (or within an unsafe type),
    /// or its signature or body uses pointers.
    /// </summary>
    /// <remarks>Such a function already needs <c>AllowUnsafeBlocks</c> in its own project.</remarks>
    public static bool NeedsUnsafe(IMethodSymbol method, SyntaxNode declaration)
    {
        if (method.Parameters
                .AsStackEnumerable()
                .Any(static p => p.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) ||
            method.ReturnType is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
        {
            return true;
        }

        for (var node = declaration; node != null; node = node.Parent)
        {
            if (node is MemberDeclarationSyntax member && member.Modifiers.Any(SyntaxKind.UnsafeKeyword))
            {
                return true;
            }
        }

        return declaration.DescendantNodes()
            .AsStackEnumerable()
            .Any(static node => node is PointerTypeSyntax or FunctionPointerTypeSyntax or UnsafeStatementSyntax
                or FixedStatementSyntax ||
                (node is PrefixUnaryExpressionSyntax prefix &&
                 prefix.Kind() is SyntaxKind.AddressOfExpression or SyntaxKind.PointerIndirectionExpression) ||
                node.IsKind(SyntaxKind.PointerMemberAccessExpression));
    }

    private static Accessibility Get(IMethodSymbol method, Compilation compilation,
        ConcurrentDictionary<IMethodSymbol, Accessibility> results, HashSet<IMethodSymbol> visiting)
    {
        if (results.TryGetValue(method, out var known))
        {
            return known;
        }

        // A cycle is not a device function G# emits (recursion is GSH0003); its result is the first function's
        if (!visiting.Add(method))
        {
            return Accessibility.Public;
        }

        var result = Evaluate(method, compilation, results, visiting);
        visiting.Remove(method);
        return results.GetOrAdd(method, result);
    }

    private static Accessibility Evaluate(IMethodSymbol method, Compilation compilation,
        ConcurrentDictionary<IMethodSymbol, Accessibility> results, HashSet<IMethodSymbol> visiting)
    {
        var widest = IsEffectivelyPublic(method) ? Accessibility.Public : Accessibility.Internal;

        // A function of another assembly: what its assembly emitted, as visible as the function
        if (DeviceExportTable.IsOutside(method, compilation))
        {
            return DeviceExportTable.TryFindFunction(method, compilation, out var imported) &&
                   imported.Record.Traits.ManagedStateMachine
                ? widest
                : Accessibility.NotApplicable;
        }

        if (!AreContainingTypesPartial(method) ||
            !TryGetFrameTypes(method, compilation, out var body, out var frameTypes) ||
            frameTypes
                .AsStackEnumerable()
                .Any(static type => FindFileLocalType(type) is not null))
        {
            return Accessibility.NotApplicable;
        }

        // The fields of a frame are internal
        var named = default(GeneratedMemberAccessibility.NamedTypes);
        foreach (var type in frameTypes)
        {
            named = named.WithInternalMember(type);
        }

        // The frame holds the frames of the calls that may suspend: by their types, or as objects run through the entries
        foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(body, compilation))
        {
            var calleeFrame = Get(callee.OriginalDefinition, compilation, results, visiting);
            switch (ChooseCallForm(calleeFrame, callee, method.ContainingType))
            {
                case SuspendingCallForm.Unavailable:
                    return Accessibility.NotApplicable;
                case SuspendingCallForm.Direct:
                    named = named.WithGenerated(callee.ContainingType, calleeFrame);
                    break;
            }
        }

        return GeneratedMemberAccessibility.Narrowest(widest, named.ChooseIn(method.ContainingType));
    }

    // The types the frame of <method> may hold: the return value, the parameters and the locals of its body
    private static bool TryGetFrameTypes(IMethodSymbol method, Compilation compilation,
        [NotNullWhen(true)] out SyntaxNode? body, out ImmutableArray<ITypeSymbol> types)
    {
        types = default;
        body = null;
        if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            declaration.GetBodyNode() is not { } bodyNode)
        {
            return false;
        }

        body = bodyNode;
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        builder.Add(method.ReturnType);
        method.Parameters
            .AsStackEnumerable()
            .Select(static p => p.Type)
            .AddTo(builder);
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        bodyNode.DescendantNodes()
            .AsStackEnumerable()
            .Select(model, static (node, m) =>
                node is VariableDeclaratorSyntax or SingleVariableDesignationSyntax
                    ? m.GetDeclaredSymbol(node) as ILocalSymbol
                    : null
            ).OfType<ILocalSymbol>()
            .Select(static local => local.Type)
            .AddTo(builder);
        types = builder.ToImmutable();
        return true;
    }

    // How code in <within> holds the frame of <callee>, whose accessibility is <frame>: by its type when it can name it,
    // else through the entries of a callee that has them
    private static SuspendingCallForm ChooseCallForm(Accessibility frame, IMethodSymbol callee,
        INamedTypeSymbol within) =>
        frame switch
        {
            Accessibility.NotApplicable => SuspendingCallForm.Unavailable,
            Accessibility.Private when !IsWithin(within, callee.ContainingType) => HasEntries(callee, frame)
                ? SuspendingCallForm.Entry
                : SuspendingCallForm.Unavailable,
            _ => SuspendingCallForm.Direct
        };

    private static bool IsWithin(INamedTypeSymbol type, INamedTypeSymbol container)
    {
        for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, container.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    // The first file-local type that <type> is or holds (an element, a pointee, a type argument, a containing type):
    // the generated file cannot name it
    private static INamedTypeSymbol? FindFileLocalType(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindFileLocalType(array.ElementType);
            case IPointerTypeSymbol pointer:
                return FindFileLocalType(pointer.PointedAtType);
            case INamedTypeSymbol named:
                return FindFileLocalType(named);
            default:
                return null;
        }
    }

    // <named>, a type that contains it, or a type argument of one of them
    private static INamedTypeSymbol? FindFileLocalType(INamedTypeSymbol named)
    {
        for (INamedTypeSymbol? current = named; current != null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return current;
            }

            if (current.TypeArguments
                .AsStackEnumerable()
                .Select(static argument => FindFileLocalType(argument))
                .TryFirst(static found => found is not null, out var fileLocal))
            {
                return fileLocal;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether every type that contains <paramref name="method"/> is declared <c>partial</c>: the state machines and the
    /// managed versions are written into a partial declaration of each one.
    /// </summary>
    internal static bool AreContainingTypesPartial(IMethodSymbol method)
    {
        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            if (!type.DeclaringSyntaxReferences
                    .AsStackEnumerable()
                    .Any(static reference => reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                                             declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                return false;
            }
        }

        return true;
    }
}
