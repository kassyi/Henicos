public class Hoist_VariablesAcrossBarrier_Snapshot
{
    public void LoweredMethod()
{
    switch (_state)
    {
        case 0:
            _ctx->a = 1;
            _ctx->b = 2;
            _state = 1;
            return;
        case 1:
            int c = _ctx->a + 3; // 'a' is hoisted (accessed via ctx->a), 'c' is kept local.
            _state = -1;
            return;
    }
}
}