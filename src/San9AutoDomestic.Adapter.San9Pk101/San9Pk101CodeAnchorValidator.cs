using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal sealed class CodeAnchorDefinition
    {
        internal CodeAnchorDefinition(string name, uint address, int length, string sha256)
        {
            Name = name;
            Address = address;
            Length = length;
            ExpectedSha256 = sha256;
        }

        internal string Name;
        internal uint Address;
        internal int Length;
        internal string ExpectedSha256;
    }

    internal static class San9Pk101CodeAnchorValidator
    {
        private static readonly CodeAnchorDefinition[] DefinitionsValue =
        {
            new CodeAnchorDefinition("patrol-can-execute", 0x004c1930, 0x40, "409502CF1FD0482DB9BF8C77918984807284617D4B6B7E5A99749DC365C62263"),
            new CodeAnchorDefinition("commerce-can-execute", 0x004c6400, 0x40, "25BB425609A5ABB2DB776A547BD73672C037317C310EEEEB646DA6B24C0537B0"),
            new CodeAnchorDefinition("cultivate-can-execute", 0x004c1f50, 0x40, "3FAE7862F9AAA2BE569588F4D730923A95153D26D026B2AD525C3C9608547EFA"),
            new CodeAnchorDefinition("repair-can-execute", 0x004c2270, 0x40, "59CAECB03E4B7833D5F2342CA2A64BA7022684C5F93E1E1DBFA7711EAF054983"),
            new CodeAnchorDefinition("train-can-execute", 0x004c3890, 0x40, "639BBCFB797EDFF7D7EDA9E79135B9E46774CA63B3A8D0E903A566C1A1BA5E61"),
            new CodeAnchorDefinition("city-state-code-6", 0x00436160, 0x10, "F4BD0FE32F42934B476E22C611D22BEBD29F596FD35FB56B3731B5E2173148B4"),
            new CodeAnchorDefinition("candidate-wrapper", 0x00436d60, 0x28, "86B5B1D17326EE51C6984A2BE119000F24EC89DDD70B5B88B2BB98F8FD359FEF"),
            new CodeAnchorDefinition("ready-predicate", 0x00472d30, 0x38, "E399C40DD914BDA78FB67ABD5ABEA870617247D0F2D349681C39C531F1D02EF8"),
            new CodeAnchorDefinition("stable-descending-sort", 0x0046f500, 0x60, "F16781BCC5DFF90C3C4DEE94A0669E18BEE1AE279CD728E3A1F6A67817E5254D"),
            new CodeAnchorDefinition("repair-current-getter", 0x00435630, 0x18, "EE1B2F07A4DAF72464E1300F7512A2CC054E7B11AA821D96B2B91491C5321984"),
            new CodeAnchorDefinition("troops-forwarder", 0x00435540, 0x10, "EB4ED5B5B56DEEA0553829347CC48699BA8884487BCAEBF159DC2AFDDBD441C2"),
            new CodeAnchorDefinition("morale-forwarder", 0x00434f50, 0x10, "51AE3D8D1B5959628D32BA6D0780F30DD5C2166D12C681B9D48AC6EFD04B5EE7"),
            new CodeAnchorDefinition("common-state-forwarder", 0x004354d0, 0x10, "2E8ADCC300E5ACB2C8431B8A087C49771BDDFAA23C74B06FB20E9D0F8BD7BB2E"),
            new CodeAnchorDefinition("common-state-derived", 0x00461730, 0x38, "1FEEE295F8E85CECECD2251BA24B46B3F87A3733F01E72D0A3E185828C377770"),
            new CodeAnchorDefinition("patrol-current-getter", 0x0043a460, 0x10, "B45A810C3D7199575734B23FFDC1FB755CC4C0342AC2FD8440CE3B2DA076B683"),
            new CodeAnchorDefinition("repair-maximum-getter", 0x0043a4c0, 0x10, "97B90164AB94EF0A1CAE342043CD5CEA5E1000B3346FC8E5CFCACE62A6E003E7"),
            new CodeAnchorDefinition("commerce-current-getter", 0x0043a4e0, 0x10, "3ADB2D92BB8059694AA99ED64A7E2CA779685C9DFF3DCBB68FFA0FC930998D5E"),
            new CodeAnchorDefinition("commerce-maximum-getter", 0x0043a530, 0x10, "32D97134071E9B8DCA7A09A9B825D513B21935CA1275CF06A10265CA43BD9987"),
            new CodeAnchorDefinition("cultivate-current-getter", 0x0043a550, 0x10, "220870B4003709A14A4ECBBE7555442F619426F2D1ED6B72EBA477A4E475F1D1"),
            new CodeAnchorDefinition("cultivate-maximum-getter", 0x0043a5a0, 0x10, "DD825612302D30D7C64DA41B6396BF511CB599316E420A63EA083E6207582C0C"),
            new CodeAnchorDefinition("troops-raw-getter", 0x0045e400, 0x10, "CDDC6EF2C7B4E08C4C25F4FD190A1AB160FE57A2F87C2E2A07ABA0A8180233D8"),
            new CodeAnchorDefinition("morale-raw-getter", 0x0045e410, 0x10, "1F4B219FB3F4F97B0ADDF8D536A535D4007AC936E457762544314CDC517527CF"),
            new CodeAnchorDefinition("embedded-a0-bit-getter", 0x0045ea80, 0x18, "164F271E06DFED79831861A84CDDDFD44BDA940A3E703CAB50417197B1D8BA2E"),
            new CodeAnchorDefinition("city-vtable-slot-50", 0x00605988, 4, "46C47FF94AA1BC3AFC083DEF5C996D40287C7CA88E591239422938D93AC6E4E8"),
            new CodeAnchorDefinition("city-vtable-slot-80", 0x006059b8, 4, "99A45A216252368B5043D1F5B0B6F058CA21D2CA53AFA55627CECB850443FF57"),
            new CodeAnchorDefinition("city-vtable-slot-84", 0x006059bc, 4, "4BD7FAAE8041C18608C5CF4ABDF4EEF82E98EA77A58518C0EBEFAF40E804A060"),
            new CodeAnchorDefinition("city-vtable-slot-90", 0x006059c8, 4, "0754190FB758D0C5D752FB3C83565FF4B6A1631DD36251562FB67AD3F855F247"),
            new CodeAnchorDefinition("city-vtable-slot-e8", 0x00605a20, 4, "2DD1523D6CB8D62712251EC97D0CF3087D29BE5706B69E778F43E8FCC2EB28A6"),
            new CodeAnchorDefinition("city-vtable-slot-f0", 0x00605a28, 4, "A268F7F02319070B557D4980FFC6905CC0592871451C7583A613765C9F18D5E3"),
            new CodeAnchorDefinition("city-vtable-slot-f4", 0x00605a2c, 4, "34D469BA119C31C10F0D280225C1550D311BDE3127A2C7B7733E4B48087D5B01"),
            new CodeAnchorDefinition("city-vtable-slot-f8", 0x00605a30, 4, "62CD8B03808A5D466AF17BDF7A89FE338569446AB70A3DAF729FDE72681EA3D6"),
            new CodeAnchorDefinition("city-vtable-slot-fc", 0x00605a34, 4, "78856C971D0F78845508C9D179D7C9A08F7A5CC812B1195D2E78E7E336CDB1C3"),
            new CodeAnchorDefinition("city-vtable-slot-12c", 0x00605a64, 4, "EC66A0E4E341D32F26058D1842A6F6BAF3F85436C81D1E103C338528BE6644D0"),
            new CodeAnchorDefinition("embedded-vtable-slot-80", 0x00606510, 4, "F09635BC62C8EB32B30AE79A2F2BDDC55EE99B337EF15F4A1E878DA14093682B"),
            new CodeAnchorDefinition("embedded-vtable-slot-84", 0x00606514, 4, "6B27F1A4F98A4BCFFD528F9398C3F6195282E6BD87D8B8C3DD9956991DB09230"),
            new CodeAnchorDefinition("embedded-vtable-slot-90", 0x00606520, 4, "405613CF4C8096BDF43D8FAE29CAF688AB8732944A749D8281D679222A52D530"),
            new CodeAnchorDefinition("embedded-vtable-slot-a0", 0x00606530, 4, "7EF78170E1121B57B2C9D1555A83FC811E1725F082383765F45AC575902FBA4D"),
            new CodeAnchorDefinition("patrol-handler-slot-20", 0x00609bb0, 4, "9540FCB885765F5A358ED9033E08A27B556D495B91A25E6BA47B6758A301BA53"),
            new CodeAnchorDefinition("commerce-handler-slot-20", 0x00609f58, 4, "67343AF536118FE759F44F2C24E58587F947CD4022E1F5C676666325D6BBAD8E"),
            new CodeAnchorDefinition("cultivate-handler-slot-20", 0x00609c10, 4, "0845AD0A8DB3BE298A24DC30BE1CB632733A8E702F1A07B1E968347382AAFFE9"),
            new CodeAnchorDefinition("repair-handler-slot-20", 0x00609c40, 4, "827C8E98F79C0B68FC912A2A0BB91A6E0DEBBA2583327A561894B860321C8CED"),
            new CodeAnchorDefinition("train-handler-slot-20", 0x00609d00, 4, "E4AC4DA8D72766710F360DD2041D7F2A3FC0897615A86D11C8C9C57EB08A4F43"),
            new CodeAnchorDefinition("common-handler-gate", 0x004c52c0, 0x40, "3E26E9D5245F181BA095AF160892B795F75BB52F16F6A711A1DAD373E0166903"),
            new CodeAnchorDefinition("command-handler-constructor", 0x004c1720, 0x30, "34B8CF379A8A690DE3787ED037BD7CD45DBACB90A06651E101AEA8C5D850032C"),
            new CodeAnchorDefinition("handler-base-constructor", 0x004c5180, 0x30, "360A26BFA7CF4EC1B3E171CC79654E6C4C73DBBE4B60692DD6CCFE6C29B449F9"),
            new CodeAnchorDefinition("handler-context-constructor", 0x0050e940, 0x30, "C56EC0475A5E6A767771463F5710A5CD2406FDC818349B5EAA5F3212B8E5B97B"),
            new CodeAnchorDefinition("handler-context-validity", 0x0050e990, 0x30, "22EE33AE23405CB873CECBFABB0968C113D26C36C74F6B89056768C6CF71BDDA"),
            new CodeAnchorDefinition("context-constant-true", 0x0045de60, 0x10, "817A089FF2FD32D2D62F3ABA44C1A5C138272415E57451CD6EAFB213015FA8D4"),
            new CodeAnchorDefinition("context-nonnull-validity", 0x00405840, 0x20, "551867BF62E1AB35F1B9CCFEA0E19178E8028CD2B4F2378EFE375CB3132D5562"),
            new CodeAnchorDefinition("corps-control-flag-getter", 0x0043f9f0, 0x10, "462C43E50B1E95BB5A796E7C5E7357865333554BA258D719D6682F59657B7324"),
            new CodeAnchorDefinition("corps-vtable-slot-40", 0x00605c90, 4, "9ED709087CE4A815B417BE18BD9E0AFB69AA3CAF56041C0A88A85CEB30FC1005")
        };

        internal static CodeAnchorDefinition[] Definitions
        {
            get { return DefinitionsValue.ToArray(); }
        }

        internal static CodeAnchorObservation[] Observe(IReadOnlyProcessMemory memory, string executablePath)
        {
            byte[] file = File.ReadAllBytes(executablePath);
            List<CodeAnchorObservation> observations = new List<CodeAnchorObservation>();
            foreach (CodeAnchorDefinition definition in DefinitionsValue)
            {
                CodeAnchorObservation observation = new CodeAnchorObservation
                {
                    Name = definition.Name,
                    Address = definition.Address,
                    Length = definition.Length,
                    ExpectedSha256 = definition.ExpectedSha256
                };
                try
                {
                    byte[] disk = ReadImageBytes(file, definition.Address, definition.Length);
                    byte[] liveA = memory.ReadBytes(definition.Address, definition.Length);
                    byte[] liveB = memory.ReadBytes(definition.Address, definition.Length);
                    observation.DiskSha256 = Sha256(disk);
                    observation.LiveSha256 = Sha256(liveB);
                    observation.DiskMatchesExpected = string.Equals(
                        observation.DiskSha256,
                        definition.ExpectedSha256,
                        StringComparison.OrdinalIgnoreCase);
                    observation.LiveWasStable = liveA.SequenceEqual(liveB);
                    observation.LiveMatchesDisk = liveB.SequenceEqual(disk);
                }
                catch (Exception exception)
                {
                    observation.Error = exception.GetType().Name + ": " + exception.Message;
                }

                observations.Add(observation);
            }

            return observations.ToArray();
        }

        internal static byte[] ReadExpectedDiskBytes(CodeAnchorDefinition definition)
        {
            return ReadImageBytes(File.ReadAllBytes(San9Pk101Target.ExpectedExecutablePath),
                definition.Address, definition.Length);
        }

        private static byte[] ReadImageBytes(byte[] file, uint address, int count)
        {
            if (file == null || file.Length < 0x100)
            {
                throw new InvalidDataException("PE image is missing or too short.");
            }

            int pe = checked((int)ReadUInt32(file, 0x3c));
            if (pe < 0 || pe + 24 > file.Length || ReadUInt32(file, pe) != 0x00004550)
            {
                throw new InvalidDataException("Invalid PE signature.");
            }

            int sectionCount = ReadUInt16(file, pe + 6);
            int optionalSize = ReadUInt16(file, pe + 20);
            int optional = pe + 24;
            if (optional + optionalSize > file.Length || ReadUInt16(file, optional) != 0x010b)
            {
                throw new InvalidDataException("Expected a complete PE32 optional header.");
            }

            uint imageBase = ReadUInt32(file, optional + 28);
            if (imageBase != San9Pk101Target.ExpectedImageBase || address < imageBase)
            {
                throw new InvalidDataException("PE image base or anchor address is invalid.");
            }

            uint rva = address - imageBase;
            int sections = optional + optionalSize;
            for (int index = 0; index < sectionCount; index++)
            {
                int section = checked(sections + index * 40);
                if (section + 40 > file.Length)
                {
                    throw new InvalidDataException("PE section table is truncated.");
                }

                uint virtualAddress = ReadUInt32(file, section + 12);
                uint rawSize = ReadUInt32(file, section + 16);
                uint rawPointer = ReadUInt32(file, section + 20);
                ulong end = unchecked((ulong)rva) + unchecked((uint)count);
                if (rva >= virtualAddress && end <= unchecked((ulong)virtualAddress) + rawSize)
                {
                    ulong offset = unchecked((ulong)rawPointer) + (rva - virtualAddress);
                    if (offset + unchecked((uint)count) > unchecked((ulong)file.Length))
                    {
                        throw new InvalidDataException("Anchor extends beyond the PE file.");
                    }

                    byte[] result = new byte[count];
                    Buffer.BlockCopy(file, checked((int)offset), result, 0, count);
                    return result;
                }
            }

            throw new InvalidDataException(string.Format(
                "Anchor 0x{0:X8}+0x{1:X} has no complete on-disk section mapping.", address, count));
        }

        internal static string Sha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty);
            }
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
            {
                throw new InvalidDataException("PE read is out of range.");
            }

            return BitConverter.ToUInt16(bytes, offset);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
            {
                throw new InvalidDataException("PE read is out of range.");
            }

            return BitConverter.ToUInt32(bytes, offset);
        }
    }
}
