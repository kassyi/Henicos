namespace StatementPreludeCases;

public class Cases
{
    public static bool TryGet(out int val)
    {
        val = 42;
        return true;
    }

    public void BoundDeclaration()
    {
        TryGet(out var x);
    }
}
