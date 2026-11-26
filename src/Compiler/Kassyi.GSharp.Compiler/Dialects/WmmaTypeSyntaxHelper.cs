namespace Kassyi.GSharp.Compiler.Dialects;

/// <summary>
/// Maps C# WMMA fragment and marker types (<c>WmmaMatrixA</c>, <c>WmmaMatrixB</c>, <c>WmmaAccumulator</c>,
/// <c>IWmmaShape</c>, <c>IWmmaLayout</c>) to their C++ <c>__gsharp_wmma_*</c> spellings.
/// </summary>
internal static class WmmaTypeSyntaxHelper
{
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
