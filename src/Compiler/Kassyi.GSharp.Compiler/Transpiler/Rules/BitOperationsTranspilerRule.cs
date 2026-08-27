namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

internal sealed class BitOperationsTranspilerRule : IInvocationTranspilerRule
{
    public static readonly BitOperationsTranspilerRule Instance = new();

    private BitOperationsTranspilerRule()
    {
    }

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (symbol.ContainingType.Name != "BitOperations" ||
            symbol.ContainingType.ToString() != "System.Numerics.BitOperations")
        {
            return false;
        }

        var is64Bit = false;
        if (symbol.Parameters.Length > 0)
        {
            var paramType = symbol.Parameters[0].Type;
            if (paramType.TypeKind == TypeKind.Enum)
            {
                paramType = ((INamedTypeSymbol)paramType).EnumUnderlyingType ?? paramType;
            }

            var platform = context.SemanticModel.Compilation.Options.Platform;
            var is64BitPointer = platform == Platform.X64 || platform == Platform.Arm64 ||
                                 platform == Platform.Itanium || platform == Platform.AnyCpu;

            is64Bit = paramType.SpecialType == SpecialType.System_UInt64 ||
                      paramType.SpecialType == SpecialType.System_Int64 ||
                      (is64BitPointer &&
                       (paramType.SpecialType == SpecialType.System_UIntPtr ||
                        paramType.SpecialType == SpecialType.System_IntPtr ||
                        paramType.Name == "nuint" ||
                        paramType.Name == "nint"));
        }

        var functionName = symbol.Name switch
        {
            "LeadingZeroCount" => is64Bit ? "__gsharp_lzc_64" : "__gsharp_lzc_32",
            "PopCount" => is64Bit ? "__popcll" : "__popc",
            "TrailingZeroCount" => is64Bit ? "__gsharp_tzc_64" : "__gsharp_tzc_32",
            "Log2" => is64Bit ? "__gsharp_log2_64" : "__gsharp_log2_32",
            "RotateLeft" => is64Bit ? "__gsharp_rotl64" : "__gsharp_rotl32",
            "RotateRight" => is64Bit ? "__gsharp_rotr64" : "__gsharp_rotr32",
            _ => throw new InvalidOperationException($"Unsupported BitOperations method: {symbol.Name}")
        };

        context.Writer.Append(functionName);
        using (context.Writer.Parentheses())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                if (symbol.Name == "PopCount" && i == 0)
                {
                    using (context.Writer.Parentheses())
                    {
                        context.Writer.AppendKeyword("unsigned");
                        context.Writer.AppendSpace();
                        if (is64Bit)
                        {
                            context.Writer.AppendKeyword("long");
                            context.Writer.AppendSpace();
                            context.Writer.AppendKeyword("long");
                        }
                        else
                        {
                            context.Writer.AppendKeyword("int");
                        }
                    }

                    using (context.Writer.Parentheses())
                    {
                        context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                    }
                }
                else
                {
                    context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                }

                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }

        return true;
    }
}
