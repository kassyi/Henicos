public class Lower_SwitchStatement_OnlyTheSwitchWithBarrierIsFlattened_Snapshot
{
    public void LoweredMethod()
{
    switch (flag)
    {
        case 0:
            value = 1;
            break;
    }

    {
        var __gsharp_switch_temp_0 = flag;
        if (__gsharp_switch_temp_0 == 1)
            goto __gsharp_SwitchSection_0_0;
        goto __gsharp_SwitchEnd_0;
    }

    __gsharp_SwitchSection_0_0:
        ;
    Gpu.SyncThreads();
    goto __gsharp_SwitchEnd_0;
    __gsharp_SwitchEnd_0:
        ;
}
}