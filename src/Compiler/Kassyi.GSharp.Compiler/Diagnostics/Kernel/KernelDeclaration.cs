namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Decides where <c>[GpuKernel]</c> may be declared (GSH0111) and which kernel parameters have no device representation
/// (GSH0112). <see cref="KernelDeclarationAnalyzer"/> reports both, and the generator leaves out every kernel that
/// <see cref="IsLaunchable"/> rejects, so that the generated C# never has a declaration error that would hide the
/// analyzer diagnostics of the project.
/// </summary>
/// <remarks>
/// A kernel runs only through the launcher that the generator adds to the kernel's class. The launcher goes into a
/// <c>partial class</c> without type parameters, so the kernel must be an ordinary static, non-generic method of a
/// non-generic class. A declaration that C# or the runtime calls on its own (an operator, the entry point, a method
/// that implements an interface member, an extension method that C# calls by its name) would also run its body as
/// plain C# on the host, so it is rejected even where a launcher could be written.
/// </remarks>
internal static class KernelDeclaration
{
    /// <summary>Where <paramref name="kernel"/> is declared, and whether that place can have a launcher.</summary>
    public static KernelPlacement GetPlacement(IMethodSymbol kernel, Compilation compilation,
        CancellationToken cancellationToken)
    {
        var kind = GetKindPlacement(kernel);
        if (kind != KernelPlacement.Allowed)
        {
            return kind;
        }

        if (!kernel.IsStatic)
        {
            return KernelPlacement.InstanceMethod;
        }

        if (kernel.IsGenericMethod)
        {
            return KernelPlacement.GenericMethod;
        }

        for (var type = kernel.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsGenericType)
            {
                return KernelPlacement.MemberOfGenericType;
            }
        }

