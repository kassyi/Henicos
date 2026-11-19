namespace Kassyi.GSharp.Compiler.Analysis;

internal enum NativeStackObstacle
{
    None,
    NoBlockBarrier,
    WarpCollective,
    GridSync,
    Cooperative,
    RecursiveSuspendingCall,
    ImportedSuspendingCall
}
