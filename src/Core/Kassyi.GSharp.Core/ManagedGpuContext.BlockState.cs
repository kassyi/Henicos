namespace Kassyi.GSharp.Core;

public static partial class ManagedGpuContext
{
    // The state that the GPU threads of a launch share: the launching thread owns one and keeps it between launches
    [ThreadStatic]
    private static ManagedBlockState? t_blockState;

    private static ManagedBlockState BlockState => t_blockState ??= new ManagedBlockState();
}
