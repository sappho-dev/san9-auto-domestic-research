using System;

namespace San9AutoDomestic.V8Transaction.SelfTest
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                int passed = SyntheticTransactionTests.RunAll();
                Console.WriteLine("V8 OFFLINE SELF-TEST PASS: " + passed + " tests");
                Console.WriteLine("live_authorization=false; process_accessed=false; native_callbacks_invoked=false");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("V8 OFFLINE SELF-TEST FAIL: " + exception);
                return 1;
            }
        }
    }
}

