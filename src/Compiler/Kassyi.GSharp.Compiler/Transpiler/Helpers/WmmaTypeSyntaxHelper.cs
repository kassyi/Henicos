namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Maps C# WMMA fragment and marker types (<c>WmmaMatrixA</c>, <c>WmmaMatrixB</c>, <c>WmmaAccumulator</c>,
/// <c>IWmmaShape</c>, <c>IWmmaLayout</c>) to their C++ <c>__gsharp_wmma_*</c> spellings.
/// </summary>
internal static class WmmaTypeSyntaxHelper
{
    /// <summary>
    /// Appends the C++ representation of a WMMA fragment or marker type when <paramref name="typeSymbol"/> is one.
    /// </summary>
    public static bool TryAppendType(ref TranspileContext context, ITypeSymbol typeSymbol)
    {
        if (typeSymbol.IsWmmaMarkerType())
        {
            if (TryGetShapeDimensions(typeSymbol, out var sm, out var sn, out var sk))
            {
                context.Emitter.Writer.Append($"{GSharpInternalNames.Wmma.Shape}<{sm}, {sn}, {sk}>");
                return true;
            }

            if (TryGetLayoutCppName(typeSymbol, out var layoutName))
            {
                context.Emitter.Writer.Append(layoutName);
                return true;
            }

            return false;
        }

        if (!typeSymbol.IsWmmaFragmentType() || typeSymbol is not INamedTypeSymbol { TypeArguments.Length: >= 2 } fragmentType)
        {
            return false;
        }

        var role = fragmentType.Name switch
        {
            TypeConstants.WmmaTypes.MatrixA => GSharpInternalNames.Wmma.MatrixA,
            TypeConstants.WmmaTypes.MatrixB => GSharpInternalNames.Wmma.MatrixB,
            _ => GSharpInternalNames.Wmma.Accumulator
        };

        if (!TryGetShapeDimensions(fragmentType.TypeArguments[0], out var m, out var n, out var k))
        {
            (m, n, k) = (0, 0, 0);
        }

        context.Emitter.Writer.Append(GSharpInternalNames.Wmma.Fragment);
        using (context.Emitter.Writer.InlineScope("<", ">"))
        {
            context.Emitter.Writer.Append($"{role}, {m}, {n}, {k}, ");
            context.Emitter.Dialect.AppendType(ref context, fragmentType.TypeArguments[1]);
            context.Emitter.Writer.Append(", ");
            if (fragmentType.TypeArguments.Length >= 3 &&
                TryGetLayoutCppName(fragmentType.TypeArguments[2], out var fragLayout))
            {
                context.Emitter.Writer.Append(fragLayout);
            }
            else
            {
                context.Emitter.Writer.Append("void");
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts the tile dimensions (<c>m, n, k</c>) from a WMMA shape marker type.
    /// </summary>
    public static bool TryGetShapeDimensions(ITypeSymbol? shapeType, out int m, out int n, out int k)
    {
        if (shapeType is INamedTypeSymbol named && named.ContainingNamespace.IsCoreNamespace())
        {
            switch (named.Name)
            {
                case TypeConstants.WmmaTypes.M16N16K16:
                    (m, n, k) = (16, 16, 16);
                    return true;
                case TypeConstants.WmmaTypes.M32N8K16:
                    (m, n, k) = (32, 8, 16);
                    return true;
                case TypeConstants.WmmaTypes.M8N32K16:
                    (m, n, k) = (8, 32, 16);
                    return true;
                case TypeConstants.WmmaTypes.M8N8K4:
                    (m, n, k) = (8, 8, 4);
                    return true;
            }
        }

        (m, n, k) = (0, 0, 0);
        return false;
    }

    /// <summary>
    /// Resolves the C++ tag name for a WMMA layout marker type (<c>WmmaRowMajor</c> or <c>WmmaColumnMajor</c>).
    /// </summary>
    public static bool TryGetLayoutCppName(ITypeSymbol? layoutType, [NotNullWhen(true)] out string? cppName)
    {
        if (layoutType is INamedTypeSymbol named && named.ContainingNamespace.IsCoreNamespace())
        {
            switch (named.Name)
            {
                case TypeConstants.WmmaTypes.RowMajor:
                    cppName = GSharpInternalNames.Wmma.RowMajor;
                    return true;
                case TypeConstants.WmmaTypes.ColumnMajor:
                    cppName = GSharpInternalNames.Wmma.ColMajor;
                    return true;
            }
        }

        cppName = null;
        return false;
    }
}
