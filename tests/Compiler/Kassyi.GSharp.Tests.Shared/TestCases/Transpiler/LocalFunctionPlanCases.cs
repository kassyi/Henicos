// Each method of Shapes declares local functions in one shape; LocalFunctionPlanTests reads the plan of its body
namespace LocalFunctionPlanCases;

public static class Shapes
{
    public static int NoLocalFunction(int n)
    {
        return n;
    }

    // Statement 0 uses First and Second, which are declared after it; Second uses First
    public static int UsedAhead(int n)
    {
        int total = Second() + First();
        return total + n;

        int First() => 1;
        int Second() => First() + 1;
    }

    // Fact reaches itself
    public static int SelfRecursive(int n)
    {
        return Fact(n);

        int Fact(int k) => k <= 1 ? 1 : k * Fact(k - 1);
    }

    // A and B reach each other, C only uses A
    public static int MutualRecursive(int n)
    {
        return C(n) + A(n);

        int A(int k) => k == 0 ? 0 : B(k - 1);
        int B(int k) => k == 0 ? 1 : A(k - 1);
        int C(int k) => A(k) + 1;
    }
}
