using Shared.ByteParsing;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Envs;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Init;
using Shared.GameFormats.Wwise.Plat;
using Shared.GameFormats.Wwise.Wem.V132;
using Shared.GameFormats.Wwise.Wem.V132.Encoding;

namespace Test.Audio
{
    // Buses, attenuation curves and the init bank's own chunks, against bytes copied out of the
    // games rather than produced by this project's writer.
    //
    // Three of the readings below were wrong until 2026-08-31 and no byte count could have said so,
    // because each was the right width and the wrong type or was simply thrown away. What settles
    // them is the value: a ducking fade authored in whole milliseconds, a volume curve that has to
    // reach silence.
    public class BankReadingTests
    {
        private const uint WarhammerBankVersion = 2147483784;
        private const uint AttilaBankVersion = 112;

        private static readonly byte[] AttilaState =
        [
            0x01, 0x0F, 0x00, 0x00, 0x00, 0x78, 0x56, 0x34, 0x12,
            0x02, 0x34, 0x78,
            0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x20, 0xC0
        ];

        // init.bnk, bus 77155641. Ducks two other buses by 3 and 6 dB.
        private static readonly byte[] WarhammerBus =
        [
            0x08, 0xA3, 0x00, 0x00, 0x00, 0x39, 0x4D, 0x99, 0x04, 0xD2, 0x6C, 0x87, 0xB8, 0x07, 0x05, 0x0E,
            0x13, 0x1B, 0x1C, 0x1D, 0x20, 0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0x88,
            0xC1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8,
            0x42, 0x01, 0x15, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xE8,
            0x03, 0x00, 0x00, 0x00, 0x00, 0xC0, 0xC2, 0x02, 0x00, 0x00, 0x00, 0x4A, 0x37, 0xE8, 0x48, 0x00,
            0x00, 0x40, 0xC0, 0x64, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x04, 0x00, 0xB7, 0x0D, 0x77,
            0x5C, 0x00, 0x00, 0xC0, 0xC0, 0x64, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x04, 0x00, 0x01,
            0x00, 0x00, 0x60, 0x80, 0x8A, 0x17, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
            0x3C, 0xE2, 0xA4, 0xA7, 0x00, 0x02, 0x05, 0x93, 0x60, 0xE6, 0x12, 0x02, 0x02, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x80, 0xBF, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00,
            0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        // init.bnk, bus 30729851 -- the same bus id Warhammer III uses, one version older.
        private static readonly byte[] AttilaBus =
        [
            0x08, 0x7A, 0x00, 0x00, 0x00, 0x7B, 0xE6, 0xD4, 0x01, 0xEB, 0x18, 0x5A, 0xCE, 0x05, 0x05, 0x1A,
            0x1B, 0x1C, 0x1F, 0x00, 0x00, 0x40, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x06, 0x94, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02,
            0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0xC0, 0xC2, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x55, 0x32, 0x31, 0xE5, 0x00, 0x02, 0x00,
            0xA5, 0xB0, 0xF6, 0xF6, 0xE5, 0xB5, 0x95, 0x3B, 0x42, 0xF9, 0x97, 0x2B, 0xA0, 0xD3, 0x41, 0x25,
            0x27, 0xBD, 0xDA, 0x3F, 0x00, 0x03, 0x00, 0x42, 0xDA, 0xDB, 0x6A, 0x53, 0xE5, 0x8C, 0x33, 0x98,
            0x53, 0x70, 0x14, 0x1C, 0xD7, 0x32, 0x37, 0x43, 0x01, 0x3B, 0xCA, 0x3C, 0x12, 0x9F, 0x39
        ];

        // campaign_vo__core.bnk, attenuation 947056.
        private static readonly byte[] WarhammerAttenuation =
        [
            0x0E, 0x29, 0x01, 0x00, 0x00, 0x70, 0x73, 0x0E, 0x00, 0x00, 0x00, 0x01, 0xFF, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x02, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x81, 0xDF, 0xD7, 0x41, 0xAE, 0x17, 0x48, 0xBE, 0x02, 0x00, 0x00, 0x00, 0x12, 0x04, 0x99,
            0x42, 0xC0, 0x72, 0x55, 0xBF, 0x06, 0x00, 0x00, 0x00, 0x00, 0xCC, 0xBE, 0x42, 0x91, 0x49, 0x71,
            0xBF, 0x02, 0x00, 0x00, 0x00, 0x2D, 0xFF, 0xC7, 0x42, 0x00, 0x00, 0x80, 0xBF, 0x09, 0x00, 0x00,
            0x00, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0x80, 0xBF, 0x04, 0x00, 0x00, 0x00, 0x02, 0x02, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x82, 0xC5, 0x5F, 0xBF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42,
            0x1A, 0xB2, 0x3F, 0xBF, 0x04, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00, 0x55, 0x55, 0x55, 0x40, 0x00, 0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0xB6, 0x6D, 0x97, 0x42, 0x00, 0x00, 0x98, 0x41, 0x07, 0x00, 0x00, 0x00, 0x00,
            0x00, 0xC8, 0x42, 0x00, 0x00, 0x3C, 0x42, 0x04, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00, 0x55, 0x55, 0x55, 0x40, 0x00, 0x00,
            0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0xE7, 0x79, 0x96, 0x42, 0x00, 0x00, 0x80, 0x41, 0x06, 0x00,
            0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0x4C, 0x42, 0x04, 0x00, 0x00, 0x00, 0x00, 0x03,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x48, 0x42, 0x01, 0x00, 0x00, 0x00, 0xAB, 0xA4, 0x83,
            0x41, 0x00, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0x00,
            0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7A, 0xDF, 0x79, 0x42,
            0x04, 0x00, 0x00, 0x00, 0x2B, 0x11, 0x82, 0x41, 0x00, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00,
            0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        // sound.pack, attenuation 255700412.
        private static readonly byte[] AttilaAttenuation =
        [
            0x0E, 0x2A, 0x00, 0x00, 0x00, 0xBC, 0xAD, 0x3D, 0x0F, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
            0xFF, 0x01, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
            0x00, 0x00, 0x00, 0xF0, 0x41, 0xFF, 0xFE, 0x7F, 0xBF, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        // A switch container from Warhammer III with authored cross-fades: 5000 and 2000 ms on one
        // branch, 2000 and 1000 on the other. Read as floats these are denormals, so every
        // authored switch cross-fade in the game was silently instant.
        private static readonly byte[] WarhammerSwitchContainer =
        [
            0x06, 0x82, 0x00, 0x00, 0x00, 0xCC, 0x1E, 0x26, 0x22, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xBA, 0xD4, 0x33, 0x3C, 0x00, 0x01, 0x17, 0x00, 0x00, 0x40, 0xC1, 0x01, 0x02, 0x00, 0x00, 0xC8,
            0xC2, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x02,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0xB9, 0x94, 0xB8, 0x1C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02,
            0x00, 0x00, 0x00, 0xE0, 0x0E, 0x2C, 0x11, 0x4F, 0xFA, 0xCE, 0x14, 0x02, 0x00, 0x00, 0x00, 0xA8,
            0xB9, 0x72, 0x2A, 0x01, 0x00, 0x00, 0x00, 0xE0, 0x0E, 0x2C, 0x11, 0xBE, 0x6D, 0xB1, 0x7D, 0x01,
            0x00, 0x00, 0x00, 0x4F, 0xFA, 0xCE, 0x14, 0x02, 0x00, 0x00, 0x00, 0xE0, 0x0E, 0x2C, 0x11, 0x00,
            0x01, 0x88, 0x13, 0x00, 0x00, 0xD0, 0x07, 0x00, 0x00, 0x4F, 0xFA, 0xCE, 0x14, 0x00, 0x01, 0xD0,
            0x07, 0x00, 0x00, 0xE8, 0x03, 0x00, 0x00
        ];

        // init.bnk's ENVS chunk, header included.
        private static readonly byte[] WarhammerEnvsChunk =
        [
            0x45, 0x4E, 0x56, 0x53, 0xA8, 0x00, 0x00, 0x00, 0x01, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x25, 0xA6, 0x0D, 0xBF,
            0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0x48, 0x42, 0x04, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00,
            0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0xC8, 0x42, 0x04, 0x00, 0x00, 0x00, 0x00, 0x02, 0x02, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42,
            0xFF, 0xFE, 0x7F, 0xBF, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0xC8, 0x42,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x00, 0xC8, 0x42, 0x04, 0x00, 0x00, 0x00
        ];

        // init.bnk's PLAT chunk, header included.
        private static readonly byte[] WarhammerPlatChunk =
        [
            0x50, 0x4C, 0x41, 0x54, 0x0C, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x57, 0x69, 0x6E, 0x64,
            0x6F, 0x77, 0x73, 0x00
        ];

        // init.bnk's INIT chunk, header included.
        private static readonly byte[] WarhammerInitChunk =
        [
            0x49, 0x4E, 0x49, 0x54, 0x53, 0x01, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x02, 0x04, 0x01, 0x00,
            0x0D, 0x00, 0x00, 0x00, 0x53, 0x77, 0x69, 0x74, 0x63, 0x68, 0x53, 0x65, 0x74, 0x74, 0x65, 0x72,
            0x00, 0x07, 0x04, 0x04, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x56, 0x69, 0x72, 0x74, 0x75, 0x61, 0x6C,
            0x53, 0x69, 0x6E, 0x6B, 0x00, 0x02, 0x00, 0x65, 0x00, 0x13, 0x00, 0x00, 0x00, 0x41, 0x6B, 0x53,
            0x69, 0x6C, 0x65, 0x6E, 0x63, 0x65, 0x47, 0x65, 0x6E, 0x65, 0x72, 0x61, 0x74, 0x6F, 0x72, 0x00,
            0x03, 0x10, 0x67, 0x00, 0x06, 0x00, 0x00, 0x00, 0x4D, 0x63, 0x44, 0x53, 0x50, 0x00, 0x03, 0x00,
            0x69, 0x00, 0x0F, 0x00, 0x00, 0x00, 0x41, 0x6B, 0x50, 0x61, 0x72, 0x61, 0x6D, 0x65, 0x74, 0x72,
            0x69, 0x63, 0x45, 0x51, 0x00, 0x03, 0x00, 0x6A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x41, 0x6B, 0x44,
            0x65, 0x6C, 0x61, 0x79, 0x00, 0x03, 0x00, 0x6C, 0x00, 0x0D, 0x00, 0x00, 0x00, 0x41, 0x6B, 0x43,
            0x6F, 0x6D, 0x70, 0x72, 0x65, 0x73, 0x73, 0x6F, 0x72, 0x00, 0x03, 0x00, 0x6E, 0x00, 0x0E, 0x00,
            0x00, 0x00, 0x41, 0x6B, 0x50, 0x65, 0x61, 0x6B, 0x4C, 0x69, 0x6D, 0x69, 0x74, 0x65, 0x72, 0x00,
            0x03, 0x00, 0x73, 0x00, 0x0F, 0x00, 0x00, 0x00, 0x41, 0x6B, 0x4D, 0x61, 0x74, 0x72, 0x69, 0x78,
            0x52, 0x65, 0x76, 0x65, 0x72, 0x62, 0x00, 0x03, 0x00, 0x76, 0x00, 0x0B, 0x00, 0x00, 0x00, 0x41,
            0x6B, 0x52, 0x6F, 0x6F, 0x6D, 0x56, 0x65, 0x72, 0x62, 0x00, 0x03, 0x00, 0x7F, 0x00, 0x14, 0x00,
            0x00, 0x00, 0x41, 0x6B, 0x43, 0x6F, 0x6E, 0x76, 0x6F, 0x6C, 0x75, 0x74, 0x69, 0x6F, 0x6E, 0x52,
            0x65, 0x76, 0x65, 0x72, 0x62, 0x00, 0x03, 0x00, 0x81, 0x00, 0x11, 0x00, 0x00, 0x00, 0x41, 0x6B,
            0x53, 0x6F, 0x75, 0x6E, 0x64, 0x45, 0x6E, 0x67, 0x69, 0x6E, 0x65, 0x44, 0x4C, 0x4C, 0x00, 0x03,
            0x00, 0x8B, 0x00, 0x07, 0x00, 0x00, 0x00, 0x41, 0x6B, 0x47, 0x61, 0x69, 0x6E, 0x00, 0x07, 0x00,
            0xAE, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x44, 0x65, 0x66, 0x61, 0x75, 0x6C, 0x74, 0x53, 0x69, 0x6E,
            0x6B, 0x00, 0x07, 0x00, 0xB5, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x44, 0x65, 0x66, 0x61, 0x75, 0x6C,
            0x74, 0x53, 0x69, 0x6E, 0x6B, 0x00, 0x02, 0x00, 0xC8, 0x00, 0x0D, 0x00, 0x00, 0x00, 0x41, 0x6B,
            0x41, 0x75, 0x64, 0x69, 0x6F, 0x49, 0x6E, 0x70, 0x75, 0x74, 0x00

        ];

        // The three fields whose width matched and whose type did not. Every bus in Warhammer III
        // authors a 1000 ms recovery; read as a float those bytes are 1.4e-42.
        [Test]
        public void ABusRecoveryTimeAndDuckFadesAreStoredAsSignedMilliseconds()
        {
            var bus = ReadHirc<Shared.GameFormats.Wwise.Hirc.V136.CAkBus_V136>(WarhammerBus, WarhammerBankVersion);

            Assert.That(bus.RecoveryTime, Is.EqualTo(1000));
            Assert.That(bus.MaxDuckVolume, Is.EqualTo(-96f));
            Assert.That(bus.DuckList.Ducks, Has.Count.EqualTo(2));
            Assert.That(bus.DuckList.Ducks[0].DuckVolume, Is.EqualTo(-3f));
            Assert.That(bus.DuckList.Ducks[0].FadeOutTime, Is.EqualTo(100));
            Assert.That(bus.DuckList.Ducks[0].FadeInTime, Is.EqualTo(100));
            Assert.That(bus.DuckList.Ducks[1].DuckVolume, Is.EqualTo(-6f));
        }

        // The bus that did not exist. Attila's banks carry 198 of these and every one of them
        // arrived as an UnknownHircItem, so a V112 game lost its authored bus gains silently.
        [Test]
        public void AnAttilaBusIsReadRatherThanLeftUnknown()
        {
            var hirc = ReadHirc<HircItem>(AttilaBus, AttilaBankVersion);

            Assert.That(hirc, Is.InstanceOf<ICAkBus>());
            var bus = (Shared.GameFormats.Wwise.Hirc.V112.CAkBus_V112)hirc;
            Assert.That(bus.RecoveryTime, Is.EqualTo(1000), "the same integer milliseconds as V136");
            Assert.That(bus.MaxDuckVolume, Is.EqualTo(-96f));
            Assert.That(((ICAkBus)bus).GetOutputBusId(), Is.Not.Zero);
        }

        [Test]
        public void AnAttilaStateUsesTheByteCountPropertyBundle()
        {
            var hirc = ReadHirc<ICAkState>(AttilaState, AttilaBankVersion);

            Assert.That(hirc.GetProperties(), Has.Count.EqualTo(2));
            Assert.That(hirc.GetProperties()[0].PropertyId, Is.EqualTo(0x34));
            Assert.That(hirc.GetProperties()[0].Value, Is.EqualTo(1f));
            Assert.That(hirc.GetProperties()[1].PropertyId, Is.EqualTo(0x78));
            Assert.That(hirc.GetProperties()[1].Value, Is.EqualTo(-2.5f));
        }

        // A V112 bus keeps no positioning or auxiliary parameters -- those reach a bus at 123 --
        // so reading it as a V136 bus would run straight off the end of the object.
        [TestCase(nameof(WarhammerBus), WarhammerBankVersion)]
        [TestCase(nameof(AttilaBus), AttilaBankVersion)]
        public void ABusIsReadToItsLastByte(string fixtureName, uint bankVersion)
        {
            var hirc = ReadHirc<HircItem>(Fixture(fixtureName), bankVersion);

            Assert.That(hirc.UnreadByteCount, Is.Zero);
        }

        // The scaling byte the reader used to skip. A volume curve stores normalised values, so a
        // curve reaching -1 means silence; discarding the scaling made it one decibel down and
        // attenuation all but inaudible.
        [TestCase(nameof(WarhammerAttenuation), WarhammerBankVersion)]
        [TestCase(nameof(AttilaAttenuation), AttilaBankVersion)]
        public void AVolumeCurveIsStoredInDecibelScalingAndAFilterCurveIsNot(string fixtureName, uint bankVersion)
        {
            var attenuation = ReadHirc<ICAkAttenuation>(Fixture(fixtureName), bankVersion);

            Assert.That(attenuation.GetCurve(AttenuationCurveType.Volume).Scaling, Is.EqualTo(AkCurveScaling.Decibels));
            var lowPass = attenuation.GetCurve(AttenuationCurveType.LowPassFilter);
            if (lowPass.Count != 0)
                Assert.That(lowPass.Scaling, Is.EqualTo(AkCurveScaling.None));
        }

        [Test]
        public void DecibelScalingTurnsAFullyAttenuatedCurveIntoSilence()
        {
            var curve = new WwiseCurve(AkCurveScaling.Decibels, []);

            Assert.That(curve.Scale(0f), Is.EqualTo(0f).Within(0.001f), "no attenuation is no decibels");
            Assert.That(curve.Scale(-1f), Is.EqualTo(WwiseCurve.MinimumDecibels), "full attenuation is silence");
            Assert.That(curve.Scale(-0.5f), Is.EqualTo(-6.02f).Within(0.01f));
            Assert.That(new WwiseCurve(AkCurveScaling.None, []).Scale(-1f), Is.EqualTo(-1f), "an unscaled curve is its own value");
        }

        // Reading the slots the other way round is the mistake this guards: a filter curve runs to
        // 100 and a volume curve runs to -1, so a swap is loud rather than subtle.
        [Test]
        public void TheVolumeSlotHoldsDecibelsAndTheFilterSlotsHoldPercentages()
        {
            var attenuation = ReadHirc<ICAkAttenuation>(WarhammerAttenuation, WarhammerBankVersion);
            var volume = attenuation.GetCurve(AttenuationCurveType.Volume);

            Assert.That(volume.Count, Is.GreaterThan(1));
            var nearest = volume.Scale(volume.Points[0].To);
            var furthest = volume.Scale(volume.Points[^1].To);
            Assert.That(furthest, Is.LessThan(nearest), "a sound gets quieter with distance");
            Assert.That(furthest, Is.LessThan(-10f), "and at maximum distance it is well down, not a decibel down");
            Assert.That(nearest, Is.LessThanOrEqualTo(0f).And.GreaterThan(WwiseCurve.MinimumDecibels), "decibels, not a normalised value");
            foreach (var point in attenuation.GetCurve(AttenuationCurveType.LowPassFilter).Points)
                Assert.That(point.To, Is.InRange(0f, 100f), "a filter curve is a percentage");
        }

        [Test]
        public void TheEnvironmentChunkHoldsSixObstructionAndOcclusionCurves()
        {
            var envs = EnvsChunk.ReadData("init.bnk", new ByteChunk(WarhammerEnvsChunk));

            Assert.That(envs.Curves, Has.Count.EqualTo(6));
            Assert.That(envs.Curves, Has.All.Property(nameof(EnvsChunk.ObstructionOcclusionCurve.PointCount)).EqualTo(2));
        }

        [Test]
        public void ThePlatformChunkNamesThePlatformTheBankWasBuiltFor()
        {
            var plat = PlatChunk.ReadData("init.bnk", new ByteChunk(WarhammerPlatChunk));

            Assert.That(plat.CustomPlatformName, Is.EqualTo("Windows"));
        }

        [Test]
        public void ThePluginChunkListsThePluginsTheGameRegisters()
        {
            var init = InitChunk.ReadData("init.bnk", new ByteChunk(WarhammerInitChunk));

            Assert.That(init.Plugins, Has.Count.EqualTo(16));
            Assert.That(init.Plugins.Select(plugin => plugin.DllName), Has.Member("SwitchSetter"));
            Assert.That(init.Plugins, Has.None.Property(nameof(InitChunk.PluginEntry.PluginId)).Zero);
        }

        // Found the same way as the bus fields and just as invisible to a byte count -- but this
        // one reaches the engine: NodeWalker feeds these to phase 12's continuous switch
        // cross-fade, so every authored fade was arriving as zero milliseconds.
        [Test]
        public void SwitchCrossFadeTimesAreStoredAsSignedMilliseconds()
        {
            var container = ReadHirc<Shared.GameFormats.Wwise.Hirc.V136.CAkSwitchCntr_V136>(WarhammerSwitchContainer, WarhammerBankVersion);

            Assert.That(container.Parameters, Has.Count.EqualTo(2));
            Assert.That(container.Parameters[0].FadeOutTime, Is.EqualTo(5000));
            Assert.That(container.Parameters[0].FadeInTime, Is.EqualTo(2000));
            Assert.That(container.Parameters[1].FadeOutTime, Is.EqualTo(2000));
            Assert.That(container.Parameters[1].FadeInTime, Is.EqualTo(1000));
            Assert.That(container.UnreadByteCount, Is.Zero);
        }

        [TestCase(0u, new byte[] { 0x00 })]
        [TestCase(0x7Fu, new byte[] { 0x7F })]
        [TestCase(0x80u, new byte[] { 0x81, 0x00 })]
        [TestCase(0x3FFFu, new byte[] { 0xFF, 0x7F })]
        [TestCase(0x4000u, new byte[] { 0x81, 0x80, 0x00 })]
        [TestCase(uint.MaxValue, new byte[] { 0x8F, 0xFF, 0xFF, 0xFF, 0x7F })]
        public void WwiseVariableUInt32IntegersUseMostSignificantGroupFirst(uint value, byte[] encoded)
        {
            Assert.That(WwiseVariableUInt32Parser.Encode(value), Is.EqualTo(encoded));
            Assert.That(WwiseVariableUInt32Parser.GetSize(value), Is.EqualTo((uint)encoded.Length));
            Assert.That(WwiseVariableUInt32Parser.Read(new ByteChunk(encoded)), Is.EqualTo(value));
        }

        [Test]
        public void RandomContainerPlaylistWeightsAreSignedInt32()
        {
            byte[] encoded = [0x78, 0x56, 0x34, 0x12, 0xFF, 0xFF, 0xFF, 0xFF];

            var v112 = Shared.GameFormats.Wwise.Hirc.V112.CAkRanSeqCntr_V112.CAkPlayList_V112.AkPlaylistItem_V112.ReadData(new ByteChunk(encoded));
            var v136 = Shared.GameFormats.Wwise.Hirc.V136.CAkRanSeqCntr_V136.CAkPlayList_V136.AkPlaylistItem_V136.ReadData(new ByteChunk(encoded));

            Assert.Multiple(() =>
            {
                Assert.That(v112.Weight, Is.EqualTo(-1));
                Assert.That(v112.WriteData(), Is.EqualTo(encoded));
                Assert.That(v136.Weight, Is.EqualTo(-1));
                Assert.That(v136.WriteData(), Is.EqualTo(encoded));
            });
        }

        [Test]
        public void AttilaUsesThePre128HircTypeTable()
        {
            byte[] hircBytes = [0x12, 0x04, 0x00, 0x00, 0x00, 0x78, 0x56, 0x34, 0x12];

            var hirc = ReadHirc<HircItem>(hircBytes, AttilaBankVersion);

            Assert.That(hirc, Is.InstanceOf<UnknownHircItem>(), "the FX payload is deliberately unsupported");
            Assert.That(hirc.HircType, Is.EqualTo(Shared.GameFormats.Wwise.Enums.AkBkHircType.FxShareSet));
            Assert.That(hirc.Header.RawHircType, Is.EqualTo(0x12));
            Assert.That(hirc.UnreadByteCount, Is.Zero);
        }

        [Test]
        public void PublicVersion135IsNotTreatedAsTheCaPseudoVersion136()
        {
            Assert.That(
                () => HircFactory.CreateFactory(135),
                Throws.Exception.Message.EqualTo("Unknown Bank Generator Version: 135"));
        }

        [Test]
        public void WwiseSamplerLoopUsesAnInclusiveFileEndAndAnExclusiveRuntimeEnd()
        {
            var values = new uint[]
            {
                0, 0, 0, 60, 0, 0, 0, 1, 0,
                7, 0, 100, 299, 0, 0
            };
            var payload = values.SelectMany(BitConverter.GetBytes).ToArray();
            var chunk = new SmplChunk();

            chunk.ReadChunk(new ByteChunk(payload));

            Assert.That(chunk.HasForwardLoop, Is.True);
            Assert.That(chunk.LoopStartFrame, Is.EqualTo(100));
            Assert.That(chunk.LoopEndFrame, Is.EqualTo(300));
            Assert.That(RiffChunkFactory.CreateChunk(SmplChunk.ChunkTag), Is.TypeOf<SmplChunk>());
        }

        [Test]
        public void SharedWwiseCodebooksCannotBeMutatedThroughAConsumerCopy()
        {
            var library = new WwiseCodebookLibrary();
            var first = library.GetCodebook(0);
            var originalByte = first.Data[0];
            first.Data[0] ^= 0xFF;

            var second = library.GetCodebook(0);

            Assert.That(second.Data[0], Is.EqualTo(originalByte));
            Assert.That(library.FindLibraryId(second.Data, second.BitCount), Is.Zero);
        }

        // BnkFile refused every tag outside BKHD/HIRC/DIDX/DATA/STID, which is why init.bnk -- the
        // only bank that holds buses -- could not be opened at all. It checks each chunk against
        // the size the bank states and throws on a mismatch, so parsing whole is the assertion.
        [TestCase(WarhammerBankVersion, true)]
        [TestCase(AttilaBankVersion, false)]
        public void AnInitBankParsesWholeOnBothVersions(uint bankVersion, bool hasPluginAndPlatformChunks)
        {
            var bank = BnkFile.CreateFromBytes(BuildInitBank(bankVersion, hasPluginAndPlatformChunks), "init.bnk", isCA: true);

            Assert.That(bank.StmgChunk, Is.Not.Null);
            Assert.That(bank.EnvsChunk, Is.Not.Null);
            Assert.That(bank.StmgChunk!.StateGroups, Has.Count.EqualTo(1));
            Assert.That(bank.StmgChunk.SwitchGroups, Has.Count.EqualTo(1));
            Assert.That(bank.StmgChunk.GameParameters, Has.Count.EqualTo(1));

            // INIT arrives at bank version 118 and PLAT at 113, so a V112 init bank correctly has
            // neither. Reading them anyway would run off the end of the chunk that follows.
            Assert.That(bank.InitChunk, hasPluginAndPlatformChunks ? Is.Not.Null : Is.Null);
            Assert.That(bank.PlatChunk, hasPluginAndPlatformChunks ? Is.Not.Null : Is.Null);
        }

        // The one field in STMG whose presence depends on the version, and the reason a shared
        // reader has to know which version it is reading.
        [Test]
        public void TheDangerousVirtualVoiceLimitIsPresentOnlyFromVersion127()
        {
            var warhammer = BnkFile.CreateFromBytes(BuildInitBank(WarhammerBankVersion, true), "init.bnk", true);
            var attila = BnkFile.CreateFromBytes(BuildInitBank(AttilaBankVersion, false), "init.bnk", true);

            Assert.That(warhammer.StmgChunk!.MaxVoiceLimit, Is.EqualTo(225));
            Assert.That(warhammer.StmgChunk.MaxDangerousVirtualVoiceLimit, Is.EqualTo(50));
            Assert.That(attila.StmgChunk!.MaxVoiceLimit, Is.EqualTo(225));
            Assert.That(attila.StmgChunk.MaxDangerousVirtualVoiceLimit, Is.Zero, "the field is not in the bank to be read");
        }

        // A bank the reader does not recognise still fails loudly. Skipping an unknown chunk would
        // hide the next gap the way the missing buses were hidden.
        [Test]
        public void AnUnknownChunkIsRefusedRatherThanSkipped()
        {
            var bank = new List<byte>();
            AppendChunk(bank, "BKHD", BankHeader(WarhammerBankVersion));
            AppendChunk(bank, "FXPR", [0x00, 0x00, 0x00, 0x00]);

            Assert.That(
                () => BnkFile.CreateFromBytes([.. bank], "odd.bnk", true),
                Throws.TypeOf<ArgumentException>());
        }

        private static byte[] BuildInitBank(uint bankVersion, bool hasPluginAndPlatformChunks)
        {
            var bank = new List<byte>();
            AppendChunk(bank, "BKHD", BankHeader(bankVersion));
            if (hasPluginAndPlatformChunks)
                bank.AddRange(WarhammerInitChunk);
            AppendChunk(bank, "STMG", SettingsChunk(bankVersion));
            bank.AddRange(WarhammerEnvsChunk);
            if (hasPluginAndPlatformChunks)
                bank.AddRange(WarhammerPlatChunk);
            return [.. bank];
        }

        private static byte[] BankHeader(uint bankVersion)
        {
            var header = new List<byte>();
            header.AddRange(BitConverter.GetBytes(bankVersion));
            for (var field = 0; field < 4; field++)
                header.AddRange(BitConverter.GetBytes(0u));
            return [.. header];
        }

        // One of each thing STMG holds, so the walk over it is exercised without embedding the
        // twelve kilobytes the real chunk runs to.
        private static byte[] SettingsChunk(uint bankVersion)
        {
            var settings = new List<byte>();
            settings.AddRange(BitConverter.GetBytes(-60f));
            settings.AddRange(BitConverter.GetBytes((ushort)225));
            if (BankVersion.Normalise(bankVersion) >= 127)
                settings.AddRange(BitConverter.GetBytes((ushort)50));

            settings.AddRange(BitConverter.GetBytes(1u));            // one state group
            settings.AddRange(BitConverter.GetBytes(0x11111111u));   // its id
            settings.AddRange(BitConverter.GetBytes(1000u));         // default transition time
            settings.AddRange(BitConverter.GetBytes(1u));            // one transition
            settings.AddRange(BitConverter.GetBytes(0x22222222u));
            settings.AddRange(BitConverter.GetBytes(0x33333333u));
            settings.AddRange(BitConverter.GetBytes(500u));

            settings.AddRange(BitConverter.GetBytes(1u));            // one switch group
            settings.AddRange(BitConverter.GetBytes(0x44444444u));
            settings.AddRange(BitConverter.GetBytes(0x55555555u));
            settings.Add(0);                                          // rtpc type
            settings.AddRange(BitConverter.GetBytes(1u));            // one graph point
            settings.AddRange(BitConverter.GetBytes(0f));
            settings.AddRange(BitConverter.GetBytes(1f));
            settings.AddRange(BitConverter.GetBytes(4u));

            settings.AddRange(BitConverter.GetBytes(1u));            // one game parameter
            settings.AddRange(BitConverter.GetBytes(0x66666666u));
            settings.AddRange(BitConverter.GetBytes(0.5f));
            settings.AddRange(BitConverter.GetBytes(0u));
            settings.AddRange(BitConverter.GetBytes(0f));
            settings.AddRange(BitConverter.GetBytes(0f));
            settings.Add(0);

            if (BankVersion.Normalise(bankVersion) >= 123)
                settings.AddRange(BitConverter.GetBytes(0u));        // no acoustic textures
            return [.. settings];
        }

        private static void AppendChunk(List<byte> bank, string tag, byte[] payload)
        {
            bank.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));
            bank.AddRange(BitConverter.GetBytes((uint)payload.Length));
            bank.AddRange(payload);
        }

        private static byte[] Fixture(string fixtureName) => fixtureName switch
        {
            nameof(WarhammerBus) => WarhammerBus,
            nameof(AttilaBus) => AttilaBus,
            nameof(WarhammerAttenuation) => WarhammerAttenuation,
            nameof(AttilaAttenuation) => AttilaAttenuation,
            _ => throw new ArgumentException($"There is no fixture called {fixtureName}.", nameof(fixtureName))
        };

        private static T ReadHirc<T>(byte[] hircBytes, uint bankVersion) where T : class
        {
            var hircItem = HircItem.ReadData("fixture.bnk", new ByteChunk(hircBytes), bankVersion, 0, true, 0);
            Assert.That(hircItem.HasError, Is.False);
            return hircItem as T ?? throw new InvalidOperationException($"The fixture is a {hircItem.GetType().Name}, not a {typeof(T).Name}.");
        }
    }
}
