namespace Cases;

public class VariantNestingCases
{
    public void M(bool a, bool b)
    {
        if (a)
        {
            if (a)
            {
#if B
                if (b)
                {
                    if (b)
                    {
                        Work();
                    }
                }
#endif
                if (b)
                {
                    Work();
                }
            }
        }
    }

    private static void Work()
    {
    }
}
