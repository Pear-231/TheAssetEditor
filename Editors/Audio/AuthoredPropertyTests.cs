using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Hirc;

namespace Test.Audio
{
    // How a bank stores a property value was an open question until 2026-08-30, when it was settled
    // against every HIRC object in Warhammer III's audio_base_bnk.pack (244 banks) and Attila's
    // sound.pack (51 banks). The bytes below are copied out of those banks rather than produced by
    // this project's bank writer, which is the point: a fixture the writer generates only proves the
    // reader and the writer agree with each other.
    public class AuthoredPropertyTests
    {
        private const uint WarhammerBankVersion = 2147483784;
        private const uint AttilaBankVersion = 112;

        // battle_environment__core.bnk, sound 330842534. Seven mixing properties, a loop count, an
        // unnamed property, and a pitch randomised within a range.
        private static readonly byte[] WarhammerSound =
        [
            0x02, 0x66, 0x00, 0x00, 0x00, 0xA6, 0x41, 0xB8, 0x13, 0x01, 0x00, 0x04, 0x00, 0x02, 0x10, 0x87,
            0x3A, 0x27, 0xD9, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x41, 0x2F,
            0x31, 0x02, 0x02, 0x09, 0x00, 0x02, 0x04, 0x06, 0x07, 0x08, 0x17, 0x3A, 0x49, 0x00, 0x00, 0x88,
            0xC1, 0x00, 0x00, 0xA0, 0x42, 0x00, 0x00, 0x50, 0x41, 0x00, 0x00, 0x40, 0x40, 0x00, 0x00, 0x82,
            0x42, 0x00, 0x00, 0x0C, 0xC2, 0x00, 0x00, 0x90, 0xC1, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
            0x00, 0x01, 0x02, 0x00, 0x00, 0xC8, 0xC2, 0x00, 0x00, 0xC8, 0x42, 0x00, 0x02, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x01, 0x00, 0x00, 0x01, 0x04, 0x00, 0x00, 0x00, 0x00
        ];

        // battle_group_vocalisation_beastmen__core.bnk, action 848535596. A delay of 500 ms randomised
        // by plus or minus 500 ms: the minimum is negative, which no float reading of those bytes is.
        private static readonly byte[] WarhammerActionWithDelayRange =
        [
            0x03, 0x2C, 0x00, 0x00, 0x00, 0x2C, 0xA0, 0x93, 0x32, 0x03, 0x01, 0x4C, 0x22, 0x25, 0x1F, 0x00,
            0x02, 0x0F, 0x10, 0xF4, 0x01, 0x00, 0x00, 0xE8, 0x03, 0x00, 0x00, 0x02, 0x0F, 0x10, 0x0C, 0xFE,
            0xFF, 0xFF, 0xF4, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF4, 0x01, 0x00, 0x00, 0x07, 0x06,
            0x00
        ];

        // campaign.bnk, sound 865624566. Attila numbers its properties differently -- volume is the
        // same id, but priority is 0x06 here and 0x07 in Warhammer III -- and stores them identically.
        private static readonly byte[] AttilaSound =
        [
            0x02, 0x6F, 0x00, 0x00, 0x00, 0xF6, 0x61, 0x98, 0x33, 0x01, 0x00, 0x04, 0x00, 0x00, 0x5F, 0x49,
            0x73, 0x10, 0x2E, 0xAD, 0x1C, 0x47, 0x80, 0x5F, 0xC6, 0x00, 0x43, 0x78, 0x08, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x62, 0xAB, 0xF3, 0x1B, 0x00, 0x02, 0x00, 0x3A, 0x00, 0x00,
            0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x00, 0x00, 0x48, 0xC3, 0x00, 0x00, 0xC8, 0x42,
            0xC0, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x25, 0xFB,
            0x91, 0xA2, 0x00, 0x01, 0x00, 0x43, 0x69, 0x95, 0x1D, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0xBF,
            0x04, 0x00, 0x00, 0x00
        ];

