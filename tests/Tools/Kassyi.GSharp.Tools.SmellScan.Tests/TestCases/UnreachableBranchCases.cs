namespace Cases;

public class UnreachableBranchCases
{
#if A && !A
    public void Never()
    {
        Work();
    }
#endif

    private static void Work()
    {
    }
}
