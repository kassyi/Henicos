namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Maps C# methods decorated with CUDA intrinsic attributes to CUDA C++ function names.</summary>
internal static class CudaIntrinsicsMapper
{
    public static bool TryMap(ISymbol symbol, out string mappedName)
    {
        // 1. Check for CudaIntrinsicAttribute on the member itself
        var intrinsicAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == KnownAttributes.CudaIntrinsicAttribute);
        if (intrinsicAttr is { ConstructorArguments.Length: > 0 })
        {
            mappedName = intrinsicAttr.ConstructorArguments[0].Value?.ToString() ?? symbol.Name;
            return true;
        }

        // 2. Check for CudaIntrinsicPatternAttribute on the containing type
        if (symbol.ContainingType != null)
        {
            var patternAttr = symbol.ContainingType.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == KnownAttributes.CudaIntrinsicPatternAttribute);
            if (patternAttr is { ConstructorArguments.Length: > 0 })
            {
                var pattern = patternAttr.ConstructorArguments[0].Value?.ToString() ?? "{0}";
                mappedName = string.Format(pattern, symbol.Name.ToLowerInvariant());
                return true;
            }

            if (symbol.ContainingNamespace.IsSystemNamespace() &&
                symbol.ContainingType.Name is TypeConstants.MathType or TypeConstants.MathFType)
            {
                var pt = symbol is IMethodSymbol { Parameters.Length: > 0 } methodSymbol
                    ? methodSymbol.Parameters[0].Type.SpecialType
                    : SpecialType.None;

                var isInteger = pt is >= SpecialType.System_SByte and <= SpecialType.System_UInt64;
                var isSingle = pt == SpecialType.System_Single ||
                               symbol.ContainingType.Name == TypeConstants.MathFType;

                var matchedName = symbol.Name switch
                {
                    "Sin" => isSingle ? "sinf" : "sin",
                    "Cos" => isSingle ? "cosf" : "cos",
                    "Max" => isInteger ? "max" : (isSingle ? "fmaxf" : "fmax"),
                    "Min" => isInteger ? "min" : (isSingle ? "fminf" : "fmin"),
                    "Abs" => isInteger ? "abs" : (isSingle ? "fabsf" : "fabs"),
                    "Clamp" => "__gsharp_clamp",
                    _ => null
                };

                if (matchedName != null)
                {
                    mappedName = matchedName;
                    return true;
                }
            }
        }

        mappedName = string.Empty;
        return false;
    }
}
