namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The kind of <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>MemoryExtensions</c>, or <c>System.Array</c>
/// method call classified by <see cref="SpanWriteClassifier"/> (#731).
/// </summary>
internal enum SpanMemberKind
{
    /// <summary>Not a method of <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>MemoryExtensions</c>, or <c>System.Array</c>.</summary>
    None,

    /// <summary>A method on one of the span or array types that G# does not map in device code (reported as GSH0003).</summary>
    Unsupported,

    /// <summary><c>Span&lt;T&gt;.Fill(T)</c>.</summary>
    Fill,

    /// <summary><c>Span&lt;T&gt;.Clear()</c>.</summary>
    Clear,

    /// <summary><c>Span&lt;T&gt;.CopyTo(Span&lt;T&gt;)</c>, <c>ReadOnlySpan&lt;T&gt;.CopyTo(Span&lt;T&gt;)</c>, or <c>MemoryExtensions.CopyTo&lt;T&gt;(T[], Span&lt;T&gt;)</c>.</summary>
    CopyTo,

    /// <summary><c>Span&lt;T&gt;.TryCopyTo(Span&lt;T&gt;)</c> or <c>ReadOnlySpan&lt;T&gt;.TryCopyTo(Span&lt;T&gt;)</c>.</summary>
    TryCopyTo,

    /// <summary><c>MemoryExtensions.Reverse&lt;T&gt;(Span&lt;T&gt;)</c> or <c>MemoryExtensions.Reverse&lt;T&gt;(T[])</c>.</summary>
    Reverse,

    /// <summary><c>Span&lt;T&gt;.Slice(int)</c> or <c>ReadOnlySpan&lt;T&gt;.Slice(int)</c>.</summary>
    Slice1,

    /// <summary><c>Span&lt;T&gt;.Slice(int, int)</c> or <c>ReadOnlySpan&lt;T&gt;.Slice(int, int)</c>.</summary>
    Slice2,

    /// <summary><c>MemoryExtensions.AsSpan&lt;T&gt;(T[])</c>.</summary>
    AsSpan0,

    /// <summary><c>MemoryExtensions.AsSpan&lt;T&gt;(T[], int)</c>.</summary>
    AsSpan1,

    /// <summary><c>MemoryExtensions.AsSpan&lt;T&gt;(T[], int, int)</c>.</summary>
    AsSpan2,

    /// <summary><c>Array.Fill&lt;T&gt;(T[], T)</c>.</summary>
    ArrayFill2,

    /// <summary><c>Array.Fill&lt;T&gt;(T[], T, int, int)</c>.</summary>
    ArrayFill4,

    /// <summary><c>Array.Clear(Array)</c>.</summary>
    ArrayClear1,

    /// <summary><c>Array.Clear(Array, int, int)</c>.</summary>
    ArrayClear3,

    /// <summary><c>Array.Reverse(Array)</c> or <c>Array.Reverse&lt;T&gt;(T[])</c>.</summary>
    ArrayReverse1,

    /// <summary><c>Array.Reverse(Array, int, int)</c> or <c>Array.Reverse&lt;T&gt;(T[], int, int)</c>.</summary>
    ArrayReverse3,

    /// <summary><c>Array.Copy(Array, Array, int)</c>.</summary>
    ArrayCopy3,

    /// <summary><c>Array.Copy(Array, int, Array, int, int)</c>.</summary>
    ArrayCopy5,

    /// <summary><c>MemoryExtensions.Sort</c>, <c>Array.Sort&lt;T&gt;(T[])</c>, or <c>Array.Sort&lt;TKey, TValue&gt;(TKey[], TValue[])</c>.</summary>
    Sort,
}

/// <summary>
/// The kind of <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, or <c>System.Array</c> property classified by
/// <see cref="SpanWriteClassifier"/> (#731).
/// </summary>
internal enum SpanPropertyKind
{
    /// <summary>Not a property of <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, or <c>System.Array</c>.</summary>
    None,

    /// <summary>A property on one of the span or array types that G# does not map in device code (reported as GSH0003).</summary>
    Unsupported,

    /// <summary><c>Span&lt;T&gt;.Length</c>, <c>ReadOnlySpan&lt;T&gt;.Length</c>, or <c>Array.Length</c>.</summary>
    Length,

    /// <summary><c>Span&lt;T&gt;.IsEmpty</c> or <c>ReadOnlySpan&lt;T&gt;.IsEmpty</c>.</summary>
    IsEmpty,

