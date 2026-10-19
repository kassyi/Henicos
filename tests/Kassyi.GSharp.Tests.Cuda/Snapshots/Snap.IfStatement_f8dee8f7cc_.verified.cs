public class Lower_IfStatement_WithElse_Snapshot
{
    public void LoweredMethod()
{
    if (!(condition))
        goto __gsharp_BranchElse_0;
    int a = 1;
    goto __gsharp_BranchEnd_0;
    __gsharp_BranchElse_0:
        ;
    int b = 2;
    Gpu.SyncThreads();
    __gsharp_BranchEnd_0:
        ;
}
}