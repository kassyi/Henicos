public class Lower_StraightLine_TwoBarriers_Snapshot
{
    public void LoweredMethod()
{
    switch (_state)
    {
        case 0:
            int a = 1;
            _state = 1;
            return;
        case 1:
            int b = a + 2;
            _state = 2;
            return;
        case 2:
            int c = b * 3;
            _state = -1;
            return;
    }
}
}