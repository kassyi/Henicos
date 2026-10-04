public class Lower_StraightLine_NoBarriers_Snapshot
{
    public void LoweredMethod()
{
    switch (_state)
    {
        case 0:
        {
            int a = 1;
            int b = a + 2;
            _state = -1;
            return;
        }
    }
}
}