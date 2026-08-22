using System;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public static class San9Pk101Target
    {
        public const string ExpectedExecutablePath = @"D:\三国志9\10101749\San9PK.exe";
        public const string ExpectedWindowClass = "KOEI_SAN9WINDOW";
        public const string ExpectedProcessName = "San9PK";
        public const long ExpectedExecutableSize = 2636800L;
        public const string ExpectedSha256 = "D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028";
        public const ushort ExpectedPeMachine = 0x014c;
        public const uint ExpectedImageBase = 0x00400000;
        public const uint ExpectedSizeOfImage = 0x01759000;

        private static readonly Version FileVersion = new Version(1, 0, 1, 0);

        public static Version ExpectedFileVersion
        {
            get { return FileVersion; }
        }
    }
}
