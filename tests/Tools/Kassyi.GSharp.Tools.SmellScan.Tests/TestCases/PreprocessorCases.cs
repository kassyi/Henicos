namespace Cases;

public class PreprocessorCases
{
    public void BeforeDirectives()
    {
        Work();
    }

#if ALPHA
    public void InAlpha()
    {
        Work();
    }
#elif BETA && GAMMA
    public void InBetaAndGamma()
    {
        Work();
    }
#else
    public void InElse()
    {
        Work();
    }
#endif

#if DELTA
    public void InDelta()
    {
        Work();
#if EPSILON
        Work();
#endif
    }
#endif

    public void AfterDirectives()
    {
        Work();
    }

    private static void Work()
    {
    }
}