    /// <summary><c>Span&lt;T&gt;.Empty</c> or <c>ReadOnlySpan&lt;T&gt;.Empty</c>.</summary>
    Empty,
}

/// <summary>
/// Classifies <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>MemoryExtensions</c>, and <c>System.Array</c>
/// methods and properties by their Roslyn symbols (#731). Any member of those four types outside the allowlist is
/// classified as <c>Unsupported</c> so the transpiler and analyzer report <c>GSH0003</c> rather than emitting an
/// uncompilable C++ member call.
/// </summary>
internal static class SpanWriteClassifier
{
    /// <summary>Classifies an ordinary or reduced-extension method symbol.</summary>
    public static SpanMemberKind ClassifyMethod(IMethodSymbol? method)
    {
        if (method is null || method.MethodKind is not (MethodKind.Ordinary or MethodKind.ReducedExtension))
        {
            return SpanMemberKind.None;
        }

        var original = (method.ReducedFrom ?? method).OriginalDefinition;
        var owner = original.ContainingType;
        if (owner is null)
        {
            return SpanMemberKind.None;
        }

        if (IsWritableSpan(owner))
        {
            return ClassifyWritableSpanMethod(original);
        }

        if (IsSpan(owner))
        {
            return ClassifyReadOnlySpanMethod(original);
        }

        if (IsMemoryExtensions(owner))
        {
            return ClassifyMemoryExtensionsMethod(original);
        }

        if (owner.SpecialType == SpecialType.System_Array)
        {
            return ClassifySystemArrayMethod(original);
        }

        return SpanMemberKind.None;
    }

    /// <summary>Classifies a property symbol on <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, or <c>System.Array</c>.</summary>
    public static SpanPropertyKind ClassifyProperty(IPropertySymbol? property)
    {
        if (property?.ContainingType is not { } owner)
        {
            return SpanPropertyKind.None;
        }

        if (IsSpan(owner))
        {
            return property.Name switch
            {
                "Length" when !property.IsStatic => SpanPropertyKind.Length,
                "IsEmpty" when !property.IsStatic => SpanPropertyKind.IsEmpty,
                "Empty" when property.IsStatic => SpanPropertyKind.Empty,
                _ => SpanPropertyKind.Unsupported,
            };
        }

        if (owner.SpecialType == SpecialType.System_Array)
        {
            return property is { Name: "Length", IsStatic: false }
                ? SpanPropertyKind.Length
                : SpanPropertyKind.Unsupported;
        }

        return SpanPropertyKind.None;
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a pure span/array view operation (<c>Slice</c> or <c>AsSpan</c>) that does not
    /// mutate memory.
    /// </summary>
    public static bool IsPureViewMethod(IMethodSymbol method) =>
        ClassifyMethod(method) is
            SpanMemberKind.Slice1 or
            SpanMemberKind.Slice2 or
            SpanMemberKind.AsSpan0 or
            SpanMemberKind.AsSpan1 or
            SpanMemberKind.AsSpan2;

    /// <summary>
    /// Resolves a method symbol from <paramref name="node"/>, falling back to the first overload candidate when a method
    /// group has multiple overloads.
    /// </summary>
    public static IMethodSymbol? TryGetMethodSymbol(SyntaxNode node, SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbol(node) is IMethodSymbol symbol)
        {
            return symbol;
        }

        if (semanticModel.GetOriginalNode(node) is not { } original)
        {
            return null;
        }

        var targetModel = original.SyntaxTree == semanticModel.SyntaxTree
            ? semanticModel
            : semanticModel.Compilation.GetCachedSemanticModel(original.SyntaxTree);
        var info = targetModel.GetSymbolInfo(original);
        return info.CandidateSymbols is [IMethodSymbol candidate, ..] ? candidate : null;
    }

    private static bool IsMemoryExtensions(INamedTypeSymbol type) =>
        type.Name == TypeConstants.MemoryExtensionsType &&
        type.ContainingNamespace.IsSystemNamespace();

