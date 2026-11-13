namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>The launch setting that a parameter of a module launcher supplies instead of a kernel argument.</summary>
internal enum ModuleLaunchSetting
{
    None,
    GridDimX,
    GridDimY,
    GridDimZ,
    BlockDimX,
    BlockDimY,
    BlockDimZ,
    SharedMemBytes,
    Stream,
}
