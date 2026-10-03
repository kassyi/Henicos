public class Lower_IfStatement_WithElse_Snapshot
{
    public void LoweredMethod()
{
    if (!(condition))
        goto _BranchElse_0;
    int a = 1;
    goto _BranchEnd_0;
    _BranchElse_0:
        ;
    int b = 2;
    Gpu.SyncThreads();
    _BranchEnd_0:
        ;
}
}