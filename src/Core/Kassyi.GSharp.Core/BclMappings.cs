using Kassyi.GSharp.Core;

// --- System.Numerics.BitOperations Mappings ---

// LeadingZeroCount
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "LeadingZeroCount", ["System.UInt32"], "__gsharp_lzc_32")]

// Interlocked
[assembly: BclIntrinsicMap(
    "System.Threading.Interlocked", "Add", ["System.Int32", "System.Int32"], "__gsharp_atomicAdd")]
[assembly: BclIntrinsicMap(
    "System.Threading.Interlocked", "Add", ["System.Int64", "System.Int64"], "__gsharp_atomicAdd")]

[assembly: BclIntrinsicMap("System.Threading.Interlocked", "Exchange", ["System.Int32", "System.Int32"], "atomicExch")]
[assembly: BclIntrinsicMap(
    "System.Threading.Interlocked", "Exchange", ["System.UInt32", "System.UInt32"], "atomicExch")]
[assembly: BclIntrinsicMap(
    "System.Threading.Interlocked", "Exchange", ["System.Int64", "System.Int64"], "__gsharp_atomicExch")]
[assembly: BclIntrinsicMap(
    "System.Threading.Interlocked", "Exchange", ["System.UInt64", "System.UInt64"], "atomicExch",
    CastFirstArgumentTo = "unsigned long long*")]

[assembly: BclIntrinsicMap(
    "System.Threading.Interlocked", "CompareExchange",
    ["System.Int32", "System.Int32", "System.Int32"], "__gsharp_atomicCAS")]
[assembly: BclIntrinsicMap(
    "System.Threading.Interlocked", "CompareExchange",
    ["System.UInt32", "System.UInt32", "System.UInt32"], "__gsharp_atomicCAS")]
[assembly: BclIntrinsicMap(
    "System.Threading.Interlocked", "CompareExchange",
    ["System.Int64", "System.Int64", "System.Int64"], "__gsharp_atomicCAS")]
[assembly: BclIntrinsicMap(
    "System.Threading.Interlocked", "CompareExchange",
    ["System.UInt64", "System.UInt64", "System.UInt64"], "__gsharp_atomicCAS",
    CastFirstArgumentTo = "unsigned long long*")]

[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "LeadingZeroCount", ["System.UInt64"], "__gsharp_lzc_64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "LeadingZeroCount", ["System.UIntPtr"], "__gsharp_lzc_64")]

// PopCount
[assembly: BclIntrinsicMap(
    "System.Numerics.BitOperations", "PopCount", ["System.UInt32"], "__popc",
    CastFirstArgumentTo = "unsigned int")]
[assembly: BclIntrinsicMap(
    "System.Numerics.BitOperations", "PopCount", ["System.UInt64"], "__popcll",
    CastFirstArgumentTo = "unsigned long long int")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "PopCount", ["System.UIntPtr"], "__popcll")]

// TrailingZeroCount
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "TrailingZeroCount", ["System.UInt32"], "__gsharp_tzc_32")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "TrailingZeroCount", ["System.UInt64"], "__gsharp_tzc_64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "TrailingZeroCount", ["System.UIntPtr"], "__gsharp_tzc_64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "TrailingZeroCount", ["System.Int32"], "__gsharp_tzc_32")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "TrailingZeroCount", ["System.Int64"], "__gsharp_tzc_64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "TrailingZeroCount", ["System.IntPtr"], "__gsharp_tzc_64")]

// IsPow2
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "IsPow2", ["System.Int32"], "__gsharp_is_pow2_i32")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "IsPow2", ["System.UInt32"], "__gsharp_is_pow2_u32")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "IsPow2", ["System.Int64"], "__gsharp_is_pow2_i64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "IsPow2", ["System.IntPtr"], "__gsharp_is_pow2_i64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "IsPow2", ["System.UInt64"], "__gsharp_is_pow2_u64")]
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "IsPow2", ["System.UIntPtr"], "__gsharp_is_pow2_u64")]

// RoundUpToPowerOf2
[assembly: BclIntrinsicMap(
    "System.Numerics.BitOperations", "RoundUpToPowerOf2", ["System.UInt32"], "__gsharp_round_up_pow2_32")]
[assembly: BclIntrinsicMap(
    "System.Numerics.BitOperations", "RoundUpToPowerOf2", ["System.UInt64"], "__gsharp_round_up_pow2_64")]
[assembly: BclIntrinsicMap(
    "System.Numerics.BitOperations", "RoundUpToPowerOf2", ["System.UIntPtr"], "__gsharp_round_up_pow2_64")]

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
