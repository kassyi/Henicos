namespace Cases;

public class VariantAttributeCases
{
#if A
    [System.Obsolete]
#endif
    public void M()
    {
        Work();
    }

    private static void Work()
    {
    }
}
