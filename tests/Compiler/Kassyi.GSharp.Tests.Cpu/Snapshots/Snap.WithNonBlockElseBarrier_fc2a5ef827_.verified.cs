public class Lower_IfStatement_WithNonBlockElseBarrier_Snapshot
{
    public void LoweredMethod()
{
    if (!(condition))
        goto __gsharp_BranchElse_0;
    int a = 1;
    goto __gsharp_BranchEnd_0;
    __gsharp_BranchElse_0:
        ;
    Gpu.SyncThreads();
    __gsharp_BranchEnd_0:
        ;
}
}