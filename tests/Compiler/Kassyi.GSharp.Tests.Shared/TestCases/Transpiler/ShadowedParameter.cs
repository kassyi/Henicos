// The parameter i of N is another symbol than the parameter i of M (OverrideScopeStackTests)
class C
{
    void M(int i)
    {
        int N(int i) => i;
    }
}
