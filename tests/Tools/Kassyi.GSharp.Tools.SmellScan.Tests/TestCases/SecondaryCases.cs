namespace Cases;

public class SecondaryCases
{
    public int this[int a, int b]
    {
        get { return a; }
        set { Work(); }
    }

    public event System.Action Changed
    {
        add { Work(); }
        remove { Work(); }
    }

    public void Deconstruct()
    {
        foreach (var (a, b) in Pairs())
        {
            Work();
        }
    }

    public void Generic<T>(int a)
    {
        Work();
    }

    public void Generic(int a)
    {
        Work();
    }

    private static System.Collections.Generic.IEnumerable<(int, int)> Pairs() => new (int, int)[0];

    private static void Work()
    {
    }
}
