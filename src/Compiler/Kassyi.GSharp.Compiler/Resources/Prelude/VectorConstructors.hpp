// --- System.Numerics.Vector2/3/4 constructors (both dialects) ---
// The constructors whose parameters are not the components in order (#499): a splat sets every component, and the
// others append components to a smaller vector. The dialect YAML maps each constructor here; the positional ones stay
// aggregate initialization (float3{x, y, z}). A function evaluates its argument once, as C# does.
__forceinline__ __device__ float2 __gsharp_vector2_create(float value) { return float2{value, value}; }
__forceinline__ __device__ float3 __gsharp_vector3_create(float value) { return float3{value, value, value}; }
__forceinline__ __device__ float3 __gsharp_vector3_create(float2 xy, float z) { return float3{xy.x, xy.y, z}; }
__forceinline__ __device__ float4 __gsharp_vector4_create(float value) { return float4{value, value, value, value}; }
__forceinline__ __device__ float4 __gsharp_vector4_create(float3 xyz, float w) { return float4{xyz.x, xyz.y, xyz.z, w}; }
__forceinline__ __device__ float4 __gsharp_vector4_create(float2 xy, float z, float w) { return float4{xy.x, xy.y, z, w}; }
