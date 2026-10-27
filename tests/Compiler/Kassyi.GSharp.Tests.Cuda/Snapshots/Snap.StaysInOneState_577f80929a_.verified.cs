public class Managed_LabelLoop_StaysInOneState_Snapshot
{

    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            int tid = ThreadIndex.X;
            int acc = 0;
            int i = 0;
            loop:
                if (i < rounds)
                {
                    acc += data[tid];
                    i++;
                    goto loop;
                }

            data[tid] = acc;
            __gsharp_state = -1;
            return;
        }
    }
}
}