        // battle_environment.bnk, action 289173196. A delay of 500 ms randomised from 0 to 1500 ms.
        // Attila authors only four ranged actions in the whole of sound.pack, and this is one of them.
        private static readonly byte[] AttilaActionWithDelayRange =
        [
            0x03, 0x20, 0x00, 0x00, 0x00, 0xCC, 0x6E, 0x3C, 0x11, 0x03, 0x04, 0xF4, 0x8C, 0x0E, 0x02, 0x00,
            0x01, 0x0E, 0xF4, 0x01, 0x00, 0x00, 0x01, 0x0E, 0x00, 0x00, 0x00, 0x00, 0xDC, 0x05, 0x00, 0x00,
            0x04, 0x61, 0xD8, 0xB1, 0xCF
        ];

        [Test]
        public void MixingPropertiesAreStoredAsFloats()
        {
            var properties = ReadHirc<ICAkParameterNode>(WarhammerSound, WarhammerBankVersion).GetProperties();

            AssertNumber(properties, WwiseProperty.Volume, -17f);
            AssertNumber(properties, WwiseProperty.Pitch, 80f);
            AssertNumber(properties, WwiseProperty.HighPassFilter, 13f);
            AssertNumber(properties, WwiseProperty.MakeUpGain, 3f);
            AssertNumber(properties, WwiseProperty.Priority, 65f);
            AssertNumber(properties, WwiseProperty.PriorityDistanceOffset, -35f);
            AssertNumber(properties, WwiseProperty.GameAuxSendVolume, -18f);
        }

        [Test]
        public void AnOlderBankStoresMixingPropertiesTheSameWay()
        {
            var properties = ReadHirc<ICAkParameterNode>(AttilaSound, AttilaBankVersion).GetProperties();

            AssertNumber(properties, WwiseProperty.Volume, -2f);
        }

        [TestCase(nameof(WarhammerSound))]
        [TestCase(nameof(AttilaSound))]
        public void ALoopCountIsStoredAsAnInteger(string fixtureName)
        {
            var (fixtureBytes, bankVersion) = Fixture(fixtureName);
            var properties = ReadHirc<ICAkParameterNode>(fixtureBytes, bankVersion).GetProperties();

            Assert.That(properties.TryGetValue(WwiseProperty.LoopCount, out var loopCount), Is.True);
            Assert.That(loopCount.Storage, Is.EqualTo(WwisePropertyStorage.Count));
            Assert.That(loopCount.Count, Is.EqualTo(0));
        }

        [TestCase(nameof(WarhammerSound), -100f, 100f)]
        [TestCase(nameof(AttilaSound), -200f, 100f)]
        public void ARandomisedPropertyCarriesItsRange(string fixtureName, float expectedMinimum, float expectedMaximum)
        {
            var (fixtureBytes, bankVersion) = Fixture(fixtureName);
            var properties = ReadHirc<ICAkParameterNode>(fixtureBytes, bankVersion).GetProperties();

            Assert.That(properties.TryGetRange(WwiseProperty.Pitch, out var minimum, out var maximum), Is.True);
            Assert.That(minimum.Number, Is.EqualTo(expectedMinimum));
            Assert.That(maximum.Number, Is.EqualTo(expectedMaximum));
        }

        [TestCase(nameof(WarhammerActionWithDelayRange), 500, -500, 500)]
        [TestCase(nameof(AttilaActionWithDelayRange), 500, 0, 1500)]
        public void AnActionDelayIsStoredAsSignedMilliseconds(string fixtureName, int expectedDelay, int expectedMinimum, int expectedMaximum)
        {
            var (fixtureBytes, bankVersion) = Fixture(fixtureName);
            var properties = ReadHirc<ICAkAction>(fixtureBytes, bankVersion).GetProperties();

            Assert.That(properties.TryGetValue(WwiseProperty.ActionDelay, out var delay), Is.True);
            Assert.That(delay.Storage, Is.EqualTo(WwisePropertyStorage.Milliseconds));
            Assert.That(delay.Milliseconds, Is.EqualTo(expectedDelay));

            Assert.That(properties.TryGetRange(WwiseProperty.ActionDelay, out var minimum, out var maximum), Is.True);
            Assert.That(minimum.Milliseconds, Is.EqualTo(expectedMinimum));
            Assert.That(maximum.Milliseconds, Is.EqualTo(expectedMaximum));
        }

