namespace Cases;

public class ExhaustiveOnlyCases
{
#if A && B && !C && !D
    public void OnlyWithAAndB()
    {
        Work();
    }
#endif

    private static void Work()
    {
    }
}
