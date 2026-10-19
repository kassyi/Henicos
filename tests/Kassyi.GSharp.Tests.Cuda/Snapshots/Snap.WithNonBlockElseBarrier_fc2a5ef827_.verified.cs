public class Lower_IfStatement_WithNonBlockElseBarrier_Snapshot
{
    public void LoweredMethod()
{
    if (!(condition))
        goto _BranchElse_0;
    int a = 1;
    goto _BranchEnd_0;
    _BranchElse_0:
        ;
    Gpu.SyncThreads();
    _BranchEnd_0:
        ;
}
}