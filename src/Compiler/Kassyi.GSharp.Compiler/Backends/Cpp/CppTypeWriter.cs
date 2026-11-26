namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Emits C++ type spellings, storage types (<c>SharedMemory&lt;T&gt;</c> as <c>T*</c>), and parameter signatures
/// without depending on <c>TranspileContext</c> or syntax nodes.
/// </summary>
internal static class CppTypeWriter
{
    /// <summary>
    /// Emits the target C++ type representation for <paramref name="typeSymbol"/> into <paramref name="context"/>.
    /// </summary>
    public static void AppendType<TServices>(in CppTypeContext<TServices> context, ITypeSymbol? typeSymbol)
        where TServices : struct, ICppTypeServices
    {
        typeSymbol = context.Services.SubstituteTypeParameters(typeSymbol);

        if (typeSymbol is null)
        {
            context.Writer.Append("void");
            return;
        }

        if (typeSymbol is ITypeParameterSymbol typeParam)
        {
            ReportUnsubstitutedTypeParameter(in context, typeParam);
            return;
        }

        if (typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } enumUnderlyingType })
        {
            typeSymbol = enumUnderlyingType;
        }

        if (TryAppendSpecialType(in context, typeSymbol))
        {
            return;
        }

        context.Services.RegisterUsedStruct(typeSymbol);

        if (typeSymbol is INamedTypeSymbol userType &&
            context.Services.TryGetUserTypeName(userType) is { } userTypeName)
        {
            context.Writer.Append(userTypeName);
            return;
        }

        context.Writer.AppendFullyQualifiedCppName(typeSymbol, tryGetUserTypeName: null);

        if (typeSymbol is not INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return;
        }

        AppendGenericArguments(in context, namedType);
    }

    /// <summary>
    /// Appends the C++ storage type for <paramref name="typeSymbol"/>: <c>T*</c> when
    /// <paramref name="typeSymbol"/> is <c>SharedMemory&lt;T&gt;</c>, or <see cref="AppendType{TServices}"/> otherwise.
    /// </summary>
    public static void AppendStorageType<TServices>(in CppTypeContext<TServices> context, ITypeSymbol? typeSymbol)
        where TServices : struct, ICppTypeServices
    {
        if (typeSymbol.IsSharedMemoryType() &&
            typeSymbol is INamedTypeSymbol { TypeArguments.Length: 1 } sharedMemory)
        {
            AppendType(in context, sharedMemory.TypeArguments[0]);
            context.Writer.Append('*');
            return;
        }

        AppendType(in context, typeSymbol);
    }

    /// <summary>
    /// Emits the target C++ parameter type: an array as <c>GSharpArray&lt;T&gt;</c> (<c>const T</c> with
    /// <c>[GpuReadOnly]</c>), a <c>SharedMemory&lt;T&gt;</c> as <c>T*</c>, and a pointer for <c>ref</c> / <c>out</c>
    /// (and <c>in</c> outside kernels or when <paramref name="isPassedByAddress"/> holds).
    /// </summary>
    public static void AppendParameter<TServices>(
        in CppTypeContext<TServices> context,
        ITypeSymbol? typeSymbol,
        IParameterSymbol? parameterSymbol,
        bool isPassedByAddress)
        where TServices : struct, ICppTypeServices
    {
        var isKernel = parameterSymbol?.ContainingSymbol is IMethodSymbol method &&
                       method.HasAttribute(KnownAttributes.GpuKernel);
        var isRefPointer = parameterSymbol is not null
            ? (isKernel
                ? parameterSymbol.RefKind is RefKind.Ref or RefKind.Out || isPassedByAddress
                : parameterSymbol.IsPassedByPointer(isKernel: false))
            : isPassedByAddress;

        var isReadOnly = parameterSymbol is not null && parameterSymbol.HasAttribute(KnownAttributes.GpuReadOnly);
        var isArray = typeSymbol.TryGetArrayLikeElementType(out var elementType);

        if (typeSymbol.IsSharedMemoryType())
        {
            AppendStorageType(in context, typeSymbol);
        }
        else if (isArray)
        {
            context.Writer.Append("GSharpArray<");
            if (isReadOnly)
            {
                context.Writer.Append("const ");
            }

            AppendType(in context, elementType);
            context.Writer.Append(">");
        }
        else
        {
            AppendType(in context, typeSymbol);
        }

        if (isRefPointer)
        {
            context.Writer.Append("*");
        }
    }

    private static void ReportUnsubstitutedTypeParameter<TServices>(
        in CppTypeContext<TServices> context,
        ITypeParameterSymbol typeParam)
        where TServices : struct, ICppTypeServices
    {
        context.Services.ReportUnsubstitutedTypeParameter(typeParam);
        context.Writer.Append("/*unsupported:TypeParameter*/");
    }

    // The forms written without the type's C++ name: arrays, delegates, pointers, WMMA fragments, dialect-mapped types
    private static bool TryAppendSpecialType<TServices>(in CppTypeContext<TServices> context, ITypeSymbol typeSymbol)
        where TServices : struct, ICppTypeServices
    {
        if (typeSymbol.TryGetArrayLikeElementType(out var elementType))
        {
            context.Writer.Append("GSharpArray<");
            AppendType(in context, elementType);
            context.Writer.Append(">");
            return true;
        }

        if (context.Dialect.IsCuda && typeSymbol.TypeKind == TypeKind.Delegate)
        {
            context.Writer.Append("auto");
            return true;
        }

        if (TryAppendParameterlessAction(in context, typeSymbol) || TryAppendDelegateType(in context, typeSymbol))
        {
            return true;
        }

        if (typeSymbol is IFunctionPointerTypeSymbol)
        {
            // [WHY] Function pointers have no type-then-name C++ spelling and host pointers are invalid on the device.
            // Declaration sites report GSH0003; this placeholder only keeps the emitted signature well-formed.
            context.Writer.Append("void*");
            return true;
        }

        if (typeSymbol is IPointerTypeSymbol pointer)
        {
            AppendType(in context, pointer.PointedAtType);
            context.Writer.Append("*");
            return true;
        }

        return TryAppendWmmaType(in context, typeSymbol) ||
               TryAppendMappedType(in context, typeSymbol);
    }

    private static bool TryAppendWmmaType<TServices>(in CppTypeContext<TServices> context, ITypeSymbol typeSymbol)
        where TServices : struct, ICppTypeServices
    {
        if (typeSymbol.IsWmmaMarkerType())
        {
            if (WmmaTypeSyntaxHelper.TryGetShapeDimensions(typeSymbol, out var sm, out var sn, out var sk))
            {
                context.Writer.Append($"{GSharpInternalNames.Wmma.Shape}<{sm}, {sn}, {sk}>");
                return true;
            }

            if (WmmaTypeSyntaxHelper.TryGetLayoutCppName(typeSymbol, out var layoutName))
            {
                context.Writer.Append(layoutName);
                return true;
            }

            return false;
        }

        if (!typeSymbol.IsWmmaFragmentType() ||
            typeSymbol is not INamedTypeSymbol { TypeArguments.Length: >= 2 } fragmentType)
        {
            return false;
        }

        var role = fragmentType.Name switch
        {
            TypeConstants.WmmaTypes.MatrixA => GSharpInternalNames.Wmma.MatrixA,
            TypeConstants.WmmaTypes.MatrixB => GSharpInternalNames.Wmma.MatrixB,
            _ => GSharpInternalNames.Wmma.Accumulator
        };

        if (!WmmaTypeSyntaxHelper.TryGetShapeDimensions(fragmentType.TypeArguments[0], out var m, out var n, out var k))
        {
            (m, n, k) = (0, 0, 0);
        }

        context.Writer.Append(GSharpInternalNames.Wmma.Fragment);
        using (context.Writer.InlineScope("<", ">"))
        {
            context.Writer.Append($"{role}, {m}, {n}, {k}, ");
            AppendType(in context, fragmentType.TypeArguments[1]);
            context.Writer.Append(", ");
            if (fragmentType.TypeArguments.Length >= 3 &&
                WmmaTypeSyntaxHelper.TryGetLayoutCppName(fragmentType.TypeArguments[2], out var fragLayout))
            {
                context.Writer.Append(fragLayout);
            }
            else
            {
                context.Writer.Append("void");
            }
        }

        return true;
    }

    // System_Action of the Prelude is a variadic alias template, which C++17 names only with its argument list: the
    // Action of System without type arguments is System_Action<>, the alias with an empty argument list
    private static bool TryAppendParameterlessAction<TServices>(
        in CppTypeContext<TServices> context,
        ITypeSymbol typeSymbol)
        where TServices : struct, ICppTypeServices
    {
        if (typeSymbol is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Delegate,
                IsGenericType: false,
                Name: "Action"
            } action ||
            !action.ContainingNamespace.IsSystemNamespace())
        {
            return false;
        }

        context.Writer.Append("System_Action<>");
        return true;
    }

    // A delegate type that is not Func or Action of System (those are the Prelude aliases) is the std::function of its
    // Invoke signature on the CPU: MyFn(int) returning int is std::function<int(int)>. A delegate with a ref, out or in
    // parameter keeps its name.
    private static bool TryAppendDelegateType<TServices>(in CppTypeContext<TServices> context, ITypeSymbol typeSymbol)
        where TServices : struct, ICppTypeServices
    {
        if (typeSymbol is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Delegate,
                DelegateInvokeMethod: { } invoke
            } named ||
            (named.ContainingNamespace.IsSystemNamespace() && named.Name is "Func" or "Action"))
        {
            return false;
        }

        // The signature of the device function that it holds: a ref, out or in parameter is a pointer, a ref result a
        // reference (MethodSignatureTranspiler)
        context.Writer.Append("std::function<");
        context.Writer.AppendIf(invoke.RefKind == RefKind.RefReadOnly, "const ");
        AppendType(in context, invoke.ReturnType);
        context.Writer.AppendIf(invoke.RefKind is RefKind.Ref or RefKind.RefReadOnly, "&");
        using (context.Writer.Parentheses())
        {
            for (var i = 0; i < invoke.Parameters.Length; i++)
            {
                context.Writer.AppendIf(i > 0, ", ");
                AppendType(in context, invoke.Parameters[i].Type);
                context.Writer.AppendIf(invoke.Parameters[i].RefKind != RefKind.None, "*");
            }
        }

        context.Writer.Append(">");
        return true;
    }

    private static bool TryAppendMappedType<TServices>(in CppTypeContext<TServices> context, ITypeSymbol typeSymbol)
        where TServices : struct, ICppTypeServices
    {
        if (!context.Dialect.TryMapType(typeSymbol, out var mappedType))
        {
            return false;
        }

        context.Writer.Append(mappedType);
        return true;
    }

    private static void AppendGenericArguments<TServices>(
        in CppTypeContext<TServices> context,
        INamedTypeSymbol namedType)
        where TServices : struct, ICppTypeServices
    {
        context.Writer.Append("<");
        for (var i = 0; i < namedType.TypeArguments.Length; i++)
        {
            AppendType(in context, namedType.TypeArguments[i]);
            if (i < namedType.TypeArguments.Length - 1)
            {
                context.Writer.Append(", ");
            }
        }

        context.Writer.Append(">");
    }
}
