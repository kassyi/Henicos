
namespace Kassyi.GSharp.Compiler.Dialects;

public abstract class YamlDialectBase : ITranspilerDialect
{
    public virtual IEnumerable<string> RequiredHeaders => Array.Empty<string>();
    public virtual string StructPrefix => string.Empty;
    public virtual string MethodPrefix => string.Empty;
    public virtual bool IsCuda => GetType().Name.Contains("Cuda");

    public virtual DialectCapabilities Capabilities =>
        DialectCapabilities.AllGpuFeatures;

    public virtual void AppendType(ref TranspileContext context, ITypeSymbol? typeSymbol)
    {
        typeSymbol = typeSymbol.UnwrapNullableArrayLike();

        if (typeSymbol == null)
        {
            context.Emitter.Writer.Append("void");
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

        if (TryMapType(GetTypeMappingKey(typeSymbol), out var mappedType))
        {
            context.Emitter.Writer.Append(mappedType);
            return;
        }

        context.Emitter.Writer.AppendFullyQualifiedCppName(typeSymbol);

        if (typeSymbol is not INamedTypeSymbol { IsGenericType: true } namedType)
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
        var isRefPointer = p.Modifiers.Any(SyntaxKind.RefKeyword) || p.Modifiers.Any(SyntaxKind.OutKeyword);
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
            context.Emitter.Writer.Append("* __restrict__ ");
        }

        return (isRefPointer, isPointerType, isRefPointer);
    }
}