    private static SpanMemberKind ClassifyWritableSpanMethod(IMethodSymbol original)
    {
        if (original.IsStatic || original.TypeParameters.Length != 0)
        {
            return SpanMemberKind.Unsupported;
        }

        var parameters = original.Parameters;
        return original.Name switch
        {
            "Fill" when parameters.Length == 1 && parameters[0].RefKind == RefKind.None => SpanMemberKind.Fill,
            "Clear" when parameters.Length == 0 => SpanMemberKind.Clear,
            "CopyTo" when parameters.Length == 1 &&
                          parameters[0].RefKind == RefKind.None &&
                          IsWritableSpan(parameters[0].Type) => SpanMemberKind.CopyTo,
            "TryCopyTo" when parameters.Length == 1 &&
                             parameters[0].RefKind == RefKind.None &&
                             IsWritableSpan(parameters[0].Type) => SpanMemberKind.TryCopyTo,
            "Slice" when parameters.Length == 1 && IsInt32Value(parameters[0]) => SpanMemberKind.Slice1,
            "Slice" when parameters.Length == 2 &&
                         IsInt32Value(parameters[0]) &&
                         IsInt32Value(parameters[1]) => SpanMemberKind.Slice2,
            _ => SpanMemberKind.Unsupported,
        };
    }

    private static SpanMemberKind ClassifyReadOnlySpanMethod(IMethodSymbol original)
    {
        if (original.IsStatic || original.TypeParameters.Length != 0)
        {
            return SpanMemberKind.Unsupported;
        }

        var parameters = original.Parameters;
        return original.Name switch
        {
            "CopyTo" when parameters.Length == 1 &&
                          parameters[0].RefKind == RefKind.None &&
                          IsWritableSpan(parameters[0].Type) => SpanMemberKind.CopyTo,
            "TryCopyTo" when parameters.Length == 1 &&
                             parameters[0].RefKind == RefKind.None &&
                             IsWritableSpan(parameters[0].Type) => SpanMemberKind.TryCopyTo,
            "Slice" when parameters.Length == 1 && IsInt32Value(parameters[0]) => SpanMemberKind.Slice1,
            "Slice" when parameters.Length == 2 &&
                         IsInt32Value(parameters[0]) &&
                         IsInt32Value(parameters[1]) => SpanMemberKind.Slice2,
            _ => SpanMemberKind.Unsupported,
        };
    }

    private static SpanMemberKind ClassifyMemoryExtensionsMethod(IMethodSymbol original)
    {
        if (!original.IsStatic)
        {
            return SpanMemberKind.Unsupported;
        }

        if (original.Name == "Sort")
        {
            return SpanMemberKind.Sort;
        }

        var parameters = original.Parameters;
        switch (original.Name)
        {
            case "Reverse" when original.TypeParameters.Length == 1 &&
                                parameters.Length == 1 &&
                                parameters[0].RefKind == RefKind.None &&
                                (IsWritableSpan(parameters[0].Type) || IsRank1Array(parameters[0].Type)):
                return SpanMemberKind.Reverse;

            case "CopyTo" when original.TypeParameters.Length == 1 &&
                               parameters.Length == 2 &&
                               parameters[0].RefKind == RefKind.None &&
                               IsRank1Array(parameters[0].Type) &&
                               parameters[1].RefKind == RefKind.None &&
                               IsWritableSpan(parameters[1].Type):
                return SpanMemberKind.CopyTo;

            case "AsSpan" when original.TypeParameters.Length == 1 &&
                               parameters.Length >= 1 &&
                               parameters[0].RefKind == RefKind.None &&
                               IsRank1Array(parameters[0].Type):
                return parameters.Length switch
                {
                    1 => SpanMemberKind.AsSpan0,
                    2 when IsInt32Value(parameters[1]) => SpanMemberKind.AsSpan1,
                    3 when IsInt32Value(parameters[1]) && IsInt32Value(parameters[2]) => SpanMemberKind.AsSpan2,
                    _ => SpanMemberKind.Unsupported,
                };

            default:
                return SpanMemberKind.Unsupported;
        }
    }

