namespace Cases;

public class SwallowedSmall
{
    public void Empty()
    {
        try { Work(); }
        catch { }
    }

    public void LogOnly()
    {
        try { Work(); }
        catch (System.Exception e) { System.Console.Error.WriteLine(e); }
    }

    private static void Work() { }
}
