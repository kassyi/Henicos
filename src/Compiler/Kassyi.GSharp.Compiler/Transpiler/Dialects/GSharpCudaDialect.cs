namespace Kassyi.GSharp.Compiler.Transpiler.Dialects;

public class GSharpCudaDialect : ITranspilerDialect
{
    public IEnumerable<string> RequiredHeaders => new[]
    {
        "#include <cuda_runtime.h>",
        "#include <stdint.h>",
        "#include <math.h>"
    };

    public string StructPrefix => "__device__ ";

    public string MethodPrefix => "__device__ inline ";

    private static readonly (string? CudaName, byte Size)[] s_specialTypes =
        new (string?, byte)[(int)SpecialType.System_Double + 1];

    static GSharpCudaDialect()
    {
        register(SpecialType.System_Void, TypeConstants.CudaTypes.Void, 0);
        register(SpecialType.System_Boolean, TypeConstants.CudaTypes.Bool, 1);
        register(SpecialType.System_Byte, TypeConstants.CudaTypes.UnsignedChar, 1);
        register(SpecialType.System_SByte, TypeConstants.CudaTypes.Char, 1);
        register(SpecialType.System_Int16, TypeConstants.CudaTypes.Short, 2);
        register(SpecialType.System_UInt16, TypeConstants.CudaTypes.UnsignedShort, 2);
        register(SpecialType.System_Int32, TypeConstants.CudaTypes.Int, 4);
        register(SpecialType.System_UInt32, TypeConstants.CudaTypes.UnsignedInt, 4);
        register(SpecialType.System_Int64, TypeConstants.CudaTypes.LongLong, 8);
        register(SpecialType.System_UInt64, TypeConstants.CudaTypes.UnsignedLongLong, 8);
        register(SpecialType.System_Single, TypeConstants.CudaTypes.Float, 4);
        register(SpecialType.System_Double, TypeConstants.CudaTypes.Double, 8);
        return;

        void register(SpecialType type, string cudaName, byte size) =>
            s_specialTypes[(int)type] = (cudaName, size);
    }

    public void AppendType(ref TranspileContext context, ITypeSymbol? typeSymbol)
    {
        switch (typeSymbol)
        {
            case null:
                context.Writer.Append(TypeConstants.CudaTypes.Void);
                return;
            // 1. Indirection Types (Arrays & Pointers)
            case IArrayTypeSymbol array:
                context.AppendType(array.ElementType);
                context.Writer.Append("*");
                return;
            case IPointerTypeSymbol pointer:
                context.AppendType(pointer.PointedAtType);
                context.Writer.Append("*");
                return;
        }

        // 2. Primitive / Special Types (O(1) table lookup)
        if (TryGetSpecialTypeName(typeSymbol.SpecialType, out var specialName))
        {
            context.Writer.Append(specialName);
            return;
        }

        // 3. Named Types (Generics, Framework Types, Vectors, Structs)
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                context.Writer.Append("System_Nullable<");
                context.AppendType(namedType.TypeArguments[0]);
                context.Writer.Append(">");
                return;
            }

            if (namedType.Name == TypeConstants.GpuBigIntRefType)
            {
                context.Writer.Append(TypeConstants.CudaTypes.UnsignedIntPtr);
                return;
            }

