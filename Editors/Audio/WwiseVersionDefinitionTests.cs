using Shared.ByteParsing;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V112;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Versions;
using Shared.GameFormats.Wwise.Versions.V112;
using Shared.GameFormats.Wwise.Versions.V136;

namespace Test.Audio
{
    public class WwiseVersionDefinitionTests
    {
        [Test]
        public void RawBankVersionsResolveToTheirImmutableDefinitions()
        {
            var attila = WwiseVersionResolver.Resolve(BankVersion.Attila);
            var warhammer = WwiseVersionResolver.Resolve(BankVersion.WarhammerIII);

            Assert.Multiple(() =>
            {
                Assert.That(attila, Is.SameAs(WwiseV112Definition.Instance));
                Assert.That(attila.RawBankGeneratorVersion, Is.EqualTo(BankVersion.Attila));
                Assert.That(attila.SchemaVersion, Is.EqualTo(112));
                Assert.That(attila.DisplayName, Is.EqualTo("V112"));
                Assert.That(warhammer, Is.SameAs(WwiseCaV136Definition.Instance));
                Assert.That(warhammer.RawBankGeneratorVersion, Is.EqualTo(BankVersion.WarhammerIII));
                Assert.That(warhammer.SchemaVersion, Is.EqualTo(136));
                Assert.That(warhammer.DisplayName, Is.EqualTo("CA-pseudo-V136"));
            });
        }

        [Test]
        public void HircTypeBytesAreDecodedAndEncodedByVersion()
        {
            var attila = WwiseV112Definition.Instance;
            var warhammer = WwiseCaV136Definition.Instance;

            Assert.Multiple(() =>
            {
                Assert.That(attila.DecodeHircType(0x12), Is.EqualTo(AkBkHircType.FxShareSet));
                Assert.That(warhammer.DecodeHircType(0x12), Is.EqualTo(AkBkHircType.AuxiliaryBus));
                Assert.That(attila.EncodeHircType(AkBkHircType.FxShareSet), Is.EqualTo(0x12));
                Assert.That(warhammer.EncodeHircType(AkBkHircType.FxShareSet), Is.EqualTo(0x10));
            });
        }

        [Test]
        public void HircRegistrationsBelongToTheVersionDefinition()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WwiseV112Definition.Instance.CreateHirc(AkBkHircType.Sound), Is.TypeOf<CAkSound_V112>());
                Assert.That(WwiseCaV136Definition.Instance.CreateHirc(AkBkHircType.Sound), Is.TypeOf<CAkSound_V136>());
                Assert.That(WwiseV112Definition.Instance.CreateHirc(AkBkHircType.TimeMod), Is.TypeOf<UnknownHircItem>());
                Assert.That(WwiseCaV136Definition.Instance.CreateHirc(AkBkHircType.TimeMod), Is.TypeOf<CAkTimeModulator_V136>());
            });
        }

        [Test]
        public void VersionDefinitionsOwnActionPropertiesAndFeatureDifferences()
        {
            var attila = WwiseV112Definition.Instance;
            var warhammer = WwiseCaV136Definition.Instance;

            Assert.Multiple(() =>
            {
                Assert.That(attila.ClassifyAction(AkActionType.Stop_E), Is.EqualTo(AkActionParameter.Active));
                Assert.That(warhammer.ClassifyAction(AkActionType.Stop_E), Is.EqualTo(AkActionParameter.ActiveWithSpecificParameter));
                Assert.That(attila.ClassifyAction(AkActionType.Play), Is.EqualTo(AkActionParameter.Play));
                Assert.That(warhammer.ClassifyAction(AkActionType.Play), Is.EqualTo(AkActionParameter.Play));

                Assert.That(attila.TryMapProperty(0x06, out var attilaProperty), Is.True);
                Assert.That(attilaProperty, Is.EqualTo(WwiseProperty.Priority));
                Assert.That(warhammer.TryMapProperty(0x06, out var warhammerProperty), Is.True);
                Assert.That(warhammerProperty, Is.EqualTo(WwiseProperty.MakeUpGain));

                Assert.That(attila.UsesVariableActionExceptionCount, Is.False);
                Assert.That(attila.HasDangerousVirtualVoiceLimit, Is.False);
                Assert.That(attila.HasAcousticTextures, Is.False);
                Assert.That(warhammer.UsesVariableActionExceptionCount, Is.True);
                Assert.That(warhammer.HasDangerousVirtualVoiceLimit, Is.True);
                Assert.That(warhammer.HasAcousticTextures, Is.True);
            });
        }

        [Test]
        public void IndexedHircHeadersUseTheSelectedVersionsTypeTable()
        {
            byte[] hircPayload =
            [
                0x01, 0x00, 0x00, 0x00,
                0x12, 0x04, 0x00, 0x00, 0x00, 0x78, 0x56, 0x34, 0x12
            ];

            var attilaEntry = HircChunk.BuildIndex(100, (uint)hircPayload.Length, new ByteChunk(hircPayload), WwiseV112Definition.Instance).Single();
            var warhammerEntry = HircChunk.BuildIndex(100, (uint)hircPayload.Length, new ByteChunk(hircPayload), WwiseCaV136Definition.Instance).Single();

            Assert.Multiple(() =>
            {
                Assert.That(attilaEntry.Header.RawHircType, Is.EqualTo(0x12));
                Assert.That(attilaEntry.Header.HircType, Is.EqualTo(AkBkHircType.FxShareSet));
                Assert.That(warhammerEntry.Header.RawHircType, Is.EqualTo(0x12));
                Assert.That(warhammerEntry.Header.HircType, Is.EqualTo(AkBkHircType.AuxiliaryBus));
            });
        }
    }
}
