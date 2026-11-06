namespace Cases;

public class VariantParameterTypeCases
{
    public void P(
#if A
        long
#else
        int
#endif
        a,
        int b)
    {
        Work();
    }

    private static void Work()
    {
    }
}
