public class Lower_IfStatement_WithoutElse_Snapshot
{
    public void LoweredMethod()
{
    if (!(condition))
        goto _BranchEnd_0;
    int a = 1;
    Gpu.SyncThreads();
    _BranchEnd_0:
        ;
}
}