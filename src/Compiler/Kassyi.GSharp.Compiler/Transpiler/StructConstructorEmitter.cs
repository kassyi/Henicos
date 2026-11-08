using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits the user-defined constructors of a struct (a constructor with a body, or the primary constructor) as
/// value-returning device functions (<c>__gsharp_ctor_...</c>) after the struct definition.
/// </summary>
/// <remarks>
/// [WHY] Defining a C++ constructor inside the struct makes it non-aggregate, which breaks CUDA __shared__ and brace
/// initialization. Instead, each user-defined constructor is emitted right after the struct as an inline factory
/// function that default-initializes __gsharp_self{}, runs the body, and returns __gsharp_self (matching Roslyn's
/// LocalRewriter_ObjectCreationExpression.MakeObjectCreationExpressionForStruct).
/// </remarks>
internal static class StructConstructorEmitter
{
    /// <summary>
    /// Whether <paramref name="constructor"/> is a user-defined struct constructor emitted as a value-returning device
    /// function (<c>__gsharp_ctor_...</c>) rather than aggregate initialization: a constructor with a body, or the
    /// primary constructor (#495). A constructor of another assembly (<see cref="DeviceExportTable.IsOutside"/>) is one
    /// when its assembly recorded it with its struct (<see cref="DeviceExportTable"/>), whose definition the consumer
    /// imports.
    /// </summary>
    public static bool IsEmittedStructConstructor(IMethodSymbol? constructor, Compilation compilation) =>
        constructor is { MethodKind: MethodKind.Constructor, IsImplicitlyDeclared: false, IsStatic: false } &&
        constructor.ContainingType is { } containingType &&
        GlobalCppNames.IsEmittedUserType(containingType) &&
        (TryGetConstructorDeclaration(constructor, out _, out _) ||
         (DeviceExportTable.IsOutside(constructor, compilation) && DeviceExportTable.TryFind(constructor, out _)));

    // The declaration of a constructor that has a body (`S(...) { }` or `=> expr`), or of a primary constructor (the
    // type declaration, `struct S(int a)`)
    private static bool TryGetConstructorDeclaration(IMethodSymbol constructor,
        [NotNullWhen(true)] out SyntaxNode? declaration, out ConstructorDeclarationSyntax? explicitDeclaration)
    {
        if (constructor.TryGetDeclaringSyntax<ConstructorDeclarationSyntax>(out explicitDeclaration))
        {
            declaration = explicitDeclaration;
            return explicitDeclaration.Body != null || explicitDeclaration.ExpressionBody != null;
        }

        if (constructor.TryGetPrimaryConstructorDeclaration(out var typeDeclaration))
        {
            declaration = typeDeclaration;
            return true;
        }

        declaration = null;
        return false;
    }

    /// <summary>
    /// Writes the factory function of each emitted constructor of <paramref name="structSym"/> declared in this
    /// compilation, a constructor chained to by <c>: this(...)</c> before the constructor that chains to it.
    /// </summary>
    public static void WriteStructConstructors(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        var ordered = ImmutableArray.CreateBuilder<(IMethodSymbol Constructor, SyntaxNode Declaration)>();
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var constructor in structSym.InstanceConstructors)
        {
            VisitConstructor(constructor);
        }

        foreach (var (constructor, declaration) in ordered)
        {
            WriteStructConstructor(structSym, constructor, declaration, ref context);
        }

