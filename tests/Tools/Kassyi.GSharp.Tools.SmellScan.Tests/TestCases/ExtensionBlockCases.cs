namespace Cases;

public static class ExtensionBlockCases
{
    extension(string value)
    {
        public int Length2()
        {
            return value.Length;
        }
    }

    extension(int value)
    {
        public int Length2()
        {
            return value;
        }
    }
}
