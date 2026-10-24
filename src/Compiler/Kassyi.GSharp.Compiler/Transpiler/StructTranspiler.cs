using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Emits CUDA C++ struct definitions, operator overloads, and handles InlineArray structures.</summary>
internal static class StructTranspiler
{
    /// <summary>
    /// Substitutes type parameters of <paramref name="currentStruct"/> in <paramref name="typeSymbol"/> with the
    /// concrete type arguments of <paramref name="currentStruct"/>.
    /// </summary>
    internal static ITypeSymbol? SubstituteTypeParameters(ITypeSymbol? typeSymbol, INamedTypeSymbol? currentStruct,
        Compilation? compilation = null)
    {
        if (typeSymbol == null || currentStruct is not { IsGenericType: true } || currentStruct.IsDefinition)
        {
            return typeSymbol;
        }

        return SubstituteTypeParametersCore(typeSymbol, currentStruct, compilation);
    }

    private static ITypeSymbol SubstituteTypeParametersCore(ITypeSymbol typeSymbol, INamedTypeSymbol currentStruct,
        Compilation? compilation)
    {
        if (typeSymbol is ITypeParameterSymbol typeParam)
        {
            var typeParams = currentStruct.TypeParameters;
            for (var i = 0; i < typeParams.Length; i++)
            {
                if (typeParams[i].IsEqualTo(typeParam))
                {
                    return currentStruct.TypeArguments[i];
                }
            }

            return typeSymbol;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType && compilation != null)
        {
            var elem = SubstituteTypeParametersCore(arrayType.ElementType, currentStruct, compilation);
            return ReferenceEquals(elem, arrayType.ElementType)
                ? arrayType
                : compilation.CreateArrayTypeSymbol(elem, arrayType.Rank);
        }

        if (typeSymbol is IPointerTypeSymbol pointerType && compilation != null)
        {
            var pointedAt = SubstituteTypeParametersCore(pointerType.PointedAtType, currentStruct, compilation);
            return ReferenceEquals(pointedAt, pointerType.PointedAtType)
                ? pointerType
                : compilation.CreatePointerTypeSymbol(pointedAt);
        }

        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType && !namedType.IsDefinition)
        {
            var args = namedType.TypeArguments;
            ITypeSymbol[]? substituted = null;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = SubstituteTypeParametersCore(args[i], currentStruct, compilation);
                if (!ReferenceEquals(arg, args[i]))
                {
                    if (substituted == null)
                    {
                        substituted = new ITypeSymbol[args.Length];
                        for (var j = 0; j < i; j++)
                        {
                            substituted[j] = args[j];
                        }
                    }
                }

                if (substituted != null)
                {
                    substituted[i] = arg;
                }
            }

