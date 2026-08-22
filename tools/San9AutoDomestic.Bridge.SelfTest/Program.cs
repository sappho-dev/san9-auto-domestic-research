using System;

namespace San9AutoDomestic.Bridge.SelfTest
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length > 1
                || (args.Length == 1
                    && !string.Equals(args[0], "--self-test", StringComparison.Ordinal)))
            {
                Console.Error.WriteLine("Usage: San9AutoDomestic.Bridge.SelfTest.exe [--self-test]");
                return 2;
            }

            Console.WriteLine("Offline synthetic bridge tests only; no game or process connection exists.");
            return SyntheticBridgeProtocolTests.RunAll();
        }
    }
}