        [Test]
        public void AnActionTransitionIsStoredAsMilliseconds()
        {
            var properties = ReadHirc<ICAkAction>(WarhammerActionWithDelayRange, WarhammerBankVersion).GetProperties();

            Assert.That(properties.TryGetValue(WwiseProperty.TransitionTime, out var transition), Is.True);
            Assert.That(transition.Milliseconds, Is.EqualTo(1000));

            Assert.That(properties.TryGetRange(WwiseProperty.TransitionTime, out var minimum, out var maximum), Is.True);
            Assert.That(minimum.Milliseconds, Is.EqualTo(0));
            Assert.That(maximum.Milliseconds, Is.EqualTo(500));
        }

        [Test]
        public void ReadingAPropertyThroughTheWrongStorageIsRefused()
        {
            var properties = ReadHirc<ICAkParameterNode>(WarhammerSound, WarhammerBankVersion).GetProperties();
            properties.TryGetValue(WwiseProperty.Volume, out var volume);

            Assert.Throws<InvalidOperationException>(() => _ = volume.Milliseconds);
        }

        // Reading repositions to the section size the bank states, so a field read at the wrong width
        // leaves no trace in the parsed object. `UnreadByteCount` is what makes it visible, and it is
        // what proved the action range bundle holds a minimum and a maximum per property: read as one
        // value each, every ranged action here stops four bytes short of its own end.
        [TestCase(nameof(WarhammerSound))]
        [TestCase(nameof(WarhammerActionWithDelayRange))]
        [TestCase(nameof(AttilaSound))]
        [TestCase(nameof(AttilaActionWithDelayRange))]
        public void EveryFixtureIsReadToItsLastByte(string fixtureName)
        {
            var (fixtureBytes, bankVersion) = Fixture(fixtureName);
            var hircItem = HircItem.ReadData("fixture.bnk", new ByteChunk(fixtureBytes), bankVersion, 0, true, 0);

            Assert.That(hircItem.UnreadByteCount, Is.EqualTo(0));
            Assert.That(fixtureBytes.Length, Is.EqualTo(HircHeader.PrefixSize + hircItem.SectionSize));
        }

        // The writer's side of the same question: what the model believes it holds has to add back up
        // to what the bank stated. Only the Warhammer action can take this check -- the sounds author
        // auxiliary sends, and both versions refuse to size those.
        [Test]
        public void TheActionModelAccountsForEveryByteTheBankStates()
        {
            var action = HircItem.ReadData("fixture.bnk", new ByteChunk(WarhammerActionWithDelayRange), WarhammerBankVersion, 0, true, 0);
            var statedSize = action.SectionSize;
            action.UpdateSectionSize();

            Assert.That(action.SectionSize, Is.EqualTo(statedSize));
        }

        private static (byte[] Bytes, uint BankVersion) Fixture(string fixtureName) => fixtureName switch
        {
            nameof(WarhammerSound) => (WarhammerSound, WarhammerBankVersion),
            nameof(WarhammerActionWithDelayRange) => (WarhammerActionWithDelayRange, WarhammerBankVersion),
            nameof(AttilaSound) => (AttilaSound, AttilaBankVersion),
            nameof(AttilaActionWithDelayRange) => (AttilaActionWithDelayRange, AttilaBankVersion),
            _ => throw new ArgumentException($"There is no fixture called {fixtureName}.", nameof(fixtureName))
        };

        private static void AssertNumber(AuthoredProperties properties, WwiseProperty property, float expected)
        {
            Assert.That(properties.TryGetValue(property, out var value), Is.True, $"{property} was not authored.");
            Assert.That(value.Storage, Is.EqualTo(WwisePropertyStorage.Number));
            Assert.That(value.Number, Is.EqualTo(expected));
        }

        private static T ReadHirc<T>(byte[] hircBytes, uint bankVersion) where T : class
        {
            var hircItem = HircItem.ReadData("fixture.bnk", new ByteChunk(hircBytes), bankVersion, 0, true, 0);
            Assert.That(hircItem.HasError, Is.False);
            return hircItem as T ?? throw new InvalidOperationException($"The fixture is a {hircItem.GetType().Name}, not a {typeof(T).Name}.");
        }
    }
}
