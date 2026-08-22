using System;

namespace San9AutoDomestic.SingleCommandSlice.SelfTest
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                int count = ShadowSliceTests.RunAll();
                Console.WriteLine("PASS {0}/{0}: offline shadow single-command slice", count);
                Console.WriteLine("SHADOW RESULT: GO; LIVE AUTHORIZATION/NATIVE SUBMISSION: NO-GO.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception);
                return 1;
            }
        }
    }
}
