public class Lower_IfStatement_WithoutElse_Snapshot
{
    public void LoweredMethod()
{
    if (!(condition))
        goto __gsharp_BranchEnd_0;
    int a = 1;
    Gpu.SyncThreads();
    __gsharp_BranchEnd_0:
        ;
}
}