namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The <c>[UnsafeAccessor]</c> stubs through which one managed body (a kernel or device function state machine, or a
/// managed version) reaches what C# does not let it name (#711): the target of an operator macro that is private to
/// another type, or internal to another assembly, and the backing field of an auto-property that an operator macro
/// passes by reference (the device stores the property as a field, and C# has no reference to it).
/// </summary>
/// <remarks>
/// <para>
/// [WHY] An operator macro names its target by attribute, so the target need not be accessible where the operator is
/// used; the native code calls it by its C++ name, which has no accessibility. The managed expansion is C#, so it calls
/// the target through a stub that the runtime binds to the method by name, as <c>UnsafeAccessorKind.StaticMethod</c>
/// does (.NET 8; the declaring type named by <c>[UnsafeAccessorType]</c>, which also reaches a static or inaccessible
/// type, .NET 10; Kassyi.GSharp.Core requires .NET 10).
/// </para>
/// <para>
/// Each stub is a private nested class of the type that holds the body, generic over the type parameters of the
/// target's declaring types (the runtime matches them by position, <c>!0</c>, <c>!1</c>, ...), with the extern accessor
/// and a method <c>Invoke</c> that passes the arguments on:
/// <c>private static class __gsharp_accessor_&lt;body&gt;_&lt;n&gt;&lt;T&gt; { [UnsafeAccessor(...)] private static extern R Target(object? _, ...); internal static R Invoke(...) =&gt; Target(null, ...); }</c>.
/// A backing field stub takes the struct by <c>in</c> and returns its field by reference:
/// <c>[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "&lt;P&gt;k__BackingField")] private static extern ref F Target(ref S _); internal static ref F Invoke(in S p0) =&gt; ref Target(ref Unsafe.AsRef(in p0));</c>.
/// </para>
/// </remarks>
internal sealed class ManagedAccessorSet(IMethodSymbol caller, Compilation compilation)
{
    private const string AttributeNamespace = "global::System.Runtime.CompilerServices.";

    private readonly Dictionary<ISymbol, string> _holders = new(SymbolEqualityComparer.Default);
    private readonly ImmutableArray<(ISymbol Target, string TargetName, string Holder)>.Builder _stubs =
        ImmutableArray.CreateBuilder<(ISymbol, string, string)>();

    /// <summary>Whether the body may name <paramref name="callee"/> directly.</summary>
    public bool IsAccessible(IMethodSymbol callee) =>
        caller.ContainingType is not { } within || compilation.IsSymbolAccessibleWithin(callee, within);

    /// <summary>
    /// The callee of a call of <paramref name="callee"/> through its stub, which calls the method named
    /// <paramref name="targetName"/> (the function itself or its managed version):
    /// <c>global::&lt;body type&gt;.__gsharp_accessor_&lt;body&gt;_&lt;n&gt;[&lt;type arguments&gt;].Invoke</c>.
    /// </summary>
    public ExpressionSyntax Spell(IMethodSymbol callee, string targetName) =>
        SpellInvoke(callee.OriginalDefinition, callee.ContainingType, targetName);

    /// <summary>
    /// A reference to the backing field of the auto-property <paramref name="property"/> of the struct that
    /// <paramref name="receiver"/> (storage) names:
    /// <c>global::&lt;body type&gt;.__gsharp_accessor_&lt;body&gt;_&lt;n&gt;[&lt;type arguments&gt;].Invoke(in &lt;receiver&gt;)</c>,
    /// which returns it by reference.
    /// </summary>
    public ExpressionSyntax SpellBackingField(IPropertySymbol property, ExpressionSyntax receiver)
    {
        var definition = property.OriginalDefinition;
        var field = definition.ContainingType.GetMembers()
            .AsStackEnumerable()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(definition, static (f, p) => f.AssociatedSymbol.IsEqualTo(p))
            ?? throw new InvalidOperationException($"The auto-property '{property.Name}' has no backing field.");
        var invoke = SpellInvoke(definition, property.ContainingType, field.Name);

        // <invoke>(in <receiver>)
        return SyntaxFactory.InvocationExpression(
            invoke,
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(receiver).WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.InKeyword))
                )
            )
        );
    }

    // global::<body type>.<holder>[<type arguments>].Invoke, with the stub of <definition> handed out once
    private ExpressionSyntax SpellInvoke(ISymbol definition, INamedTypeSymbol? declaringType, string targetName)
    {
        if (!_holders.TryGetValue(definition, out var holder))
        {
            holder = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.ManagedVersion.AccessorPrefix, caller) +
                     "_" + _stubs.Count.ToString(CultureInfo.InvariantCulture);
            _holders.Add(definition, holder);
            _stubs.Add((definition, targetName, holder));
        }

        using var writer = new SourceWriter();
        writer.AppendFullyQualifiedCsharpName(caller.ContainingType);
        writer.Append($".{holder}");
        var typeArguments = GetTypeArguments(declaringType);
        if (typeArguments.Length > 0)
        {
            writer.Append("<");
            for (var i = 0; i < typeArguments.Length; i++)
            {
                writer.AppendIf(i > 0, ", ");
                writer.AppendCsharpTypeName(typeArguments[i]);
            }

            writer.Append(">");
        }

        writer.Append(".Invoke");
        return SyntaxFactory.ParseExpression(writer.ToString());
    }

    /// <summary>
    /// Writes the stubs that <see cref="Spell"/> and <see cref="SpellBackingField"/> handed out, nothing when they handed
    /// out none.
    /// </summary>
    public void WriteTo(SourceWriter writer)
    {
        foreach (var (target, targetName, holder) in _stubs)
        {
            var typeParameters = GetTypeParameters(target.ContainingType);
            var isUnsafe = target is IMethodSymbol method && NeedsUnsafe(method);

            // private static [unsafe] class <holder>[<T...>] [where ...]
            writer.AppendLine(ManagedStateMachineEmitter.EditorBrowsableNever);
            writer.Append("private static ");
            writer.AppendIf(isUnsafe, "unsafe ");
            writer.Append($"class {holder}");
            AppendTypeParameterList(writer, typeParameters);
            writer.AppendLine();
            AppendConstraints(writer, typeParameters);
            using (writer.Scope())
            {
                if (target is IMethodSymbol callee)
                {
                    WriteMethodStub(writer, callee, targetName, typeParameters.Length);
                }
                else if (target is IPropertySymbol property)
                {
                    WriteFieldStub(writer, property, targetName);
                }
            }

            writer.AppendLine();
        }
    }

    private static bool NeedsUnsafe(IMethodSymbol target) =>
        target.Parameters
            .AsStackEnumerable()
            .Any(static p => p.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) ||
        target.ReturnType is IPointerTypeSymbol or IFunctionPointerTypeSymbol;

    // [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "<target>")]
    // private static extern <return> Target(<declaring type marker>, <parameters>);
    // internal static <return> Invoke(<parameters>) => Target(default, <arguments>);
    private static void WriteMethodStub(SourceWriter writer, IMethodSymbol target, string targetName,
        int typeParameterCount)
    {
        writer.AppendLine(
            $"[{AttributeNamespace}UnsafeAccessor({AttributeNamespace}UnsafeAccessorKind.StaticMethod, Name = \"{targetName}\")]");
        writer.Append("private static extern ");
        AppendReturnType(writer, target);
        writer.Append(" Target");
        using (writer.Parentheses())
        {
            AppendDeclaringTypeMarker(writer, target.ContainingType, typeParameterCount);
            AppendParameters(writer, target, withLeadingComma: true);
        }

        writer.AppendLine(";");
        writer.Append("internal static ");
        AppendReturnType(writer, target);
        writer.Append(" Invoke");
        using (writer.Parentheses())
        {
            AppendParameters(writer, target, withLeadingComma: false);
        }

        writer.Append(" => Target");
        using (writer.Parentheses())
        {
            writer.Append("default");
            for (var i = 0; i < target.Parameters.Length; i++)
            {
                writer.Append(
                    $", {ManagedRefKinds.ArgumentModifier(target.Parameters[i].RefKind)}p{i.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        writer.AppendLine(";");
    }

    // [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<backing field>")]
    // private static extern ref <property type> Target(ref <struct> _);
    // internal static ref <property type> Invoke(in <struct> p0) => ref Target(ref Unsafe.AsRef(in p0));
    // [WHY] The struct comes by 'in', so that a readonly variable (an in parameter, a ref readonly local) passes as well;
    // the caller reads the field through the reference, or writes it when it passes the result by 'ref', as the device
    // writes the field
    private static void WriteFieldStub(SourceWriter writer, IPropertySymbol property, string fieldName)
    {
        writer.AppendLine(
            $"[{AttributeNamespace}UnsafeAccessor({AttributeNamespace}UnsafeAccessorKind.Field, Name = \"{fieldName}\")]");
        writer.Append("private static extern ref ");
        writer.AppendCsharpTypeName(property.Type);
        writer.Append(" Target(ref ");
        writer.AppendCsharpTypeName(property.ContainingType);
        writer.AppendLine(" _);");
        writer.Append("internal static ref ");
        writer.AppendCsharpTypeName(property.Type);
        writer.Append(" Invoke(in ");
        writer.AppendCsharpTypeName(property.ContainingType);
        writer.AppendLine($" p0) => ref Target(ref {AttributeNamespace}Unsafe.AsRef(in p0));");
    }

    // The first parameter of a static accessor names the type to look the method up in; its value is not used. A class
    // (static ones and inaccessible ones too) by name: [UnsafeAccessorType("<declaring type>, <assembly>")] object? _.
    // A struct by its type, as UnsafeAccessorType does not take value types: <declaring type> _
    private static void AppendDeclaringTypeMarker(SourceWriter writer, INamedTypeSymbol declaringType,
        int typeParameterCount)
    {
        if (declaringType.IsValueType)
        {
            writer.AppendCsharpTypeName(declaringType.OriginalDefinition);
            writer.Append(" _");
            return;
        }

        var name = GetRuntimeTypeName(declaringType, typeParameterCount);
        writer.Append($"[{AttributeNamespace}UnsafeAccessorType(\"{name}\")] object? _");
    }

    /// <summary>
    /// Why the managed path cannot call <paramref name="target"/> from code in <paramref name="within"/>, or
    /// <see langword="null"/> when it can: the target and the struct that declares it are both inaccessible there.
    /// </summary>
    /// <remarks>
    /// [WHY] C# cannot name the struct, and the runtime's <c>[UnsafeAccessorType]</c>, which names an inaccessible type by
    /// string, does not take value types; reaching it would take reflection, which G# does not emit. A class (static or
    /// not) is named by string, and an accessible struct by its type, so any other target is reached.
    /// </remarks>
    public static string? GetUnreachableTargetFailure(IMethodSymbol target, INamedTypeSymbol within,
        Compilation compilation)
    {
        if (compilation.IsSymbolAccessibleWithin(target, within) ||
            target.ContainingType is not { IsValueType: true } declaringType ||
            compilation.IsSymbolAccessibleWithin(declaringType, within))
        {
            return null;
        }

        return
            $"Operator macro target '{declaringType.Name}.{target.Name}' is not accessible here and is declared in struct '{declaringType.Name}', which is not accessible either: the managed CPU path calls such a target through [UnsafeAccessor], which can name an inaccessible class but not an inaccessible struct; make '{declaringType.Name}' or the target accessible here";
    }

    private static void AppendReturnType(SourceWriter writer, IMethodSymbol target)
    {
        if (target.ReturnsVoid)
        {
            writer.Append("void");
            return;
        }

        writer.AppendCsharpTypeName(target.ReturnType);
    }

    // [, ]<modifier> <type> p<i>, ...
    private static void AppendParameters(SourceWriter writer, IMethodSymbol target, bool withLeadingComma)
    {
        for (var i = 0; i < target.Parameters.Length; i++)
        {
            var parameter = target.Parameters[i];
            writer.AppendIf(withLeadingComma || i > 0, ", ");
            writer.Append(ManagedRefKinds.ParameterModifier(parameter.RefKind));
            writer.AppendCsharpTypeName(parameter.Type);
            writer.Append($" p{i.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    // <T, U>: the declaring types' own names, which their constraints refer to
    private static void AppendTypeParameterList(SourceWriter writer, ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        if (typeParameters.Length == 0)
        {
            return;
        }

        writer.Append("<");
        for (var i = 0; i < typeParameters.Length; i++)
        {
            writer.AppendIf(i > 0, ", ");
            writer.Append(typeParameters[i].Name.ToCSharpIdentifier());
        }

        writer.Append(">");
    }

    // where T : unmanaged, ...: the runtime requires the constraints of the declaring types' parameters
    private static void AppendConstraints(SourceWriter writer, ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        foreach (var parameter in typeParameters)
        {
            var kind = parameter.HasUnmanagedTypeConstraint ? "unmanaged"
                : parameter.HasValueTypeConstraint ? "struct"
                : parameter.HasReferenceTypeConstraint ? "class"
                : parameter.HasNotNullConstraint ? "notnull"
                : null;
            var hasConstructor = parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint;
            if (kind is null && parameter.ConstraintTypes.IsEmpty && !hasConstructor)
            {
                continue;
            }

            writer.Append($"where {parameter.Name.ToCSharpIdentifier()} : ");
            var first = true;
            if (kind is not null)
            {
                writer.Append(kind);
                first = false;
            }

            foreach (var constraint in parameter.ConstraintTypes)
            {
                writer.AppendIf(!first, ", ");
                writer.AppendCsharpTypeName(constraint);
                first = false;
            }

            writer.AppendIf(hasConstructor, first ? "new()" : ", new()");
            writer.AppendLine();
        }
    }

    // N.Outer`1+Inner[[!0]], Assembly: the type name that UnsafeAccessorType resolves, with the type parameters of the
    // stub's class standing for those of the declaring types
    private static string GetRuntimeTypeName(INamedTypeSymbol type, int typeParameterCount)
    {
        var name = new StringBuilder();
        AppendNestedName(name, type);
        if (typeParameterCount > 0)
        {
            name.Append('[');
            for (var i = 0; i < typeParameterCount; i++)
            {
                name.Append(i > 0 ? ",[!" : "[!").Append(i.ToString(CultureInfo.InvariantCulture)).Append(']');
            }

            name.Append(']');
        }

        return name.Append(", ").Append(type.ContainingAssembly.Identity.Name).ToString();
    }

    private static void AppendNestedName(StringBuilder name, INamedTypeSymbol type)
    {
        if (type.ContainingType is { } outer)
        {
            AppendNestedName(name, outer);
            name.Append('+');
        }
        else if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            name.Append(ns.GetFullNamespaceName()).Append('.');
        }

        name.Append(type.MetadataName);
    }

    // The type parameters of <type> and the types that contain it, the outermost first
    private static ImmutableArray<ITypeParameterSymbol> GetTypeParameters(INamedTypeSymbol? type)
    {
        var parameters = ImmutableArray.CreateBuilder<ITypeParameterSymbol>();
        AddTypeParameters(type?.OriginalDefinition, parameters);
        return parameters.ToImmutable();
    }

    private static void AddTypeParameters(INamedTypeSymbol? type, ImmutableArray<ITypeParameterSymbol>.Builder parameters)
    {
        if (type is null)
        {
            return;
        }

        AddTypeParameters(type.ContainingType, parameters);
        type.TypeParameters
            .AsStackEnumerable()
            .AddTo(parameters);
    }

    // The type arguments of <type> and the types that contain it, the outermost first
    private static ImmutableArray<ITypeSymbol> GetTypeArguments(INamedTypeSymbol? type)
    {
        var arguments = ImmutableArray.CreateBuilder<ITypeSymbol>();
        AddTypeArguments(type, arguments);
        return arguments.ToImmutable();
    }

    private static void AddTypeArguments(INamedTypeSymbol? type, ImmutableArray<ITypeSymbol>.Builder arguments)
    {
        if (type is null)
        {
            return;
        }

        AddTypeArguments(type.ContainingType, arguments);
        type.TypeArguments
            .AsStackEnumerable()
            .AddTo(arguments);
    }
}
