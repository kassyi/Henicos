using Kassyi.GSharp.Core;

// --- System.Numerics.BitOperations Mappings ---

// LeadingZeroCount
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "LeadingZeroCount", ["System.UInt32"], "__gsharp_lzc_32")]
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