        void VisitConstructor(IMethodSymbol constructor)
        {
            if (!visited.Add(constructor) ||
                !IsEmittedStructConstructor(constructor, compilation) ||
                !TryGetConstructorDeclaration(constructor, out var declaration, out var explicitDeclaration) ||
                !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                return;
            }

            if (explicitDeclaration?.Initializer is { } initializer)
            {
                var model = compilation.GetSemanticModel(declaration.SyntaxTree);
                if (ResolveChainedConstructor(structSym, model.GetSymbol(initializer) as IMethodSymbol) is { } target)
                {
                    VisitConstructor(target);
                }
            }

            ordered.Add((constructor, declaration));
        }
    }

    private static IMethodSymbol? ResolveChainedConstructor(INamedTypeSymbol structSym, IMethodSymbol? target)
    {
        if (target == null)
        {
            return null;
        }

        foreach (var candidate in structSym.InstanceConstructors)
        {
            if (candidate.OriginalDefinition.IsEqualTo(target.OriginalDefinition))
            {
                return candidate;
            }
        }

        return target;
    }

    private static bool TryGetMemberInitializer(ISymbol memberSymbol, Compilation compilation,
        [NotNullWhen(true)] out EqualsValueClauseSyntax? initializer)
    {
        if (memberSymbol.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) &&
            declarator.Initializer is { } fieldInit &&
            compilation.ContainsSyntaxTree(declarator.SyntaxTree))
        {
            initializer = fieldInit;
            return true;
        }

        if (memberSymbol.TryGetDeclaringSyntax<PropertyDeclarationSyntax>(out var propertyDecl) &&
            propertyDecl.Initializer is { } propInit &&
            compilation.ContainsSyntaxTree(propertyDecl.SyntaxTree))
        {
            initializer = propInit;
            return true;
        }

        initializer = null;
        return false;
    }

    // __device__ inline <StructType> __gsharp_ctor_<Name>(<params>) {
    // <StructType> __gsharp_self{}; (or = __gsharp_ctor_...(...) when chaining : this(...))
    // <member initializers when not chaining>
    // <body>
    // return __gsharp_self;
    // }
    // A primary constructor (declared by the type declaration) has no body and no initializer; its member
    // initializers also store the captured parameters and, in a record struct, the positional properties.
    private static void WriteStructConstructor(INamedTypeSymbol structSym, IMethodSymbol constructor,
        SyntaxNode constructorDeclaration, ref TranspileContext context)
    {
        if (constructorDeclaration is TypeDeclarationSyntax { ParameterList: { } primaryParameters })
        {
            WriteStructConstructorCore(structSym, constructor, primaryParameters, null, ref context);
        }
        else if (constructorDeclaration is ConstructorDeclarationSyntax declaration)
        {
            WriteStructConstructorCore(structSym, constructor, declaration.ParameterList, declaration, ref context);
        }
    }

    private static void WriteStructConstructorCore(INamedTypeSymbol structSym, IMethodSymbol constructor,
        ParameterListSyntax parameterList, ConstructorDeclarationSyntax? declaration, ref TranspileContext context)
    {
        var saved = SemanticModelSwitch.Enter(ref context, parameterList.SyntaxTree);
        var previousCtorType = context.Emitter.StructConstructorType;
        var previousCallableDepth = context.Emitter.CallableDepth;
        context.Emitter.StructConstructorType = structSym;
        context.Emitter.CallableDepth = 1;

        try
        {
            context.Emitter.Writer.Append("__device__ inline ");
            context.Emitter.Dialect.AppendType(ref context, structSym);
            context.Emitter.Writer.Append(' ').Append(GlobalCppNames.GetConstructorName(constructor));
            context.Visitor.Visit(parameterList);
            context.Emitter.Writer.Append(' ');

            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Dialect.AppendType(ref context, structSym);
                WriteSelfInitialization(structSym, constructor, declaration, saved.Model, ref context);
                WriteConstructorBody(declaration, ref context);
                context.Emitter.Writer.AppendLine($"return {GSharpInternalNames.Lowering.ConstructorSelf};");
            }
        }
        finally
        {
            context.Emitter.StructConstructorType = previousCtorType;
            context.Emitter.CallableDepth = previousCallableDepth;
            saved.Restore(ref context);
        }
    }

    // After the struct type: ` __gsharp_self = <chained constructor call>;` for `: this(...)`, otherwise
    // ` __gsharp_self{};` followed by the member initializers
    private static void WriteSelfInitialization(INamedTypeSymbol structSym, IMethodSymbol constructor,
        ConstructorDeclarationSyntax? declaration, SemanticModel model, ref TranspileContext context)
    {
        if (declaration?.Initializer is not { } initializer)
        {
            context.Emitter.Writer.AppendLine($" {GSharpInternalNames.Lowering.ConstructorSelf}{{}};");
            WriteStructMemberInitializers(structSym, declaration == null ? constructor : null, ref context);
            return;
        }

        var target = ResolveChainedConstructor(structSym, model.GetSymbol(initializer) as IMethodSymbol);
        if (target != null && IsEmittedStructConstructor(target, model.Compilation))
        {
            context.Emitter.Writer.Append($" {GSharpInternalNames.Lowering.ConstructorSelf} = ");
            ObjectCreationHelper.TranspileConstructorCall(structSym, target, initializer.ArgumentList, ref context);
            context.Emitter.Writer.AppendLine(";");
            return;
        }

        if (target is not { Parameters.Length: 0 })
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, initializer, initializer.Kind().ToString());
        }

        context.Emitter.Writer.AppendLine($" {GSharpInternalNames.Lowering.ConstructorSelf}{{}};");
    }

    // The block body, or `<expression>;` for an expression body; nothing for a primary constructor
    private static void WriteConstructorBody(ConstructorDeclarationSyntax? declaration, ref TranspileContext context)
    {
        if (declaration?.Body != null)
        {
            context.Visitor.VisitBlock(declaration.Body);
        }
        else if (declaration?.ExpressionBody != null)
        {
            context.Visitor.Visit(declaration.ExpressionBody.Expression);
            context.Emitter.Writer.AppendLine(";");
        }
    }

    // The member initializers in declaration order. The primary constructor first stores the parameters that members
    // capture (__gsharp_self.__gsharp_mb_a = a;), which Roslyn lists before the declared members, and initializes the
    // positional properties of a record struct from their parameters at the position of the parameter list.
    private static void WriteStructMemberInitializers(INamedTypeSymbol structSym, IMethodSymbol? primaryConstructor,
        ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        foreach (var member in StructAbiMembers.Get(structSym))
        {
            if (primaryConstructor != null && TryGetPrimaryParameter(member, primaryConstructor, out var parameter))
            {
                context.Emitter.Writer.Append($"{GSharpInternalNames.Lowering.ConstructorSelf}.")
                    .Append(StructMemberEmitter.GetMemberName(member.Symbol, ref context))
                    .Append(" = ");
                CppNameResolver.AppendSymbolName(parameter, ref context);
                context.Emitter.Writer.AppendLine(";");
                continue;
            }

            if (TryGetMemberInitializer(member.Symbol, compilation, out var initializer))
            {
                WriteMemberInitializer(member, initializer, ref context);
            }
        }
    }

    // __gsharp_self.<member> = <initializer>; bound with a model of the initializer's own tree
    private static void WriteMemberInitializer(StructAbiMember member, EqualsValueClauseSyntax initializer,
        ref TranspileContext context)
    {
        var saved = SemanticModelSwitch.Enter(ref context, initializer.SyntaxTree);
        try
        {
            context.Emitter.Writer.Append($"{GSharpInternalNames.Lowering.ConstructorSelf}.")
                .Append(StructMemberEmitter.GetMemberName(member.Symbol, ref context))
                .Append(" = ");
            context.Visitor.Visit(initializer.Value);
            context.Emitter.Writer.AppendLine(";");
        }
        finally
        {
            saved.Restore(ref context);
        }
    }

    // The parameter of the primary constructor that a member is initialized from without an initializer of its own:
    // the hidden field of a captured parameter, or a positional property of a record struct (declared by the parameter)
    private static bool TryGetPrimaryParameter(StructAbiMember member, IMethodSymbol primaryConstructor,
        [NotNullWhen(true)] out IParameterSymbol? parameter)
    {
        parameter = null;
        if (member.Kind == StructAbiMemberKind.CapturedParameter ||
            (member is { Kind: StructAbiMemberKind.AutoProperty, Symbol: IPropertySymbol property } &&
             property.TryGetDeclaringSyntax<ParameterSyntax>(out _)))
        {
            parameter = primaryConstructor.Parameters
                .AsStackEnumerable()
                .FirstOrDefault(member.Name, static (p, name) => p.Name == name);
        }

        return parameter != null;
    }
}
