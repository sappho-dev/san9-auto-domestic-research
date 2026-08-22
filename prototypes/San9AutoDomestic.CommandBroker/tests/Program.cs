using System;

namespace San9AutoDomestic.CommandBroker.SelfTest
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                int count = CommandBrokerTests.RunAll();
                Console.WriteLine("PASS {0}/{0} offline tests", count);
                Console.WriteLine("live_authorized=false; process_accessed=false; pinvoke=false; native_callbacks=false; transport=in_memory_only");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("SELF-TEST FAILURE: " + exception);
                return 1;
            }
        }
    }
}
