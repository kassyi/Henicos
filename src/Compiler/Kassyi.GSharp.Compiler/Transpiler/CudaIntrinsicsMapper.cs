namespace Kassyi.GSharp.Compiler.Transpiler;

internal static class CudaIntrinsicsMapper
{
    public static bool TryMap(ISymbol symbol, out string mappedName)
    {
        // 1. Check for CudaIntrinsicAttribute on the member itself
        var intrinsicAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CudaIntrinsicAttribute");
        if (intrinsicAttr != null && intrinsicAttr.ConstructorArguments.Length > 0)
        {
            mappedName = intrinsicAttr.ConstructorArguments[0].Value?.ToString() ?? symbol.Name;
            return true;
        }

        // 2. Check for CudaIntrinsicPatternAttribute on the containing type
        if (symbol.ContainingType != null)
        {
            var patternAttr = symbol.ContainingType.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "CudaIntrinsicPatternAttribute");
            if (patternAttr != null && patternAttr.ConstructorArguments.Length > 0)
            {
                var pattern = patternAttr.ConstructorArguments[0].Value?.ToString() ?? "{0}";
                mappedName = string.Format(pattern, symbol.Name.ToLowerInvariant());
                return true;
            }

            // Hardcoded external mappings (like System.Math) which cannot be annotated
            if (symbol.ContainingType.Name == "Math")
            {
                mappedName = symbol.Name.ToLowerInvariant();
                return true;
            }
        }

        mappedName = string.Empty;
        return false;
    }
}
