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
        Pharaoh
    }

    public enum GameBnkVersion : uint
    {
        Unsupported = 0,
        Warhammer3 = 2147483784,
        Attila = 112
    }

    public enum WwiseProjectId : uint
    {
        Unsupported = 0,
        Warhammer3 = 2361
    }

    public enum PackFileVersion
    {
        PFH0,
        PFH2,
        PFH3,
        PFH4,
        PFH5
    }

    public enum WsModelVersion
    {
        Unknown = 0,
        Version1,
        Version2,
        Version3
    }

    public enum EncryptionKeystream
    {
        Unsupported = 0,
        None,
        ThirtyTwoBitComplement,
        SixtyFourBitComplement
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
            var warhammer = new GameInformation(
                GameTypeEnum.Warhammer,
                "Total War: WARHAMMER",
                PackFileVersion.PFH4,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.None],
                EncryptionKeystream.SixtyFourBitComplement);

            var warhammer2 = new GameInformation(
                GameTypeEnum.Warhammer2,
                "Total War: WARHAMMER II",
                PackFileVersion.PFH5,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Version1,
                [CompressionFormat.Lzma1],
                EncryptionKeystream.SixtyFourBitComplement);

            var warhammer3 = new GameInformation(
                GameTypeEnum.Warhammer3,
                "Total War: WARHAMMER III",
                PackFileVersion.PFH5,
                GameBnkVersion.Warhammer3,
                WwiseProjectId.Warhammer3,
                WsModelVersion.Version3,
                [CompressionFormat.Lzma1, CompressionFormat.Lz4, CompressionFormat.Zstd],
                EncryptionKeystream.SixtyFourBitComplement);

            var troy = new GameInformation(
                GameTypeEnum.Troy,
                "A Total War Saga: TROY",
                PackFileVersion.PFH5,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.Lzma1],
                EncryptionKeystream.SixtyFourBitComplement);

            var threeKingdoms = new GameInformation(
                GameTypeEnum.ThreeKingdoms,
                "Total War: THREE KINGDOMS",
                PackFileVersion.PFH5,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Version1,
                [CompressionFormat.Lzma1],
                EncryptionKeystream.None);

            var rome2 = new GameInformation(
                GameTypeEnum.Rome2,
                "Total War: ROME II",
                PackFileVersion.PFH4,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.None],
                EncryptionKeystream.ThirtyTwoBitComplement);

            var attila = new GameInformation(
                GameTypeEnum.Attila,
                "Total War: ATTILA",
                PackFileVersion.PFH4,
                GameBnkVersion.Attila,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.None],
                EncryptionKeystream.ThirtyTwoBitComplement);

            var pharaoh = new GameInformation(
                GameTypeEnum.Pharaoh,
                "Total War: PHARAOH",
                PackFileVersion.PFH5,
                GameBnkVersion.Unsupported,
                WwiseProjectId.Unsupported,
                WsModelVersion.Unknown,
                [CompressionFormat.Lzma1],
                EncryptionKeystream.SixtyFourBitComplement);

            Games = new Dictionary<GameTypeEnum, GameInformation>
            {
                { GameTypeEnum.Warhammer, warhammer },
                { GameTypeEnum.Warhammer2, warhammer2 },
                { GameTypeEnum.Warhammer3, warhammer3 },
                { GameTypeEnum.Troy, troy },
                { GameTypeEnum.ThreeKingdoms, threeKingdoms },
                { GameTypeEnum.Rome2, rome2 },
                { GameTypeEnum.Attila, attila },
                { GameTypeEnum.Pharaoh, pharaoh }
            };
        }

        public static GameInformation GetGameById(GameTypeEnum type)
        {
            return Games[type];
        }

        public static IEnumerable<GameTypeEnum> GetSupportedGames()
        {
            return Games.Values
                .Where(game => game.EncryptionKeystream != EncryptionKeystream.Unsupported)
                .Select(game => game.Type);
        }

        public static string GetEnumAsString(GameTypeEnum game)
        {
            if (Games.TryGetValue(game, out var gameInformation))
                return gameInformation.DisplayName;

            throw new Exception($"Unknown or unsupported game {game}");
        }
    }
}
