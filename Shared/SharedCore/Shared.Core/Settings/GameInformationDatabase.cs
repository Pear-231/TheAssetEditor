using Shared.Core.PackFiles.Utility;

namespace Shared.Core.Settings
{
    public enum GameTypeEnum
    {
        Unknown = -1,
        Arena = 0,
        Attila,
        Empire,
        Napoleon,
        RomeRemastered,
        Rome2,
        Shogun2,
        ThreeKingdoms,
        ThronesOfBritannia,
        Warhammer,
        Warhammer2,
        Warhammer3,
        Troy,
        Pharaoh,
        Dragonfruit
    }

    public enum GameBnkVersion : uint
    {
        Unsupported = 0,
        Warhammer3 = 2147483784,
        Attila = 112,
        Dragonfruit = 2147483798
    }

    public enum WwiseProjectId : uint
    {
        Unsupported = 0,
        Warhammer3 = 2361,
        Dragonfruit = 11883
    }

    // No game is known to use use PFH6 or PFH7
    public enum PackFileVersion
    {
        PFH0,
        PFH2,
        PFH3,
        PFH4,
        PFH5,
        PFH6,
        PFH7,
        PFH8
    }

    public enum WsModelVersion
    {
        Unknown = 0,
        Version1,
        Version2,
        Version3,
    }

    public enum EncryptionKeystream
    {
        Unknown = 0,
        None,
        ThirtyTwoBitComplement,
        SixtyFourBitComplement,
    }

    public class GameInformation(
        GameTypeEnum gameType,
        string displayName,
        PackFileVersion packFileVersion,
        GameBnkVersion bankGeneratorVersion,
        WwiseProjectId wwiseProjectId,
        WsModelVersion wsModelVersion,
        List<CompressionFormat> compressionFormats,
        EncryptionKeystream encryptionKeystream)
    {
        public GameTypeEnum Type { get; } = gameType;
        public string DisplayName { get; } = displayName;
        public PackFileVersion PackFileVersion { get; } = packFileVersion;
        public GameBnkVersion BankGeneratorVersion { get; } = bankGeneratorVersion;
        public WwiseProjectId WwiseProjectId { get; } = wwiseProjectId;
        public WsModelVersion WsModelVersion { get; } = wsModelVersion;
        public List<CompressionFormat> CompressionFormats { get; } = compressionFormats;
        public EncryptionKeystream EncryptionKeystream { get; } = encryptionKeystream;
    }

    public static class GameInformationDatabase
    {
        public static Dictionary<GameTypeEnum, GameInformation> Games { get; private set; }

        static GameInformationDatabase()
        {
            var rome2 = new GameInformation(
                GameTypeEnum.Rome2,
                "Rome II",
                PackFileVersion.PFH4,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.None],
                EncryptionKeystream.ThirtyTwoBitComplement);

            var attila = new GameInformation(
                GameTypeEnum.Attila,
                "Attila",
                PackFileVersion.PFH4,
                GameBnkVersion.Attila,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.None],
                EncryptionKeystream.ThirtyTwoBitComplement);

            var warhammer = new GameInformation(
                GameTypeEnum.Warhammer,
                "Warhammer",
                PackFileVersion.PFH4,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.None],
                EncryptionKeystream.SixtyFourBitComplement);

            var warhammer2 = new GameInformation(
                GameTypeEnum.Warhammer2,
                "Warhammer II",
                PackFileVersion.PFH5,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Version1,
                [CompressionFormat.Lzma1],
                EncryptionKeystream.SixtyFourBitComplement);

            var threeKingdoms = new GameInformation(
                GameTypeEnum.ThreeKingdoms,
                "Three Kingdoms",
                PackFileVersion.PFH5,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Version1,
                [CompressionFormat.Lzma1],
                EncryptionKeystream.None);

            var troy = new GameInformation(
                GameTypeEnum.Troy,
                "Troy",
                PackFileVersion.PFH5,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.Lzma1],
                EncryptionKeystream.SixtyFourBitComplement);

            var warhammer3 = new GameInformation(
                GameTypeEnum.Warhammer3,
                "Warhammer III",
                PackFileVersion.PFH5,
                GameBnkVersion.Warhammer3,
                WwiseProjectId.Warhammer3,
                WsModelVersion.Version3,
                [CompressionFormat.Lzma1, CompressionFormat.Lz4, CompressionFormat.Zstd],
                EncryptionKeystream.SixtyFourBitComplement);

            var pharaoh = new GameInformation(
                GameTypeEnum.Pharaoh,
                "Pharaoh",
                PackFileVersion.PFH5,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.Lzma1],
                EncryptionKeystream.SixtyFourBitComplement);

            var dragonfruit = new GameInformation(
                GameTypeEnum.Dragonfruit,
                "Dragonfruit",
                PackFileVersion.PFH8,
                GameBnkVersion.Dragonfruit,
                WwiseProjectId.Dragonfruit,
                WsModelVersion.Version2,
                [CompressionFormat.Lz4],
                EncryptionKeystream.None);

            Games = new Dictionary<GameTypeEnum, GameInformation>
            {
                { GameTypeEnum.Rome2, rome2 },
                { GameTypeEnum.Attila, attila },
                { GameTypeEnum.Warhammer, warhammer },
                { GameTypeEnum.Warhammer2, warhammer2 },
                { GameTypeEnum.ThreeKingdoms, threeKingdoms },
                { GameTypeEnum.Troy, troy },
                { GameTypeEnum.Warhammer3, warhammer3 },
                { GameTypeEnum.Pharaoh, pharaoh },
                { GameTypeEnum.Dragonfruit, dragonfruit }
            };
        }

        public static GameInformation GetGameById(GameTypeEnum type)
        {
            return Games[type];
        }


        public static string GetEnumAsString(GameTypeEnum game)
        {
            return game switch
            {
                GameTypeEnum.Unknown => "Unknown",
                GameTypeEnum.Arena => "Arena",
                GameTypeEnum.Attila => "Attila",
                GameTypeEnum.Empire => "Empire",
                GameTypeEnum.Napoleon => "Napoleon",
                GameTypeEnum.RomeRemastered => "Rome Remastered",
                GameTypeEnum.Rome2 => "Rome II",
                GameTypeEnum.Shogun2 => "Shogun 2",
                GameTypeEnum.ThreeKingdoms => "Three Kingdoms",
                GameTypeEnum.ThronesOfBritannia => "Thrones of Britannia",
                GameTypeEnum.Warhammer => "Warhammer",
                GameTypeEnum.Warhammer2 => "Warhammer II",
                GameTypeEnum.Warhammer3 => "Warhammer III",
                GameTypeEnum.Troy => "Troy",
                GameTypeEnum.Pharaoh => "Pharaoh",
                GameTypeEnum.Dragonfruit => "Dragonfruit",
                _ => throw new Exception($"Unknown game - {game}"),
            };
        }
    }
}
