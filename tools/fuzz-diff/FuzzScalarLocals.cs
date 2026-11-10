namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Declares the scalar locals every generated kernel starts with: one local per scalar kind, in a random order, each
/// read from the thread's own input elements.
/// </summary>
internal static class FuzzScalarLocals
{
    /// <summary>Adds the declarations of the scalar locals to <paramref name="state"/>.</summary>
    public static void Initialize(KernelGenerationState state)
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