    private static SpanMemberKind ClassifySystemArrayMethod(IMethodSymbol original)
    {
        if (!original.IsStatic)
        {
            return SpanMemberKind.Unsupported;
        }

        var parameters = original.Parameters;
        switch (original.Name)
        {
            case "Sort":
                // Only Array.Sort<T>(T[]) and Array.Sort<TKey, TValue>(TKey[], TValue[]) are supported (#731 Q1=a).
                if (original.TypeParameters.Length == 1 &&
                    parameters.Length == 1 &&
                    parameters[0].RefKind == RefKind.None &&
                    IsRank1Array(parameters[0].Type))
                {
                    return SpanMemberKind.Sort;
                }

                if (original.TypeParameters.Length == 2 &&
                    parameters.Length == 2 &&
                    parameters[0].RefKind == RefKind.None &&
                    IsRank1Array(parameters[0].Type) &&
                    parameters[1].RefKind == RefKind.None &&
                    IsRank1Array(parameters[1].Type))
                {
                    return SpanMemberKind.Sort;
                }

                return SpanMemberKind.Unsupported;

            case "Fill" when original.TypeParameters.Length == 1 &&
                             parameters.Length >= 2 &&
                             parameters[0].RefKind == RefKind.None &&
                             IsRank1Array(parameters[0].Type) &&
                             parameters[1].RefKind == RefKind.None:
                return parameters.Length switch
                {
                    2 => SpanMemberKind.ArrayFill2,
                    4 when IsInt32Value(parameters[2]) && IsInt32Value(parameters[3]) => SpanMemberKind.ArrayFill4,
                    _ => SpanMemberKind.Unsupported,
                };

            case "Clear" when original.TypeParameters.Length == 0 &&
                              parameters.Length >= 1 &&
                              parameters[0].RefKind == RefKind.None &&
                              parameters[0].Type.SpecialType == SpecialType.System_Array:
                return parameters.Length switch
                {
                    1 => SpanMemberKind.ArrayClear1,
                    3 when IsInt32Value(parameters[1]) && IsInt32Value(parameters[2]) => SpanMemberKind.ArrayClear3,
                    _ => SpanMemberKind.Unsupported,
                };

            case "Reverse" when original.TypeParameters.Length <= 1 &&
                                parameters.Length >= 1 &&
                                parameters[0].RefKind == RefKind.None &&
                                (IsRank1Array(parameters[0].Type) || parameters[0].Type.SpecialType == SpecialType.System_Array):
                return parameters.Length switch
                {
                    1 => SpanMemberKind.ArrayReverse1,
                    3 when IsInt32Value(parameters[1]) && IsInt32Value(parameters[2]) => SpanMemberKind.ArrayReverse3,
                    _ => SpanMemberKind.Unsupported,
                };

            case "Copy" when original.TypeParameters.Length == 0:
                if (parameters.Length == 3 &&
                    parameters[0].RefKind == RefKind.None &&
                    parameters[0].Type.SpecialType == SpecialType.System_Array &&
                    parameters[1].RefKind == RefKind.None &&
                    parameters[1].Type.SpecialType == SpecialType.System_Array &&
                    IsInt32Value(parameters[2]))
                {
                    return SpanMemberKind.ArrayCopy3;
                }

                if (parameters.Length == 5 &&
                    parameters[0].RefKind == RefKind.None &&
                    parameters[0].Type.SpecialType == SpecialType.System_Array &&
                    IsInt32Value(parameters[1]) &&
                    parameters[2].RefKind == RefKind.None &&
                    parameters[2].Type.SpecialType == SpecialType.System_Array &&
                    IsInt32Value(parameters[3]) &&
                    IsInt32Value(parameters[4]))
                {
                    return SpanMemberKind.ArrayCopy5;
                }

                return SpanMemberKind.Unsupported;

            default:
                return SpanMemberKind.Unsupported;
        }
    }

    private static bool IsInt32Value(IParameterSymbol parameter) =>
        parameter.RefKind == RefKind.None &&
        parameter.Type.SpecialType == SpecialType.System_Int32;

    private static bool IsRank1Array(ITypeSymbol type) =>
        type is IArrayTypeSymbol { Rank: 1 };

    private static bool IsWritableSpan(ITypeSymbol? type) =>
        type is INamedTypeSymbol { IsGenericType: true, Name: TypeConstants.SpanType } named &&
        named.ContainingNamespace.IsSystemNamespace();

    private static bool IsSpan(ITypeSymbol? type) =>
        type is INamedTypeSymbol { IsGenericType: true, Name: TypeConstants.SpanType or TypeConstants.ReadOnlySpanType } named &&
        named.ContainingNamespace.IsSystemNamespace();

    /// <summary>
    /// Formats a diagnostic description for an unsupported method on a classified receiver type.
    /// </summary>
    public static string FormatUnsupportedMethod(IMethodSymbol symbol) =>
        $"Method '{symbol.ContainingType.Name}.{symbol.Name}'";

    /// <summary>
    /// Formats a diagnostic description for an unsupported property on a classified receiver type.
    /// </summary>
    public static string FormatUnsupportedProperty(IPropertySymbol symbol) =>
        $"Property '{symbol.ContainingType.Name}.{symbol.Name}'";
}
