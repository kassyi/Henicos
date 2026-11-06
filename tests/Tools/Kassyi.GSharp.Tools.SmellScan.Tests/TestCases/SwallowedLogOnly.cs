namespace Cases;

public class SwallowedPair
{
    public void Both()
    {
        try { Work(); }
        catch (System.Exception e) { System.Console.Error.WriteLine(e); }
    }

    private static void Work() { }
}
