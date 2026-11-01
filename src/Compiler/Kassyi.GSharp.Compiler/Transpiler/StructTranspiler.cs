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

        if (!namedStruct.IsRefLikeType)
        {
            var compilation = context.Query.SemanticModel.Compilation;
            foreach (var member in namedStruct.GetMembers())
            {
                if (member is IMethodSymbol conversion && IsEmittedStructConversion(conversion, compilation))
                {
                    RegisterUsedStructIfApplicable(conversion.ReturnType, ref context);
                    foreach (var parameter in conversion.Parameters)
                    {
                        RegisterUsedStructIfApplicable(parameter.Type, ref context);
                    }
                }
            }
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

            if (!s.IsRefLikeType)
            {
                foreach (var member in s.GetMembers())
                {
                    if (member is IMethodSymbol { MethodKind: MethodKind.Conversion, IsImplicitlyDeclared: false } conversion)
                    {
                        if (TryGetUserStruct(conversion.ReturnType, out var retDep) &&
                            !retDep.IsEqualTo(s) && structSet.Contains(retDep))
                        {
                            visit(retDep);
                        }

                        foreach (var parameter in conversion.Parameters)
                        {
                            if (TryGetUserStruct(parameter.Type, out var paramDep) &&
                                !paramDep.IsEqualTo(s) && structSet.Contains(paramDep))
                            {
                                visit(paramDep);
                            }
                        }
                    }
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
            var hasPack = StructLayoutReader.TryGetPack(structSym, out var packValue);
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

        WriteStructFooter(structSym, isInlineArray: true, ref context);
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
            var layout = TypeLayoutCalculator.GetStructLayout(structSym, context.Emitter.Dialect);
            if (layout.Kind == TypeLayoutKind.Explicit)
            {
                WriteExplicitLayoutMembers(ref context, layout);
            }
            else
            {
                foreach (var member in StructAbiMembers.Get(structSym))
                {
                    WriteStructMember(ref context, member);
                }

                var end = 0;
                foreach (var field in layout.DeviceFields)
                {
                    end = Math.Max(end, field.Offset + field.Layout.Size);
                }

                WriteTailPadding(ref context, 0, end, layout);
            }

            foreach (var member in structSym.GetMembers())
            {
                if (structSym.IsRefLikeType &&
                    member is IMethodSymbol { MethodKind: MethodKind.Conversion, Name: "op_Implicit" } conv)
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

            // Declared the same way, defined only by the methods that sort with them (SortComparisonMembers)
            SortComparisonMembers.WriteDeclarations(structSym, ref context);
        }

        context.Emitter.Writer.AppendLine(";");
        WriteStructStaticAsserts(structSym, isInlineArray: false, ref context);
        WriteStructConstructors(structSym, ref context);
        WriteStructConversionOperators(structSym, ref context);
        context.Emitter.Writer
            .AppendLine("#endif")
            .AppendLine();
    }

    /// <summary>
    /// Whether <paramref name="conversion"/> is a user-defined struct conversion operator (<c>implicit operator</c> or
    /// <c>explicit operator</c>) emitted as a value-returning device function (<c>__gsharp_fn_...__op_Implicit</c> /
    /// <c>__op_Explicit</c>). A <c>ref struct</c> (such as <c>GpuBigIntRef</c>) keeps its inline C++ member conversion
    /// operators for <c>[GpuOperatorMacro]</c> buffer forwarding.
    /// </summary>
    public static bool IsEmittedStructConversion(IMethodSymbol? conversion, Compilation compilation) =>
        conversion is { MethodKind: MethodKind.Conversion, IsImplicitlyDeclared: false } &&
        !conversion.HasAttribute(KnownAttributes.GpuOperatorMacro) &&
        conversion.ContainingType is { IsRefLikeType: false } containingType &&
        GlobalCppNames.IsEmittedUserType(containingType) &&
        (TryGetConversionDeclaration(conversion, out _) ||
         (DeviceExportTable.IsOutside(conversion, compilation) && DeviceExportTable.TryFind(conversion, out _)));

    private static bool TryGetConversionDeclaration(IMethodSymbol conversion,
        [NotNullWhen(true)] out ConversionOperatorDeclarationSyntax? declaration)
    {
        if (conversion.TryGetDeclaringSyntax<ConversionOperatorDeclarationSyntax>(out declaration))
        {
            return declaration.Body != null || declaration.ExpressionBody != null;
        }

        declaration = null;
        return false;
    }

    private static void WriteStructConversionOperators(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        if (structSym.IsRefLikeType)
        {
            return;
        }

        var compilation = context.Query.SemanticModel.Compilation;
        foreach (var member in structSym.GetMembers())
        {
            if (member is IMethodSymbol conversion &&
                IsEmittedStructConversion(conversion, compilation) &&
                TryGetConversionDeclaration(conversion, out var declaration) &&
                compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                WriteStructConversionOperator( conversion, declaration, ref context);
            }
        }
    }

    private static void WriteStructConversionOperator(IMethodSymbol conversion,
        ConversionOperatorDeclarationSyntax declaration, ref TranspileContext context)
    {
        var query = context.Query;
        var reporter = context.Reporter;
        var model = query.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
        context.Query = new Query(model, query.BclIntrinsicMap, query.UsedStructs);
        context.Reporter = reporter.WithSemanticModel(model);
        var previousCtorType = context.Emitter.StructConstructorType;
        var previousCallableDepth = context.Emitter.CallableDepth;
        context.Emitter.StructConstructorType = null;
        context.Emitter.CallableDepth = 0;

        try
        {
            context.Emitter.Writer.Append("__device__ inline ");
            context.Emitter.Dialect.AppendType(ref context, conversion.ReturnType);
            context.Emitter.Writer.Append(' ').Append(GlobalCppNames.GetFunctionName(conversion));
            context.Visitor.Visit(declaration.ParameterList);
            context.Emitter.Writer.Append(' ');

            LambdaTranspilerHelper.TranspileLambdaBody(
                ref context,
                declaration.Body,
                declaration.ExpressionBody?.Expression,
                isVoidExpression: false);
        }
        finally
        {
            context.Emitter.StructConstructorType = previousCtorType;
            context.Emitter.CallableDepth = previousCallableDepth;
            context.Query = query;
            context.Reporter = reporter;
        }
    }

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

    // [WHY] Defining a C++ constructor inside the struct makes it non-aggregate, which breaks CUDA __shared__ and brace
    // initialization. Instead, each user-defined constructor is emitted right after the struct as an inline factory
    // function that default-initializes __gsharp_self{}, runs the body, and returns __gsharp_self (matching Roslyn's
    // LocalRewriter_ObjectCreationExpression.MakeObjectCreationExpressionForStruct).
    private static void WriteStructConstructors(INamedTypeSymbol structSym, ref TranspileContext context)
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
        var query = context.Query;
        var reporter = context.Reporter;
        var model = query.SemanticModel.Compilation.GetSemanticModel(parameterList.SyntaxTree);
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
            context.Visitor.Visit(parameterList);
            context.Emitter.Writer.Append(' ');

            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Dialect.AppendType(ref context, structSym);
                if (declaration?.Initializer is { } initializer)
                {
                    var target = ResolveChainedConstructor(structSym, model.GetSymbol(initializer) as IMethodSymbol);
                    if (target != null && IsEmittedStructConstructor(target, model.Compilation))
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
                    WriteStructMemberInitializers(structSym, declaration == null ? constructor : null, ref context);
                }

                if (declaration?.Body != null)
                {
                    context.Visitor.VisitBlock(declaration.Body);
                }
                else if (declaration?.ExpressionBody != null)
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
                    .Append(GetMemberName(member.Symbol, ref context))
                    .Append(" = ");
                CppNameResolver.AppendSymbolName(parameter, ref context);
                context.Emitter.Writer.AppendLine(";");
                continue;
            }

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

    /// <summary>
    /// Emits the members of a <c>[StructLayout(LayoutKind.Explicit)]</c> struct in offset order: the fields that share
    /// an offset go into one anonymous <c>union</c>, the largest first, and a gap before a field or before
    /// <c>StructLayout.Size</c> becomes <c>unsigned char __gsharp_pad_&lt;k&gt;[&lt;n&gt;];</c>.
    /// </summary>
    /// <remarks>
    /// The members of an anonymous union are members of the struct, so member accesses keep their names. Partially
    /// overlapping fields cannot be expressed (GSH0078 reports them); they are emitted in offset order without padding.
    /// </remarks>
    private static void WriteExplicitLayoutMembers(ref TranspileContext context, StructLayoutInfo layout)
    {
        const int MaxStackFields = 64;
        var fields = layout.DeviceFields;
        Span<int> order = fields.Length <= MaxStackFields ? stackalloc int[fields.Length] : new int[fields.Length];
        for (var i = 0; i < order.Length; i++)
        {
            // [WHY] Insertion sort by offset, the larger field first at one offset: S{} initializes only the first
            // member of an anonymous union, so the largest one zeroes the whole group. Ties keep the declaration order.
            var j = i;
            while (j > 0 && GoesAfter(fields[order[j - 1]], fields[i]))
            {
                order[j] = order[j - 1];
                j--;
            }

            order[j] = i;
        }

        var cursor = 0;
        var padCount = 0;
        for (var start = 0; start < order.Length;)
        {
            var offset = fields[order[start]].Offset;
            var end = start;
            var groupEnd = offset;
            while (end < order.Length && fields[order[end]].Offset == offset)
            {
                groupEnd = Math.Max(groupEnd, offset + fields[order[end]].Layout.Size);
                end++;
            }

            if (offset > cursor)
            {
                WritePadding(ref context, padCount++, offset - cursor);
            }

            if (end - start > 1)
            {
                context.Emitter.Writer.Append("union");
                using (context.Emitter.Writer.Scope())
                {
                    for (var k = start; k < end; k++)
                    {
                        WriteStructMember(ref context, fields[order[k]].Member);
                    }
                }

                context.Emitter.Writer.AppendLine(";");
            }
            else
            {
                WriteStructMember(ref context, fields[order[start]].Member);
            }

            cursor = Math.Max(cursor, groupEnd);
            start = end;
        }

        WriteTailPadding(ref context, padCount, cursor, layout);
    }

    /// <summary>
    /// Pads a struct whose members end at <paramref name="cursor"/> up to its device size, which
    /// <c>StructLayout.Size</c> can make larger than the members rounded up to the alignment:
    /// <c>unsigned char __gsharp_pad_&lt;padIndex&gt;[&lt;n&gt;];</c>.
    /// </summary>
    /// <remarks>
    /// [WHY] C++ rounds the size up to the alignment only; a larger StructLayout.Size needs explicit bytes (#621).
    /// </remarks>
    internal static void WriteTailPadding(ref TranspileContext context, int padIndex, int cursor,
        StructLayoutInfo layout)
    {
        if (TypeLayoutCalculator.AlignUp(cursor, layout.Device.Alignment) < layout.Device.Size)
        {
            WritePadding(ref context, padIndex, layout.Device.Size - cursor);
        }
    }

    private static bool GoesAfter(FieldLayout earlier, FieldLayout field) =>
        earlier.Offset > field.Offset || (earlier.Offset == field.Offset && earlier.Layout.Size < field.Layout.Size);

    // unsigned char __gsharp_pad_<index>[<bytes>];
    private static void WritePadding(ref TranspileContext context, int index, int bytes) =>
        context.Emitter.Writer.AppendLine($"unsigned char {GSharpInternalNames.Struct.PaddingPrefix}{index}[{bytes}];");

    private static void WriteStructHeader(ref TranspileContext context, INamedTypeSymbol structSym)
    {
        var guardName = CppNameFormatter.GetIncludeGuardName(structSym);

        context.Emitter.Writer.Append("#ifndef ").AppendLine(guardName)
            .Append("#define ").AppendLine(guardName);

        context.Emitter.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine();
    }

    private static void WriteStructStaticAsserts(INamedTypeSymbol structSym, bool isInlineArray,
        ref TranspileContext context)
    {
        var layout = TypeLayoutCalculator.GetStructLayout(structSym, context.Emitter.Dialect);
        var csType = GlobalCppNames.GetTypePath(structSym).ToDisplayString();
        context.Emitter.Writer.Append("static_assert(sizeof(")
            .AppendFullyQualifiedCppName(structSym)
            .Append($")=={layout.Device.Size},\"{csType}: size\");")
            .AppendLine();
        context.Emitter.Writer.Append("static_assert(alignof(")
            .AppendFullyQualifiedCppName(structSym)
            .Append($")=={layout.Device.Alignment},\"{csType}: alignment\");")
            .AppendLine();

        if (!isInlineArray)
        {
            foreach (var field in layout.DeviceFields)
            {
                var cppMember = GetMemberName(field.Member.Symbol, ref context);
                var csMember = field.Member.Name;
                context.Emitter.Writer.Append("static_assert(offsetof(")
                    .AppendFullyQualifiedCppName(structSym)
                    .Append($",{cppMember})=={field.Offset},\"{csType}.{csMember}: offset\");")
                    .AppendLine();
            }
        }
    }

    private static void WriteStructFooter(INamedTypeSymbol structSym, bool isInlineArray, ref TranspileContext context)
    {
        context.Emitter.Writer
            .AppendLine(";");
        WriteStructStaticAsserts(structSym, isInlineArray, ref context);
        context.Emitter.Writer
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

    /// <summary>
    /// Whether a device code export records <paramref name="type"/> on its own, although no device code of the assembly
    /// uses it: a struct of the source that other assemblies can name and whose device definition has parts that a
    /// consumer cannot build from metadata (a constructor with a body, a computed property, a <c>Dispose</c>).
    /// </summary>
    /// <remarks>A struct without such parts is defined from metadata by the consumer, from its fields alone.</remarks>
    public static bool IsExportedOnItsOwn(INamedTypeSymbol type, Compilation compilation)
    {
        if (!GlobalCppNames.IsEmittedUserType(type) || type.IsGenericType || type.IsRefLikeType ||
            !IsVisibleToOtherAssemblies(type) ||
            !type.TryGetDeclaringSyntax<TypeDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return false;
        }

        return type.InstanceConstructors
                   .AsStackEnumerable()
                   .Any(compilation, static (constructor, c) => IsEmittedStructConstructor(constructor, c)) ||
               type.GetMembers()
                   .AsStackEnumerable()
                   .Any(compilation, static (member, c) =>
                       (member is IPropertySymbol property && StructAbiMembers.IsComputedProperty(property)) ||
                       (member is IMethodSymbol conversion && IsEmittedStructConversion(conversion, c))) ||
               UsingTranspiler.FindDisposeMethod(type, compilation) != null;
    }

    // Public or internal, and so are the types that contain it
    private static bool IsVisibleToOtherAssemblies(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal
                or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes the device definition of <paramref name="type"/> for <paramref name="dialect"/> outside any method, as a
    /// method transpilation writes it in front of the method, with its <c>Dispose</c> definition and the definitions of
    /// every struct that its text names (fields, constructor parameters, operator results), each apart.
    /// </summary>
    /// <remarks>
    /// [WHY] A consumer imports the struct's record alone; the structs that its text names must come from records as
    /// well (the library's own structs) or from the record's text (the structs that no record holds, such as the
    /// Kassyi.GSharp.Core vector types; see <c>DeviceExportEmitter.CreateStructRecords</c>).
    /// </remarks>
    public static StructSourceCollector RenderStandalone(INamedTypeSymbol type, SemanticModel semanticModel,
        ITranspilerDialect dialect, Action<TranspileFailure> reportFailure)
    {
        var compilation = semanticModel.Compilation;
        using var visitor = RoslynToCudaVisitor.Rent(dialect, semanticModel, reportFailure,
            BclIntrinsicMapBuilder.GetOrBuildMap(compilation));
        ref var context = ref visitor.Context;
        var collector = new StructSourceCollector();

        RegisterUsedStructIfApplicable(type, ref context);
        using var disposeWriter = new SourceWriter();
        if (UsingTranspiler.FindDisposeMethod(type, compilation) != null)
        {
            context.Emitter.Writer = disposeWriter;
            context.Emitter.DisposedStructs.Add(type);
            UsingTranspiler.WriteDisposeDefinitions(ref context, collector.Disposes, isolate: true);
        }

        // Each struct keeps its own failures: a struct that the text names and cannot be emitted (a host type) marks its
        // own record failed, which the consumer reports at the use (GSH0089), not the struct that names it
        using var writer = new SourceWriter();
        context.Emitter.Writer = writer;
        WriteDiscoveredDefinitions(ref context, collector);
        return collector;
    }

    /// <summary>
    /// Writes the definition of <paramref name="type"/> into a writer of its own, with a set of includes of its own,
    /// and returns it with whether it met a construct that could not be emitted; the includes are also added to the
    /// transpilation's.
    /// </summary>
    /// <param name="type">The struct.</param>
    /// <param name="context">The transpilation.</param>
    /// <param name="isolate">
    /// Whether the failures stay with the part instead of also reaching the transpilation's failures, and an exception
    /// of the writer marks the part failed instead of stopping the transpilation: a definition that the method's own
    /// text does not contain (<see cref="WriteDiscoveredDefinitions"/>), or a struct exported on its own.
    /// </param>
    public static StructSourcePart WriteDefinitionPart(INamedTypeSymbol type, ref TranspileContext context, bool isolate)
    {
        var writer = context.Emitter.Writer;
        var includes = context.Emitter.RequiredIncludes;
        var reporter = context.Reporter;
        var own = new HashSet<string>(StringComparer.Ordinal);
        var failure = new PartFailure();
        var internalError = "";
        using var structWriter = new SourceWriter();
        context.Emitter.Writer = structWriter;
        context.Emitter.RequiredIncludes = own;
        context.Reporter = reporter.Intercept(failure.Mark, forward: !isolate);
        try
        {
            WriteStructDefinition(type, ref context);
        }
        catch (Exception exception) when (isolate && exception is not OperationCanceledException)
        {
            // [WHY] Not swallowed: the part is failed and its record keeps the message (InternalError), which a consumer
            // that uses the struct states in GSH0089; a struct that no device code of the library uses does not stop the
            // library's build
            failure.Mark();
            internalError = PartFailure.Describe(exception);
        }
        finally
        {
            context.Emitter.Writer = writer;
            context.Emitter.RequiredIncludes = includes;
            context.Reporter = reporter;
            includes.UnionWith(own);
        }

        return new StructSourcePart(type, structWriter.ToString(), ToIncludes(own), failure.Failed, internalError);
    }

    /// <summary>
    /// Writes into <see cref="StructSourceCollector.Discovered"/> the definitions of the structs that
    /// <paramref name="context"/> registered and <paramref name="collector"/> has no definition of, until the
    /// definitions it writes name no further struct. Each keeps its own failures (<see cref="WriteDefinitionPart"/>).
    /// </summary>
    public static void WriteDiscoveredDefinitions(ref TranspileContext context, StructSourceCollector collector)
    {
        while (true)
        {
            var pending = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            GetTopologicallySortedStructs(context.Query.UsedStructs)
                .AsStackEnumerable()
                .Where(collector, static (type, c) => !c.HasDefinition(type))
                .AddTo(pending);
            if (pending.Count == 0)
            {
                return;
            }

            foreach (var type in pending)
            {
                collector.Discovered.Add(WriteDefinitionPart(type, ref context, isolate: true));
            }
        }
    }

    /// <summary>The includes of a set, in ordinal order.</summary>
    public static EquatableArray<string> ToIncludes(HashSet<string> includes)
    {
        var builder = ImmutableArray.CreateBuilder<string>(includes.Count);
        includes
            .AsStackEnumerable()
            .AddTo(builder);
        builder.Sort(StringComparer.Ordinal);
        return builder.MoveToImmutable().AsEquatableArray();
    }
}
