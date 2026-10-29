public class Lower_SwitchStatement_WithBarrierInSection_Snapshot
{
    public void LoweredMethod()
{
    {
        var __gsharp_switch_temp_0 = flag + 1;
        if (__gsharp_switch_temp_0 == 0)
            goto __gsharp_SwitchSection_0_0;
        if (__gsharp_switch_temp_0 == Last)
            goto __gsharp_SwitchSection_0_0;
        if (__gsharp_switch_temp_0 == 1)
            goto __gsharp_SwitchSection_0_1;
        goto __gsharp_SwitchSection_0_2;
    }

    __gsharp_SwitchSection_0_0:
        ;
    value = 1;
    Gpu.SyncThreads();
    goto __gsharp_SwitchSection_0_2;
    __gsharp_SwitchSection_0_1:
        ;
    for (int i = 0; i < 2; i++)
    {
        if (i == 1)
        {
            break;
        }
    }

    switch (value)
    {
        case 3:
            break;
        default:
            goto case 3;
    }

    goto __gsharp_SwitchSection_0_0;
    __gsharp_SwitchSection_0_2:
        ;
    if (!(value > 1))
        goto __gsharp_BranchEnd_0;
    Gpu.SyncThreads();
    goto __gsharp_SwitchEnd_0;
    __gsharp_BranchEnd_0:
        ;
    value += 2;
    goto __gsharp_SwitchEnd_0;
    __gsharp_SwitchEnd_0:
        ;
}
}