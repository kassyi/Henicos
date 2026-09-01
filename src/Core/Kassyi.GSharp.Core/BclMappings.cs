using Kassyi.GSharp.Core;

// --- System.Numerics.BitOperations Mappings ---

// LeadingZeroCount
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "LeadingZeroCount", ["System.UInt32"], "__gsharp_lzc_32")]

// SYN-49: Interlocked
[assembly: BclIntrinsicMap("System.Threading.Interlocked", "Add", ["System.Int32", "System.Int32"], "__gsharp_atomicAdd")]
[assembly: BclIntrinsicMap("System.Threading.Interlocked", "Add", ["System.Int64", "System.Int64"], "__gsharp_atomicAdd", CastFirstArgumentTo = "unsigned long long*")]

[assembly: BclIntrinsicMap("System.Threading.Interlocked", "Exchange", ["System.Int32", "System.Int32"], "atomicExch")]
[assembly: BclIntrinsicMap("System.Threading.Interlocked", "Exchange", ["System.UInt32", "System.UInt32"], "atomicExch")]
[assembly: BclIntrinsicMap("System.Threading.Interlocked", "Exchange", ["System.Int64", "System.Int64"], "atomicExch", CastFirstArgumentTo = "unsigned long long*")]
[assembly: BclIntrinsicMap("System.Threading.Interlocked", "Exchange", ["System.UInt64", "System.UInt64"], "atomicExch", CastFirstArgumentTo = "unsigned long long*")]

[assembly: BclIntrinsicMap("System.Threading.Interlocked", "CompareExchange", ["System.Int32", "System.Int32", "System.Int32"], "__gsharp_atomicCAS")]
[assembly: BclIntrinsicMap("System.Threading.Interlocked", "CompareExchange", ["System.UInt32", "System.UInt32", "System.UInt32"], "__gsharp_atomicCAS")]
[assembly: BclIntrinsicMap("System.Threading.Interlocked", "CompareExchange", ["System.Int64", "System.Int64", "System.Int64"], "__gsharp_atomicCAS", CastFirstArgumentTo = "unsigned long long*")]
[assembly: BclIntrinsicMap("System.Threading.Interlocked", "CompareExchange", ["System.UInt64", "System.UInt64", "System.UInt64"], "__gsharp_atomicCAS", CastFirstArgumentTo = "unsigned long long*")]

[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "LeadingZeroCount", ["System.UInt64"], "__gsharp_lzc_64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "LeadingZeroCount", ["System.UIntPtr"], "__gsharp_lzc_64")]

// PopCount
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "PopCount", ["System.UInt32"], "__popc", CastFirstArgumentTo = "unsigned int")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "PopCount", ["System.UInt64"], "__popcll", CastFirstArgumentTo = "unsigned long long int")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "PopCount", ["System.UIntPtr"], "__popcll")]

// TrailingZeroCount
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "TrailingZeroCount", ["System.UInt32"], "__gsharp_tzc_32")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "TrailingZeroCount", ["System.UInt64"], "__gsharp_tzc_64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "TrailingZeroCount", ["System.UIntPtr"], "__gsharp_tzc_64")]

// Log2
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "Log2", ["System.UInt32"], "__gsharp_log2_32")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "Log2", ["System.UInt64"], "__gsharp_log2_64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "Log2", ["System.UIntPtr"], "__gsharp_log2_64")]

// RotateLeft
[assembly:
    BclIntrinsicMap("System.Numerics.BitOperations", "RotateLeft", ["System.UInt32", "System.Int32"],
        "__gsharp_rotl32")]
[assembly:
    BclIntrinsicMap("System.Numerics.BitOperations", "RotateLeft", ["System.UInt64", "System.Int32"],
        "__gsharp_rotl64")]
[assembly:
    BclIntrinsicMap("System.Numerics.BitOperations", "RotateLeft", ["System.UIntPtr", "System.Int32"],
        "__gsharp_rotl64")]

// RotateRight
[assembly:
    BclIntrinsicMap("System.Numerics.BitOperations", "RotateRight", ["System.UInt32", "System.Int32"],
        "__gsharp_rotr32")]
[assembly:
    BclIntrinsicMap("System.Numerics.BitOperations", "RotateRight", ["System.UInt64", "System.Int32"],
        "__gsharp_rotr64")]
[assembly:
    BclIntrinsicMap("System.Numerics.BitOperations", "RotateRight", ["System.UIntPtr", "System.Int32"],
        "__gsharp_rotr64")]

