namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Emitter for scalar local variables and input initialization expressions.
/// </summary>
internal static class FuzzScalarLocalEmitter
{
    private const string EnumDeclarationsBlock =
        """
        public enum FuzzEnumByte : byte { Zero = 0, One = 1, Two = 2, Bit = 0x10, High = 0x80, Mask = 0x55, Alt = 0xAA, Edge = byte.MaxValue }
        public enum FuzzEnumShort : short { Zero = 0, One = 1, Two = 2, Bit = 0x0100, High = -256, Mask = 0x1234, Alt = -1, Edge = short.MinValue }
        public enum FuzzEnumInt : int { Zero = 0, One = 1, Two = 2, Bit = 0x00010000, High = -65536, Mask = 0x13579BDF, Alt = -1, Edge = int.MinValue }
        public enum FuzzEnumULong : ulong { Zero = 0UL, One = 1UL, Two = 2UL, Bit = 0x0000000100000000UL, High = 0x8000000000000000UL, Mask = 0x5555555555555555UL, Alt = 0xAAAAAAAAAAAAAAAAUL, Edge = ulong.MaxValue }
        """;

    /// <summary>Appends the device enum declarations used by generated kernels to <paramref name="writer"/>.</summary>
    public static void AppendEnumDeclarations(SourceWriter writer) =>
        writer.AppendLine(EnumDeclarationsBlock);

    /// <summary>Initializes scalar local variables from kernel inputs.</summary>
    internal static void InitializeScalarLocals(KernelGenerationState state)
    {
        FuzzScalarKind[] baseTypes =
        [
            FuzzScalarKind.SByte,
            FuzzScalarKind.Byte,
            FuzzScalarKind.Int16,
            FuzzScalarKind.UInt16,
            FuzzScalarKind.Int32,
            FuzzScalarKind.UInt32,
            FuzzScalarKind.Int64,
            FuzzScalarKind.UInt64,
            FuzzScalarKind.Boolean,
            FuzzScalarKind.Char,
            FuzzScalarKind.EnumByte,
            FuzzScalarKind.EnumShort,
            FuzzScalarKind.EnumInt,
            FuzzScalarKind.EnumULong,
        ];

        var activeTypes = new List<FuzzTypeInfo>(baseTypes.Length);
        foreach (var kind in baseTypes)
        {
            activeTypes.Add(FuzzTypeInfo.Get(kind));
        }

        for (int i = activeTypes.Count - 1; i > 0; i--)
        {
            int j = state.Rng.NextBelow(i + 1);
            (activeTypes[i], activeTypes[j]) = (activeTypes[j], activeTypes[i]);
        }

        foreach (var typeInfo in activeTypes)
        {
            string varName = $"v{state.Locals.Count}";
            string initExpr = BuildInputInitializationExpression(state.Rng, typeInfo, state.Locals.Count);
            state.Statements.Add($"{typeInfo.Keyword} {varName} = {initExpr};");
            state.Locals.Add(new ScalarLocalVariable(varName, typeInfo));
        }
    }

    private static string BuildInputInitializationExpression(FuzzRandom rng, FuzzTypeInfo type, int index)
    {
        string src = (index % 3) switch { 0 => "inA[gid]", 1 => "inB[gid]", _ => "inC[gid]" };
        string alt = ((index + 1) % 3) switch { 0 => "inA[gid]", 1 => "inB[gid]", _ => "inC[gid]" };

        return type.Kind switch
        {
            FuzzScalarKind.SByte => $"(sbyte){src}",
            FuzzScalarKind.Byte => $"(byte){src}",
            FuzzScalarKind.Int16 => $"(short){src}",
            FuzzScalarKind.UInt16 => $"(ushort){src}",
            FuzzScalarKind.Int32 => src,
            FuzzScalarKind.UInt32 => $"(uint){src}",
            FuzzScalarKind.Int64 => rng.NextBool() ? $"(((long){src} << 32) ^ (uint){alt})" : $"(long){src}",
            FuzzScalarKind.UInt64 => rng.NextBool() ? $"(((ulong)(uint){src} << 32) | (uint){alt})" : $"(ulong){src}",
            FuzzScalarKind.Boolean => $"((({src} ^ {alt}) & 1) != 0)",
            FuzzScalarKind.Char => $"(char){src}",
            FuzzScalarKind.EnumByte => $"(FuzzEnumByte)(byte){src}",
            FuzzScalarKind.EnumShort => $"(FuzzEnumShort)(short){src}",
            FuzzScalarKind.EnumInt => $"(FuzzEnumInt){src}",
            FuzzScalarKind.EnumULong => $"(FuzzEnumULong)(ulong){src}",
            _ => src,
        };
    }
}
