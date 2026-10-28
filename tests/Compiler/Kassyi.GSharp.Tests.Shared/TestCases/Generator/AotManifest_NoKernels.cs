namespace AotManifestTest
{
    // An assembly without kernels: the generator writes an empty artifact manifest, so that the build compiles no native
    // module left over from an earlier build.
    public static class Helpers
    {
        public static int Twice(int value) => value * 2;
    }
}