            return substituted != null ? namedType.OriginalDefinition.Construct(substituted) : namedType;
        }

        return typeSymbol;
    }

    /// <summary>
    /// Recursively registers referenced struct symbols and their field types to ensure necessary forward definitions are emitted.
    /// </summary>
    public static void RegisterUsedStructIfApplicable(ITypeSymbol? typeSymbol, ref TranspileContext context)
    {
        typeSymbol = SubstituteTypeParameters(typeSymbol, context.Emitter.CurrentStructType,
            context.Query.SemanticModel.Compilation);
        if (!TryGetUserStruct(typeSymbol, out var namedStruct))
        {
            return;
        }

        if (!context.Query.UsedStructs.Add(namedStruct))
        {
            return;
        }

        foreach (var typeArgument in namedStruct.TypeArguments)
        {
            RegisterUsedStructIfApplicable(typeArgument, ref context);
        }

        foreach (var member in StructAbiMembers.Get(namedStruct))
        {
            RegisterUsedStructIfApplicable(member.Type, ref context);
        }
    }

    /// <summary>
    /// Topologically sorts referenced structs in the current context so that dependent nested structs are declared first in C++.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> GetTopologicallySortedStructs(ref TranspileContext context)
        => GetTopologicallySortedStructs(context.Query.UsedStructs);

    /// <summary>
    /// Topologically sorts referenced structs so that dependent nested structs are declared first in C++.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> GetTopologicallySortedStructs(
        IEnumerable<INamedTypeSymbol> usedStructs)
    {
        // [WHY] CUDA C++ requires full struct definitions in dependency order before value-type usage as fields.
        var result = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var structSet = usedStructs as ISet<INamedTypeSymbol> ??
                        new HashSet<INamedTypeSymbol>(usedStructs, SymbolEqualityComparer.Default);
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var s in structSet)
        {
            visit(s);
        }

        return result.ToImmutable();

        void visit(INamedTypeSymbol s)
        {
            if (visited.Contains(s))
            {
                return;
            }

            if (!visiting.Add(s))
            {
                return;
            }

            foreach (var typeArgument in s.TypeArguments)
            {
                if (TryGetUserStruct(typeArgument, out var dep) && structSet.Contains(dep))
                {
                    visit(dep);
                }
            }

            foreach (var member in StructAbiMembers.Get(s))
            {
                if (TryGetUserStruct(member.Type, out var dep) && structSet.Contains(dep))
                {
                    visit(dep);
                }
            }

            visiting.Remove(s);
            visited.Add(s);
            result.Add(s);
        }
    }

    private static bool TryGetUserStruct(ITypeSymbol? typeSymbol, [NotNullWhen(true)] out INamedTypeSymbol? namedStruct)
    {
        // [WHY] Array-like wrappers (T[], Memory<T>, ImmutableArray<T>, ...) reach the device as GSharpArray<T>; only their element type can be a user struct.
        var structType = typeSymbol.TryGetArrayLikeElementType(out var elementType) ? elementType : typeSymbol;
        if (structType is INamedTypeSymbol candidate && GlobalCppNames.IsEmittedUserType(candidate))
        {
            namedStruct = candidate;
            return true;
        }

        namedStruct = null;
        return false;
    }

    /// <summary>
    /// Emits the C++ struct definition for the specified named struct symbol, handling InlineArray structures and operator overloads.
    /// </summary>
    public static void WriteStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var previousStructType = context.Emitter.CurrentStructType;
        context.Emitter.CurrentStructType = structSym;

        try
        {
            var hasPack = structSym.TryGetStructLayoutPack(out var packValue);
            if (hasPack)
            {
                context.Emitter.Writer.AppendLine($"#pragma pack(push, {packValue})");
            }

            var inlineArrayAttr = structSym.GetAttribute(KnownAttributes.InlineArray);
            if (inlineArrayAttr != null)
            {
                WriteInlineArrayDefinition(structSym, inlineArrayAttr, ref context);
            }
            else
            {
                WriteStandardStructDefinition(structSym, ref context);
            }

            if (hasPack)
            {
                context.Emitter.Writer.AppendLine("#pragma pack(pop)");
            }
        }
        finally
        {
            context.Emitter.CurrentStructType = previousStructType;
        }
    }

    private static void WriteInlineArrayDefinition(INamedTypeSymbol structSym, AttributeData inlineArrayAttr,
        ref TranspileContext context)
    {
        var length = (int)(inlineArrayAttr.ConstructorArguments[0].Value ?? 0);
        if (!TryGetFirstMember(structSym, out var firstMember))
        {
            return;
        }

        var element = GetMemberName(firstMember.Symbol, ref context);
        WriteStructHeader(ref context, structSym);
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Dialect.AppendType(ref context, firstMember.Type);
            context.Emitter.Writer.Append($" {element}");
            using (context.Emitter.Writer.Brackets())
            {
                context.Emitter.Writer.Append(length);
            }

            context.Emitter.Writer.AppendLine(";");

            WriteInlineArrayIndexer(ref context, firstMember.Type, element, isConst: false);
            WriteInlineArrayIndexer(ref context, firstMember.Type, element, isConst: true);
            WriteInlineArrayIterators(ref context, firstMember.Type, element, length);
        }

        WriteStructFooter(ref context);
    }

    private static void WriteInlineArrayIterators(ref TranspileContext context, ITypeSymbol elementType, string element,
        int length)
    {
        WriteInlineArrayIteratorPair(ref context, elementType, element, length, isConst: false);
        WriteInlineArrayIteratorPair(ref context, elementType, element, length, isConst: true);
        context.Emitter.Writer.AppendLine($"__device__ inline int size() const {{ return {length}; }}");
    }

    private static void WriteInlineArrayIteratorPair(ref TranspileContext context, ITypeSymbol elementType,
        string element, int length, bool isConst)
    {
        var prefix = isConst ? "__device__ inline const " : "__device__ inline ";
        var suffix = isConst ? " const" : string.Empty;

        context.Emitter.Writer.Append(prefix);
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.AppendLine($"* begin(){suffix} {{ return {element}; }}");

        context.Emitter.Writer.Append(prefix);
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.AppendLine($"* end(){suffix} {{ return {element} + {length}; }}");
    }

    private static void WriteInlineArrayIndexer(ref TranspileContext context, ITypeSymbol elementType, string element,
        bool isConst)
    {
        context.Emitter.Writer.Append(isConst ? "__device__ inline const " : "__device__ inline ");
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.Append("& operator[]");

        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append("int index");
        }

        if (isConst)
        {
            context.Emitter.Writer.AppendLine(" const");
        }

        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.Append($"return {element}");
            using (context.Emitter.Writer.Brackets())
            {
                context.Emitter.Writer.Append("index");
            }

            context.Emitter.Writer.AppendLine(";");
        }
    }

    private static void WriteStandardStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        WriteStructHeader(ref context, structSym);

        using (context.Emitter.Writer.Scope())
        {
            foreach (var member in StructAbiMembers.Get(structSym))
            {
                WriteStructMember(ref context, member);
            }

            foreach (var member in structSym.GetMembers())
            {
                if (member is IMethodSymbol { MethodKind: MethodKind.Conversion, Name: "op_Implicit" } conv)
                {
                    WriteImplicitConversionOperator(structSym, ref context, conv);
                }
                else if (member is IPropertySymbol property && StructAbiMembers.IsComputedProperty(property))
                {
                    WriteComputedPropertyGetter(property, ref context);
                    WriteComputedPropertySetter(property, ref context);
                }
            }

            // [WHY] Declared in every definition, defined only by the methods that dispose the struct (UsingTranspiler):
            // the include guard keeps the first definition of the struct that any method writes.
            if (UsingTranspiler.FindDisposeMethod(structSym, context.Query.SemanticModel.Compilation) != null)
            {
                context.Emitter.Writer.AppendLine($"__device__ void {GSharpInternalNames.Using.Dispose}();");
            }
        }

        context.Emitter.Writer.AppendLine(";");
        WriteStructConstructors(structSym, ref context);
        context.Emitter.Writer
            .AppendLine("#endif")
            .AppendLine();
    }

    /// <summary>
    /// Whether <paramref name="constructor"/> is a user-defined struct constructor emitted as a value-returning device
    /// function (<c>__gsharp_ctor_...</c>) rather than aggregate initialization.
    /// </summary>
    public static bool IsEmittedStructConstructor(IMethodSymbol? constructor) =>
        constructor is { MethodKind: MethodKind.Constructor, IsImplicitlyDeclared: false, IsStatic: false } &&
        constructor.ContainingType is { } containingType &&
        GlobalCppNames.IsEmittedUserType(containingType) &&
        constructor.TryGetDeclaringSyntax<ConstructorDeclarationSyntax>(out var declaration) &&
        (declaration.Body != null || declaration.ExpressionBody != null);

    // [WHY] Defining a C++ constructor inside the struct makes it non-aggregate, which breaks CUDA __shared__ and brace
    // initialization. Instead, each user-defined constructor is emitted right after the struct as an inline factory
    // function that default-initializes __gsharp_self{}, runs the body, and returns __gsharp_self (matching Roslyn's
    // LocalRewriter_ObjectCreationExpression.MakeObjectCreationExpressionForStruct).
    private static void WriteStructConstructors(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        var ordered = ImmutableArray.CreateBuilder<(
            IMethodSymbol Constructor, ConstructorDeclarationSyntax Declaration)>();
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
                !IsEmittedStructConstructor(constructor) ||
                !constructor.TryGetDeclaringSyntax<ConstructorDeclarationSyntax>(out var declaration) ||
                !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                return;
            }

            if (declaration.Initializer is { } initializer)
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
    private static void WriteStructConstructor(INamedTypeSymbol structSym, IMethodSymbol constructor,
        ConstructorDeclarationSyntax declaration, ref TranspileContext context)
    {
        var query = context.Query;
        var reporter = context.Reporter;
        var model = query.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
        context.Query = new Query(model, query.BclIntrinsicMap, query.UsedStructs);
        context.Reporter = reporter.WithSemanticModel(model);
        var previousCtorType = context.Emitter.StructConstructorType;
        var previousCallableDepth = context.Emitter.CallableDepth;
        context.Emitter.StructConstructorType = structSym;
        context.Emitter.CallableDepth = 1;

        try
        {
            context.Emitter.Writer.Append("__device__ inline ");
            context.Emitter.Dialect.AppendType(ref context, structSym);
            context.Emitter.Writer.Append(' ').Append(GlobalCppNames.GetConstructorName(constructor));
            context.Visitor.Visit(declaration.ParameterList);
            context.Emitter.Writer.Append(' ');

            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Dialect.AppendType(ref context, structSym);
                if (declaration.Initializer is { } initializer)
                {
                    var target = ResolveChainedConstructor(structSym, model.GetSymbol(initializer) as IMethodSymbol);
                    if (target != null && IsEmittedStructConstructor(target))
                    {
                        context.Emitter.Writer.Append($" {GSharpInternalNames.Lowering.ConstructorSelf} = ");
                        ObjectCreationHelper.TranspileConstructorCall(structSym, target, initializer.ArgumentList,
                            ref context);
                        context.Emitter.Writer.AppendLine(";");
                    }
                    else if (target is { Parameters.Length: 0 })
                    {
                        context.Emitter.Writer.AppendLine($" {GSharpInternalNames.Lowering.ConstructorSelf}{{}};");
                    }
                    else
                    {
                        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, initializer,
                            initializer.Kind().ToString());
                        context.Emitter.Writer.AppendLine($" {GSharpInternalNames.Lowering.ConstructorSelf}{{}};");
                    }
                }
                else
                {
                    context.Emitter.Writer.AppendLine($" {GSharpInternalNames.Lowering.ConstructorSelf}{{}};");
                    WriteStructMemberInitializers(structSym, ref context);
                }

                if (declaration.Body != null)
                {
                    context.Visitor.VisitBlock(declaration.Body);
                }
                else if (declaration.ExpressionBody != null)
                {
                    context.Visitor.Visit(declaration.ExpressionBody.Expression);
                    context.Emitter.Writer.AppendLine(";");
                }

                context.Emitter.Writer.AppendLine($"return {GSharpInternalNames.Lowering.ConstructorSelf};");
            }
        }
        finally
        {
            context.Emitter.StructConstructorType = previousCtorType;
            context.Emitter.CallableDepth = previousCallableDepth;
            context.Query = query;
            context.Reporter = reporter;
        }
    }

    private static void WriteStructMemberInitializers(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        foreach (var member in StructAbiMembers.Get(structSym))
        {
            if (!TryGetMemberInitializer(member.Symbol, compilation, out var initializer))
            {
                continue;
            }

            var query = context.Query;
            var reporter = context.Reporter;
            var model = compilation.GetSemanticModel(initializer.SyntaxTree);
            context.Query = new Query(model, query.BclIntrinsicMap, query.UsedStructs);
            context.Reporter = reporter.WithSemanticModel(model);

            try
            {
                context.Emitter.Writer.Append($"{GSharpInternalNames.Lowering.ConstructorSelf}.")
                    .Append(GetMemberName(member.Symbol, ref context))
                    .Append(" = ");
                context.Visitor.Visit(initializer.Value);
                context.Emitter.Writer.AppendLine(";");
            }
            finally
            {
                context.Query = query;
                context.Reporter = reporter;
            }
        }
    }

    private static void WriteStructHeader(ref TranspileContext context, INamedTypeSymbol structSym)
    {
        var guardName = CppNameFormatter.GetIncludeGuardName(structSym);

        context.Emitter.Writer.Append("#ifndef ").AppendLine(guardName)
            .Append("#define ").AppendLine(guardName);

        context.Emitter.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine();
    }

    private static void WriteStructFooter(ref TranspileContext context)
    {
        context.Emitter.Writer
            .AppendLine(";")
            .AppendLine("#endif")
            .AppendLine();
    }

    private static void WriteImplicitConversionOperator(INamedTypeSymbol structSym, ref TranspileContext context,
        IMethodSymbol conv)
    {
        var hasFirstMember = TryGetFirstMember(structSym, out var firstMember);

        if (conv.ReturnType.IsEqualTo(structSym))
        {
            // Implicit constructor from param type
            context.Emitter.Writer.Append("__device__ inline ").AppendFullyQualifiedCppName(structSym)
                .AppendLine("() {}");

            context.Emitter.Writer.Append("__device__ inline ").AppendFullyQualifiedCppName(structSym);
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, conv.Parameters[0].Type);
                context.Emitter.Writer.Append(" v");
            }

            context.Emitter.Writer.Append(' ');
            using (context.Emitter.Writer.Scope())
            {
                if (hasFirstMember)
                {
                    context.Emitter.Writer.AppendLine($"this->{GetMemberName(firstMember.Symbol, ref context)} = v;");
                }
            }
        }
        else
        {
            // Implicit conversion operator to return type
            context.Emitter.Writer.Append("__device__ inline operator ");
            context.Emitter.Dialect.AppendType(ref context, conv.ReturnType);
            context.Emitter.Writer.Append("() const ");
            using (context.Emitter.Writer.Scope())
            {
                if (!hasFirstMember)
                {
                    return;
                }

                context.Emitter.Writer.Append($"return this->{GetMemberName(firstMember.Symbol, ref context)}");
                context.Emitter.Writer.AppendLine(";");
            }
        }
    }

    // __device__ inline <type> <property name>() const { <getter body> }
    // [WHY] The getter keeps the C++ name of the property (CppNameResolver), so the implicit `A` and `Sum` in a getter body
    // resolve to the members of the struct, and a read elsewhere only appends the call (ComputedPropertyGetterCall).
    private static void WriteComputedPropertyGetter(IPropertySymbol property, ref TranspileContext context)
    {
        // A struct from metadata or from another compilation has no getter body to bind here.
        var compilation = context.Query.SemanticModel.Compilation;
        if (!property.TryGetDeclaringSyntax<PropertyDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            !TryGetGetterBody(declaration, out var block, out var expression))
        {
            return;
        }

        context.Emitter.Writer.Append("__device__ inline ");
        context.Emitter.Dialect.AppendType(ref context, property.Type);
        context.Emitter.Writer.Append(' ')
            .AppendCppName(CppNameResolver.Resolve(property, property.Name, context.Emitter.Dialect))
            .Append("() const ");

        TranspilePropertyAccessorBody(declaration.SyntaxTree, block, expression, isVoidExpression: false, ref context);
    }

    // __device__ inline void <property name>(<type> <value parameter>) { <setter body> }
    // [WHY] The setter overloads the C++ name of the property with one parameter, so assignments and object / `with`
    // initializers call it by appending `(<rhs>)` after the member name (ComputedPropertyGetterCall).
    private static void WriteComputedPropertySetter(IPropertySymbol property, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        if (property.SetMethod is not { Parameters: [var valueParameter] } ||
            !property.TryGetDeclaringSyntax<PropertyDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            !TryGetSetterBody(declaration, out var block, out var expression))
        {
            return;
        }

        context.Emitter.Writer.Append("__device__ inline void ")
            .AppendCppName(CppNameResolver.Resolve(property, property.Name, context.Emitter.Dialect));
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, property.Type);
            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendSymbolName(valueParameter, ref context);
        }

        context.Emitter.Writer.Append(' ');
        TranspilePropertyAccessorBody(declaration.SyntaxTree, block, expression, isVoidExpression: true, ref context);
    }

    private static void TranspilePropertyAccessorBody(SyntaxTree syntaxTree, BlockSyntax? block,
        ExpressionSyntax? expression, bool isVoidExpression, ref TranspileContext context)
    {
        // [WHY] The struct may be declared in another file than the kernel, so its body is bound with a model of its own tree.
        var query = context.Query;
        var reporter = context.Reporter;
        var model = query.SemanticModel.Compilation.GetSemanticModel(syntaxTree);
        context.Query = new Query(model, query.BclIntrinsicMap, query.UsedStructs);
        context.Reporter = reporter.WithSemanticModel(model);

        try
        {
            LambdaTranspilerHelper.TranspileLambdaBody(ref context, block, expression, isVoidExpression);
        }
        finally
        {
            context.Query = query;
            context.Reporter = reporter;
        }
    }

    // The body of `=> expr`, `get => expr` or `get { ... }`
    private static bool TryGetGetterBody(PropertyDeclarationSyntax declaration, out BlockSyntax? block,
        out ExpressionSyntax? expression)
    {
        block = null;
        expression = declaration.ExpressionBody?.Expression;
        if (expression != null || declaration.AccessorList is not { } accessorList)
        {
            return expression != null;
        }

        var getter = accessorList.Accessors
            .AsStackEnumerable()
            .FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        block = getter?.Body;
        expression = getter?.ExpressionBody?.Expression;
        return block != null || expression != null;
    }

    // The body of `set => expr`, `set { ... }`, `init => expr` or `init { ... }`
    private static bool TryGetSetterBody(PropertyDeclarationSyntax declaration, out BlockSyntax? block,
        out ExpressionSyntax? expression)
    {
        block = null;
        expression = null;
        if (declaration.AccessorList is not { } accessorList)
        {
            return false;
        }

        var setter = accessorList.Accessors
            .AsStackEnumerable()
            .FirstOrDefault(static accessor =>
                accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
                accessor.IsKind(SyntaxKind.InitAccessorDeclaration));
        block = setter?.Body;
        expression = setter?.ExpressionBody?.Expression;
        return block != null || expression != null;
    }

    private static string GetMemberName(ISymbol member, ref TranspileContext context) =>
        CppNameResolver.Resolve(member, member.Name, context.Emitter.Dialect).ToString();

    private static bool TryGetFirstMember(INamedTypeSymbol structSym, out StructAbiMember firstMember)
    {
        foreach (var member in StructAbiMembers.Get(structSym))
        {
            firstMember = member;
            return true;
        }

        firstMember = default;
        return false;
    }

    private static SyntaxNode? GetMemberTypeSyntax(ISymbol symbol, Compilation compilation)
    {
        if (symbol.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) &&
            declarator.Parent is VariableDeclarationSyntax varDecl &&
            compilation.ContainsSyntaxTree(varDecl.SyntaxTree))
        {
            return varDecl.Type;
        }

        if (symbol.TryGetDeclaringSyntax<PropertyDeclarationSyntax>(out var propDecl) &&
            compilation.ContainsSyntaxTree(propDecl.SyntaxTree))
        {
            return propDecl.Type;
        }

        return null;
    }

    private static void WriteStructMember(ref TranspileContext context, StructAbiMember member)
    {
        var previousNode = context.Emitter.CurrentNode;
        context.Emitter.CurrentNode =
            GetMemberTypeSyntax(member.Symbol, context.Query.SemanticModel.Compilation);
        try
        {
            WriteStructMemberCore(ref context, member);
        }
        finally
        {
            context.Emitter.CurrentNode = previousNode;
        }
    }

    private static void WriteStructMemberCore(ref TranspileContext context, StructAbiMember member)
    {
        if (member is { Kind: StructAbiMemberKind.FixedBuffer, Symbol: IFieldSymbol field })
        {
            field.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var syntax);

            ExpressionSyntax? sizeExp = null;
            if (syntax?.ArgumentList is { Arguments.Count: > 0 } argList)
            {
                sizeExp = argList.Arguments[0].Expression;
            }

            if (sizeExp != null)
            {
                context.Emitter.Dialect.AppendType(ref context, member.Type);
                context.Emitter.Writer.Append($" {GetMemberName(field, ref context)}");
                using (context.Emitter.Writer.Brackets())
                {
                    context.Visitor.Visit(sizeExp);
                }

                context.Emitter.Writer.AppendLine(";");
            }
            else
            {
                context.Emitter.Dialect.AppendType(ref context, member.Type);
                context.Emitter.Writer.AppendLine(
                    $"* {GetMemberName(field, ref context)}; // ERROR: fixed size not found");
            }

            return;
        }

        WriteMemberDeclaration(member.Type, GetMemberName(member.Symbol, ref context), member.Symbol, ref context);
    }

    private static void WriteMemberDeclaration(ITypeSymbol type, string name, ISymbol memberSymbol,
        ref TranspileContext context)
    {
        var isReadOnly = memberSymbol.HasAttribute(KnownAttributes.GpuReadOnly);
        var isArray = type.TryGetArrayLikeElementType(out var elementType);

        if (isReadOnly && !isArray)
        {
            context.Emitter.Writer.Append("const ");
        }

        // C# 'volatile' forbids caching/reordering of the field access; C++ 'volatile' gives the same guarantee to nvcc/cl.
        if (memberSymbol is IFieldSymbol { IsVolatile: true })
        {
            context.Emitter.Writer.Append("volatile ");
        }

        if (isArray)
        {
            context.Emitter.Writer.Append("GSharpArray<");
            if (isReadOnly)
            {
                context.Emitter.Writer.Append("const ");
            }

            context.Emitter.Dialect.AppendType(ref context, elementType);
            context.Emitter.Writer.Append(">");
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, type);
        }

        context.Emitter.Writer.AppendLine($" {name};");
    }
}
