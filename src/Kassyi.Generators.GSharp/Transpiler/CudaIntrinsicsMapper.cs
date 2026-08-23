namespace Kassyi.Generators.GSharp.Transpiler;

internal static class CudaIntrinsicsMapper
{
    private static readonly Dictionary<string, Func<string, string>> s_apiMappings = new()
    {
        { "Math", name => name.ToLowerInvariant() },
        { "ThreadIndex", name => $"threadIdx.{name.ToLowerInvariant()}" },
        { "BlockIndex", name => $"blockIdx.{name.ToLowerInvariant()}" },
        { "BlockDimension", name => $"blockDim.{name.ToLowerInvariant()}" },
        { "GridDimension", name => $"gridDim.{name.ToLowerInvariant()}" },
        { "Gpu", name => name switch {
            "SyncThreads" => "__syncthreads",
            "SyncGrid" => "cg::this_grid().sync",
            "AtomicAdd" => "atomicAdd",
            "AtomicSub" => "atomicSub",
            "AddWithCarry" => "__gsharp_add_cc",
            "SubWithBorrow" => "__gsharp_sub_cc",
            _ => name
        } }
    };

    public static bool TryMap(string typeName, string methodName, out string mappedName)
    {
        if (s_apiMappings.TryGetValue(typeName, out var mapper))
        {
            mappedName = mapper(methodName);
            return true;
        }

        mappedName = string.Empty;
        return false;
    }
}