// --- System.Math and System.MathF Mappings ---
[assembly: BclIntrinsicMap("System.Math", "Abs", ["System.Double"], "fabs")]
[assembly: BclIntrinsicMap("System.MathF", "Abs", ["System.Single"], "fabsf")]
[assembly: BclIntrinsicMap("System.Math", "Sqrt", ["System.Double"], "sqrt")]
[assembly: BclIntrinsicMap("System.MathF", "Sqrt", ["System.Single"], "sqrtf")]
[assembly: BclIntrinsicMap("System.Math", "Sin", ["System.Double"], "sin")]
[assembly: BclIntrinsicMap("System.MathF", "Sin", ["System.Single"], "sinf")]
[assembly: BclIntrinsicMap("System.Math", "Cos", ["System.Double"], "cos")]
[assembly: BclIntrinsicMap("System.MathF", "Cos", ["System.Single"], "cosf")]
[assembly: BclIntrinsicMap("System.Math", "Max", ["System.Double", "System.Double"], "fmax")]
[assembly: BclIntrinsicMap("System.MathF", "Max", ["System.Single", "System.Single"], "fmaxf")]
[assembly: BclIntrinsicMap("System.Math", "Min", ["System.Double", "System.Double"], "fmin")]
[assembly: BclIntrinsicMap("System.MathF", "Min", ["System.Single", "System.Single"], "fminf")]
[assembly: BclIntrinsicMap("System.Math", "Floor", ["System.Double"], "floor")]
[assembly: BclIntrinsicMap("System.MathF", "Floor", ["System.Single"], "floorf")]
[assembly: BclIntrinsicMap("System.Math", "Ceiling", ["System.Double"], "ceil")]
[assembly: BclIntrinsicMap("System.MathF", "Ceiling", ["System.Single"], "ceilf")]

// --- System.Numerics.Vector2,3,4 Mappings ---
[assembly: BclIntrinsicMap("System.Numerics.Vector2", "Dot", ["System.Numerics.Vector2", "System.Numerics.Vector2"], "__gsharp_dot2")]
[assembly: BclIntrinsicMap("System.Numerics.Vector2", "Distance", ["System.Numerics.Vector2", "System.Numerics.Vector2"], "__gsharp_distance2")]
[assembly: BclIntrinsicMap("System.Numerics.Vector2", "Normalize", ["System.Numerics.Vector2"], "__gsharp_normalize2")]
[assembly: BclIntrinsicMap("System.Numerics.Vector2", "SquareRoot", ["System.Numerics.Vector2"], "__gsharp_sqrt2")]
[assembly: BclIntrinsicMap("System.Numerics.Vector2", "Length", [], "__gsharp_length2")]

[assembly: BclIntrinsicMap("System.Numerics.Vector3", "Dot", ["System.Numerics.Vector3", "System.Numerics.Vector3"], "__gsharp_dot3")]
[assembly: BclIntrinsicMap("System.Numerics.Vector3", "Distance", ["System.Numerics.Vector3", "System.Numerics.Vector3"], "__gsharp_distance3")]
[assembly: BclIntrinsicMap("System.Numerics.Vector3", "Normalize", ["System.Numerics.Vector3"], "__gsharp_normalize3")]
[assembly: BclIntrinsicMap("System.Numerics.Vector3", "SquareRoot", ["System.Numerics.Vector3"], "__gsharp_sqrt3")]
[assembly: BclIntrinsicMap("System.Numerics.Vector3", "Length", [], "__gsharp_length3")]

[assembly: BclIntrinsicMap("System.Numerics.Vector4", "Dot", ["System.Numerics.Vector4", "System.Numerics.Vector4"], "__gsharp_dot4")]
[assembly: BclIntrinsicMap("System.Numerics.Vector4", "Distance", ["System.Numerics.Vector4", "System.Numerics.Vector4"], "__gsharp_distance4")]
[assembly: BclIntrinsicMap("System.Numerics.Vector4", "Normalize", ["System.Numerics.Vector4"], "__gsharp_normalize4")]
[assembly: BclIntrinsicMap("System.Numerics.Vector4", "SquareRoot", ["System.Numerics.Vector4"], "__gsharp_sqrt4")]
[assembly: BclIntrinsicMap("System.Numerics.Vector4", "Length", [], "__gsharp_length4")]
