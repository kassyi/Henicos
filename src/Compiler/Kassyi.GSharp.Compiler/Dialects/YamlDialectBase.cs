namespace Kassyi.GSharp.Compiler.Dialects;

public abstract class YamlDialectBase : ITranspilerDialect
{
    public abstract string Name { get; }
    public virtual bool IsCuda => false;

    public virtual DialectCapabilities Capabilities =>
        DialectCapabilities.AllGpuFeatures;

    public virtual void AppendType(ref TranspileContext context, ITypeSymbol? typeSymbol)
    {
        typeSymbol = StructTranspiler.SubstituteTypeParameters(typeSymbol, context.Emitter.CurrentStructType,
            context.Query.SemanticModel.Compilation);
        typeSymbol = typeSymbol.UnwrapNullableArrayLike();

        if (typeSymbol == null)
        {
            context.Emitter.Writer.Append("void");
            return;
        }

        if (typeSymbol is ITypeParameterSymbol typeParam)
        {
            if (context.Emitter.CurrentNode is { } currentNode)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, currentNode,
                    "unsubstituted type parameter");
            }
            else
            {
                var location = typeParam.Locations.AsStackEnumerable()
                                   .FirstOrDefault(static loc => loc.IsInSource) ?? Location.None;
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, location,
                    "unsubstituted type parameter");
            }

            context.Emitter.Writer.Append("/*unsupported:TypeParameter*/");
            return;
        }

        if (typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } enumUnderlyingType })
        {
            typeSymbol = enumUnderlyingType;
        }

        if (typeSymbol.TryGetArrayLikeElementType(out var elementType))
        {
            context.Emitter.Writer.Append("GSharpArray<");
            AppendType(ref context, elementType);
            context.Emitter.Writer.Append(">");
            return;
        }

        if (IsCuda && typeSymbol.TypeKind == TypeKind.Delegate)
        {
            context.Emitter.Writer.Append("auto");
            return;
        }

        if (typeSymbol is IFunctionPointerTypeSymbol)
        {
            // [WHY] Function pointers have no type-then-name C++ spelling and host pointers are invalid on the device.
            // Declaration sites report GSH0003; this placeholder only keeps the emitted signature well-formed.
            context.Emitter.Writer.Append("void*");
            return;
        }

        if (typeSymbol is IPointerTypeSymbol pointer)
        {
            AppendType(ref context, pointer.PointedAtType);
            context.Emitter.Writer.Append("*");
            return;
        }

        if (WmmaTypeSyntaxHelper.TryAppendType(ref context, typeSymbol))
        {
            return;
        }

        if (TryMapType(GetTypeMappingKey(typeSymbol), out var mappedType))
        {
            context.Emitter.Writer.Append(mappedType);
            return;
        }

        StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
        context.Emitter.Writer.AppendFullyQualifiedCppName(typeSymbol);

        if (typeSymbol is not INamedTypeSymbol { IsGenericType: true } namedType ||
            GlobalCppNames.IsEmittedUserType(namedType))
        {
            return;
        }

        context.Emitter.Writer.Append("<");
        for (var i = 0; i < namedType.TypeArguments.Length; i++)
        {
            AppendType(ref context, namedType.TypeArguments[i]);
            if (i < namedType.TypeArguments.Length - 1)
            {
                context.Emitter.Writer.Append(", ");
            }
        }

        context.Emitter.Writer.Append(">");
    }

    public virtual bool TryMapType(string fullyQualifiedName, out string mappedType)
    {
        mappedType = string.Empty;
        return false;
    }

    public virtual TypeLayout ArrayViewLayout => new(16, 8);

    public virtual TypeLayout PointerLayout => new(8, 8);

    public virtual bool TryGetMappedTypeLayout(string fullyQualifiedName, out TypeLayout layout)
    {
        layout = default;
        return false;
    }

    public bool TryGetTypeLayout(ITypeSymbol typeSymbol, out TypeLayout layout) =>
        TryGetMappedTypeLayout(GetTypeMappingKey(typeSymbol), out layout);

    public bool IsMappedType(ITypeSymbol typeSymbol) => TryMapType(GetTypeMappingKey(typeSymbol), out _);

    private static string GetTypeMappingKey(ITypeSymbol typeSymbol)
    {
        var format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions &
            ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        // [WHY] Dictionary key lookup for Yaml Dialects requires a string allocation.
        // I tolerate ToDisplayString here because it is not in the execution hot path.
#pragma warning disable RS0030 // Do not used banned APIs
        return typeSymbol.ToDisplayString(format).Replace("global::", "");
#pragma warning restore RS0030
    }

    public virtual bool TryMapIntrinsic(ISymbol symbol, out string mappedExpression)
    {
        var intrinsicAttr = symbol.GetAttribute(KnownAttributes.CudaIntrinsic);
        if (intrinsicAttr is { ConstructorArguments.Length: > 0 })
        {
            mappedExpression = intrinsicAttr.ConstructorArguments[0].Value?.ToString() ?? symbol.Name;
            return true;
        }

        var patternAttr = symbol.ContainingType?.GetAttribute(KnownAttributes.CudaIntrinsicPattern);
        if (patternAttr is { ConstructorArguments.Length: > 0 })
        {
            var pattern = patternAttr.ConstructorArguments[0].Value?.ToString() ?? "{0}";
#pragma warning disable RS0030 // Intrinsic pattern formatting requires lowercase symbol name
            mappedExpression = string.Format(pattern, symbol.Name.ToLowerInvariant());
#pragma warning restore RS0030
            return true;
        }

        // [WHY] Dictionary key lookup for Yaml Dialects requires a string signature.
        // I tolerate ToDisplayString here because it is not in the execution hot path.
#pragma warning disable RS0030 // Do not used banned APIs
        var name = symbol.ToDisplayString(new SymbolDisplayFormat(
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters |
                           SymbolDisplayMemberOptions.IncludeContainingType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType |
                              SymbolDisplayParameterOptions.IncludeParamsRefOut,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
        )).Replace("global::", "");
#pragma warning restore RS0030

        return TryMapIntrinsicCore(name, out mappedExpression);
    }

    protected virtual bool TryMapIntrinsicCore(string signature, out string mappedExpression)
    {
        mappedExpression = string.Empty;
        return false;
    }

    public virtual bool TryMapMemberAccess(ITypeSymbol typeSymbol, string memberName, out string? mappedName,
        out bool isMethodCall)
    {
        mappedName = null;
        isMethodCall = false;

        if (typeSymbol.IsSystemNumericsVector())
        {
            mappedName = memberName switch
            {
                "X" => "x",
                "Y" => "y",
                "Z" => "z",
                "W" => "w",
                _ => char.ToLowerInvariant(memberName[0]) + (memberName.Length > 1 ? memberName[1..] : string.Empty),
            };
            return true;
        }

        if (memberName == "Length")
        {
            if (typeSymbol.HasAttribute(KnownAttributes.InlineArray))
            {
                mappedName = "size";
                isMethodCall = true;
                return true;
            }

            mappedName = "length";
            return true;
        }

        if (memberName != "Count")
        {
            return false;
        }

        mappedName = "length";
        return true;
    }

    public virtual (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameter(
        ref TranspileContext context, ParameterSyntax p, ITypeSymbol? typeSymbol, IParameterSymbol? parameterSymbol)
    {
        var isKernel = parameterSymbol?.ContainingSymbol is IMethodSymbol method &&
                       method.HasAttribute(KnownAttributes.GpuKernel);
        var isRefPointer = parameterSymbol is not null
            ? parameterSymbol.RefKind is RefKind.Ref or RefKind.Out ||
              (!isKernel && parameterSymbol.RefKind is RefKind.In or RefKind.RefReadOnlyParameter)
            : p.Modifiers.Any(SyntaxKind.RefKeyword) ||
              p.Modifiers.Any(SyntaxKind.OutKeyword) ||
              (!isKernel && p.Modifiers.Any(SyntaxKind.InKeyword));
        var isPointerType = typeSymbol is IPointerTypeSymbol;

        var isReadOnly = parameterSymbol != null && parameterSymbol.HasAttribute(KnownAttributes.GpuReadOnly);
        var isArray = typeSymbol.TryGetArrayLikeElementType(out var elementType);

        if (typeSymbol.IsSharedMemoryType() && typeSymbol is INamedTypeSymbol { TypeArguments.Length: 1 } namedType)
        {
            AppendType(ref context, namedType.TypeArguments[0]);
            context.Emitter.Writer.Append("*");
            isPointerType = true;
        }
        else if (isArray)
        {
            context.Emitter.Writer.Append("GSharpArray<");
            if (isReadOnly)
            {
                context.Emitter.Writer.Append("const ");
            }

            AppendType(ref context, elementType);
            context.Emitter.Writer.Append(">");
        }
        else
        {
            AppendType(ref context, typeSymbol);
        }

        if (isRefPointer)
        {
            context.Emitter.Writer.Append("*");
        }

        return (isRefPointer, isPointerType, false);
    }
}