            if (TryGetVectorTypeName(namedType, out var vectorName))
            {
                context.Writer.Append(vectorName);
                return;
            }
        }

        // 4. Fallback: User Defined Structs / Custom Types
        context.Writer.AppendFullyQualifiedCppName(typeSymbol);
    }

    private static bool TryGetSpecialTypeName(SpecialType specialType, out string cudaName)
    {
        if ((uint)specialType < (uint)s_specialTypes.Length &&
            s_specialTypes[(int)specialType].CudaName is { } name)
        {
            cudaName = name;
            return true;
        }

        cudaName = string.Empty;
        return false;
    }

    private static bool TryGetVectorTypeName(ITypeSymbol typeSymbol, out string cudaName)
    {
        if (typeSymbol.IsSystemNumericsVector())
        {
            cudaName = typeSymbol.Name switch
            {
                TypeConstants.Vector2Type => TypeConstants.CudaTypes.Float2,
                TypeConstants.Vector3Type => TypeConstants.CudaTypes.Float3,
                TypeConstants.Vector4Type => TypeConstants.CudaTypes.Float4,
                _ => string.Empty
            };
            return cudaName.Length > 0;
        }

        cudaName = string.Empty;
        return false;
    }

    public int GetApproximateSize(ITypeSymbol typeSymbol) => GetSizeAndAlignment(typeSymbol).Size;

    private static (int Size, int Alignment) GetSizeAndAlignment(ITypeSymbol typeSymbol)
    {
        if (TryGetSpecialTypeSize(typeSymbol.SpecialType, out var specialSize))
        {
            return (specialSize, specialSize);
        }

        if (typeSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named)
        {
            return (8, 8); // arrays/pointers
        }

        var currentOffset = 0;
        var maxAlignment = 1;

        foreach (var field in named.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
        {
            var (fieldSize, fieldAlign) = GetSizeAndAlignment(field.Type);

            // Align current offset
            if (currentOffset % fieldAlign != 0)
            {
                currentOffset += fieldAlign - (currentOffset % fieldAlign);
            }

            currentOffset += fieldSize;
            if (fieldAlign > maxAlignment)
            {
                maxAlignment = fieldAlign;
            }
        }

        // Padding at the end of the struct
        if (currentOffset % maxAlignment != 0)
        {
            currentOffset += maxAlignment - (currentOffset % maxAlignment);
        }

        return (currentOffset == 0 ? 1 : currentOffset, maxAlignment);
    }

    private static bool TryGetSpecialTypeSize(SpecialType specialType, out byte size)
    {
        if ((uint)specialType < (uint)s_specialTypes.Length &&
            s_specialTypes[(int)specialType].Size is var s and > 0)
        {
            size = s;
            return true;
        }

        size = 0;
        return false;
    }

    public (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameter(
        ref TranspileContext context, ParameterSyntax p, ITypeSymbol? typeSymbol)
    {
        var isRefPointer = false;
        var isIn = false;
        var isRefOut = false;

        foreach (var m in p.Modifiers)
        {
            if (m.IsKind(SyntaxKind.InKeyword))
            {
                isIn = true;
            }

            if (!m.IsKind(SyntaxKind.RefKeyword) && !m.IsKind(SyntaxKind.OutKeyword))
            {
                continue;
            }

            isRefPointer = true;
            isRefOut = true;
        }

        if (isIn)
        {
            context.Writer.Append("const ");
        }

        var isPointerType = typeSymbol is IPointerTypeSymbol;
        var hasRestrict = false;

        if (typeSymbol is { TypeKind: TypeKind.Array })
        {
            context.AppendType(((IArrayTypeSymbol)typeSymbol).ElementType);
            context.Writer.AppendOperator("*");
            context.Writer.AppendSpace();
            context.Writer.AppendKeyword("__restrict__");
            isPointerType = true;
            hasRestrict = true;
        }
        else
        {
            context.AppendType(typeSymbol);
        }

        if (isIn)
        {
            context.Writer.Append("&");
        }
        else if (isRefOut)
        {
            context.Writer.Append("*");
            isPointerType = true;
        }

        return (isRefPointer, isPointerType, hasRestrict);
    }

    public bool TryMapIntrinsic(ISymbol symbol, out string mappedExpression)
    {
        // 1. Check for CudaIntrinsicAttribute on the member itself
        var intrinsicAttr = symbol.GetAttribute(KnownAttributes.CudaIntrinsic);
        if (intrinsicAttr is { ConstructorArguments.Length: > 0 })
        {
            mappedExpression = intrinsicAttr.ConstructorArguments[0].Value?.ToString() ?? symbol.Name;
            return true;
        }

        // 2. Check for CudaIntrinsicPatternAttribute on the containing type
        if (symbol.ContainingType != null)
        {
            var patternAttr = symbol.ContainingType.GetAttribute(KnownAttributes.CudaIntrinsicPattern);
            if (patternAttr is { ConstructorArguments.Length: > 0 })
            {
                var pattern = patternAttr.ConstructorArguments[0].Value?.ToString() ?? "{0}";
                mappedExpression = string.Format(pattern, symbol.Name.ToLowerInvariant());
                return true;
            }

            if (symbol.ContainingNamespace.IsSystemNamespace() &&
                symbol.ContainingType.Name is TypeConstants.MathType or TypeConstants.MathFType)
            {
                var pt = symbol is IMethodSymbol { Parameters.Length: > 0 } ms
                    ? ms.Parameters[0].Type.SpecialType
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
                    mappedExpression = matchedName;
                    return true;
                }
            }
        }

        mappedExpression = string.Empty;
        return false;
    }

    public CSharpSyntaxRewriter? GetAstPreProcessor(SemanticModel semanticModel) => null;

    public CSharpSyntaxVisitor<bool> GetInterceptorVisitor(ref TranspileContext context)
    {
        // Returning a dummy visitor that never intercepts for now, to keep compilation unbroken during refactoring.
        return new DummyInterceptor();
    }

    private class DummyInterceptor : CSharpSyntaxVisitor<bool>
    {
        public override bool DefaultVisit(SyntaxNode node) => false;
    }
}
