namespace Cases;

public class ManySymbols
{
#if S1
    public void InS1()
    {
        Work();
    }
#endif

#if S2
    public void InS2()
    {
        Work();
    }
#endif

#if S3
    public void InS3()
    {
        Work();
    }
#endif

#if S4
    public void InS4()
    {
        Work();
    }
#endif

#if S5
    public void InS5()
    {
        Work();
    }
#endif

#if S6
    public void InS6()
    {
        Work();
    }
#endif

#if S7
    public void InS7()
    {
        Work();
    }
#endif

#if S8
    public void InS8()
    {
        Work();
    }
#endif

#if S9
    public void InS9()
    {
        Work();
    }
#endif

#if S1 && S2 && S3 && S4 && S5 && S6 && S7 && S8 && !S9
    public void InAllButLast()
    {
        Work();
    }
#endif

    private static void Work()
    {
    }
}