        return GetCallerPlacement(kernel, compilation, cancellationToken);
    }

    /// <summary>
    /// Whether the generator emits <paramref name="kernel"/>: its placement is <see cref="KernelPlacement.Allowed"/>,
    /// every parameter has a device representation, and its return type can be the element of the launcher's result
    /// array.
    /// </summary>
    public static bool IsLaunchable(IMethodSymbol kernel, Compilation compilation,
        CancellationToken cancellationToken) =>
        GetPlacement(kernel, compilation, cancellationToken) == KernelPlacement.Allowed &&
        !kernel.Parameters
            .AsStackEnumerable()
            .Any(static parameter => FindManagedReference(parameter.Type) is not null) &&
        HasLaunchableReturnType(kernel);

    /// <summary>
    /// The reference type that makes a kernel parameter of <paramref name="type"/> have no device representation: the
    /// type itself when it is a reference type other than an array (a class, <see cref="string"/>, an interface, a
    /// delegate, <see cref="object"/>, <c>dynamic</c>), or the innermost element type of an array or span when that is
    /// one; otherwise <see langword="null"/>. GSH0112 reports it, except <c>dynamic</c>, which GSH0003 reports.
    /// </summary>
    public static ITypeSymbol? FindManagedReference(ITypeSymbol type)
    {
        var element = type;
        while (element.TryGetArrayLikeElementType(out var inner))
        {
            element = inner;
        }

        return element is { IsReferenceType: true } and not (ITypeParameterSymbol or IErrorTypeSymbol) ? element : null;
    }

    /// <summary>The text of GSH0111 that names <paramref name="placement"/>.</summary>
    public static string Describe(KernelPlacement placement, IMethodSymbol kernel) => placement switch
    {
        KernelPlacement.Lambda => "a lambda",
        KernelPlacement.LocalFunction => "a local function",
        KernelPlacement.Operator => "an operator",
        KernelPlacement.ConversionOperator => "a conversion operator",
        KernelPlacement.Accessor => "an accessor",
        KernelPlacement.StructMember => "a member of a struct",
        KernelPlacement.InterfaceMember => "a member of an interface",
        KernelPlacement.ExtensionBlockMember => "a member of an extension block",
        KernelPlacement.InstanceMethod => "an instance method",
        KernelPlacement.GenericMethod => "a generic method",
        KernelPlacement.MemberOfGenericType => "a member of a generic type",
        KernelPlacement.EntryPoint => "the entry point",
        KernelPlacement.ModuleInitializer => "a [ModuleInitializer] method",
        KernelPlacement.UnmanagedCallersOnly => "an [UnmanagedCallersOnly] method",
        KernelPlacement.InterfaceImplementation => "a method that implements an interface member",
        KernelPlacement.PatternExtension => $"an extension method that C# calls by its name '{kernel.Name}'",
        KernelPlacement.CollectionBuilderMethod => "a method that [CollectionBuilder] names",
        _ => throw new ArgumentOutOfRangeException(nameof(placement), placement, null)
    };

    // The kind of method, and the type it is declared in
    private static KernelPlacement GetKindPlacement(IMethodSymbol kernel) => kernel.MethodKind switch
    {
        MethodKind.AnonymousFunction => KernelPlacement.Lambda,
        MethodKind.LocalFunction => KernelPlacement.LocalFunction,
        MethodKind.UserDefinedOperator => KernelPlacement.Operator,
        MethodKind.Conversion => KernelPlacement.ConversionOperator,
        MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove
            or MethodKind.EventRaise => KernelPlacement.Accessor,
        MethodKind.ExplicitInterfaceImplementation => KernelPlacement.InterfaceImplementation,
        _ => GetTypePlacement(kernel.ContainingType)
    };

    // The launcher is added to a partial class, which cannot extend another kind of type
    private static KernelPlacement GetTypePlacement(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class => KernelPlacement.Allowed,
        TypeKind.Struct => KernelPlacement.StructMember,
        TypeKind.Interface => KernelPlacement.InterfaceMember,
        // [WHY] Roslyn 4.8, which the compiler builds against, has no TypeKind for a C# 14 extension block
        _ => KernelPlacement.ExtensionBlockMember
    };

    // A static method of a class that the runtime, native code or C# itself calls without a launcher
    private static KernelPlacement GetCallerPlacement(IMethodSymbol kernel, Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (kernel.Name == WellKnownMemberNames.EntryPointMethodName &&
            kernel.IsEqualTo(compilation.GetEntryPoint(cancellationToken)))
        {
            return KernelPlacement.EntryPoint;
        }

        if (kernel.HasAttribute(KnownAttributes.ModuleInitializer))
        {
            return KernelPlacement.ModuleInitializer;
        }

        if (kernel.HasAttribute(KnownAttributes.UnmanagedCallersOnly))
        {
            return KernelPlacement.UnmanagedCallersOnly;
        }

        if (ImplementsInterfaceMember(kernel))
        {
            return KernelPlacement.InterfaceImplementation;
        }

        if (kernel.IsExtensionMethod && IsPatternMethodName(kernel.Name))
        {
            return KernelPlacement.PatternExtension;
        }

        return IsNamedByCollectionBuilder(kernel) ? KernelPlacement.CollectionBuilderMethod : KernelPlacement.Allowed;
    }

    // A static abstract or virtual interface member that the kernel implements is called as T.Member() in generic code
    private static bool ImplementsInterfaceMember(IMethodSymbol kernel) =>
        kernel.ContainingType.AllInterfaces
            .AsStackEnumerable()
            .Any(kernel, static (contract, method) => contract.GetMembers(method.Name)
                .AsStackEnumerable()
                .Any(method, static (member, implementation) => implementation.ContainingType
                    .FindImplementationForInterfaceMember(member)
                    .IsEqualTo(implementation)
                )
            );

    // The names of the extension methods that C# looks up to expand foreach, await foreach, a deconstruction, await,
    // fixed, a collection initializer and using
    private static bool IsPatternMethodName(string name) => name is
        WellKnownMemberNames.GetEnumeratorMethodName or WellKnownMemberNames.GetAsyncEnumeratorMethodName or
        WellKnownMemberNames.DeconstructMethodName or WellKnownMemberNames.GetAwaiter or
        WellKnownMemberNames.CollectionInitializerAddMethodName or WellKnownMemberNames.DisposeMethodName or
        WellKnownMemberNames.DisposeAsyncMethodName or "GetPinnableReference";

    // [CollectionBuilder(typeof(Builder), "Create")] on the collection type, which is what Create returns
    private static bool IsNamedByCollectionBuilder(IMethodSymbol kernel) =>
        kernel.ReturnType.OriginalDefinition.GetAttribute(KnownAttributes.CollectionBuilder) is
        {
            ConstructorArguments: { Length: 2 } arguments
        } &&
        arguments[0].Value is INamedTypeSymbol builder &&
        arguments[1].Value is string methodName &&
        methodName == kernel.Name &&
        builder.IsEqualTo(kernel.ContainingType.OriginalDefinition);

    // The launcher returns the results of all threads in an array of the return type
    private static bool HasLaunchableReturnType(IMethodSymbol kernel) =>
        kernel.ReturnsVoid ||
        IteratorTypeResolver.IsDeviceIteratorMethod(kernel) ||
        kernel.ReturnType is { IsUnmanagedType: true, IsRefLikeType: false };
}
