namespace LeafpileGPT.Core;

using System.Collections.Immutable;

/// <summary>Describes the fonts provided by the interpreter and the Z-machine character graphics font.</summary>
public static class ZMachineFontCatalog
{
    public const ushort NormalFont = 1;
    public const ushort PictureFont = 2;
    public const ushort CharacterGraphicsFont = 3;
    public const ushort CourierFont = 4;
    public const int CharacterGraphicsWidth = 8;
    public const int CharacterGraphicsHeight = 8;
    public const byte FirstCharacterGraphicsCode = 32;
    public const byte LastCharacterGraphicsCode = 126;

    // Eight row bytes per glyph, taken from the Standard 1.1 section 16.1 bitmap table.
    private static readonly string[] GlyphHexRows =
    [
        "0000000000000000", // 32
        "00002060FE602000", // 33
        "0000080CFE0C0800", // 34
        "0102040810204080", // 35
        "8040201008040201", // 36
        "0000000000000000", // 37
        "00000000FF000000", // 38
        "000000FF00000000", // 39
        "0808080808080808", // 40
        "1010101010101010", // 41
        "080808FF00000000", // 42
        "00000000FF080808", // 43
        "080808080F080808", // 44
        "10101010F0101010", // 45
        "101010101F000000", // 46
        "0000001F10101010", // 47
        "000000F808080808", // 48
        "08080808F8000000", // 49
        "101010101F204080", // 50
        "8040201F10101010", // 51
        "010204F808080808", // 52
        "08080808F8040201", // 53
        "FFFFFFFFFFFFFFFF", // 54
        "FFFFFFFFFF000000", // 55
        "000000FFFFFFFFFF", // 56
        "F8F8F8F8F8F8F8F8", // 57
        "1F1F1F1F1F1F1F1F", // 58
        "080808FFFFFFFFFF", // 59
        "FFFFFFFFFF080808", // 60
        "F8F8F8F8FFF8F8F8", // 61
        "1F1F1F1FFF1F1F1F", // 62
        "1F1F1F1F1F000000", // 63
        "0000001F1F1F1F1F", // 64
        "000000F8F8F8F8F8", // 65
        "F8F8F8F8F8000000", // 66
        "1F1F1F1F1F204080", // 67
        "8040201F1F1F1F1F", // 68
        "010204F8F8F8F8F8", // 69
        "F8F8F8F8F8040201", // 70
        "0100000000000000", // 71
        "0000000000000001", // 72
        "0000000000000080", // 73
        "8000000000000000", // 74
        "FF00000000000000", // 75
        "00000000000000FF", // 76
        "8080808080808080", // 77
        "0101010101010101", // 78
        "00FF00000000FF00", // 79
        "00FF80808080FF00", // 80
        "00FFC0C0C0C0FF00", // 81
        "00FFE0E0E0E0FF00", // 82
        "00FFF0F0F0F0FF00", // 83
        "00FFF8F8F8F8FF00", // 84
        "00FFFCFCFCFCFF00", // 85
        "00FFFEFEFEFEFF00", // 86
        "00FFFFFFFFFFFF00", // 87
        "0001010101010100", // 88
        "0080808080808000", // 89
        "8142241818244281", // 90
        "08080808FF080808", // 91
        "183CDB1818181800", // 92
        "18181818DB3C1800", // 93
        "183CDB18DB3C1800", // 94
        "FF818181818181FF", // 95
        "3C66060C18001800", // 96
        "C4A890C0A0908000", // 97
        "6050487048506000", // 98
        "1018149250301000", // 99
        "82C6AA92AAC68200", // 100
        "82C6AA9282828200", // 101
        "94A8D0A0C0808000", // 102
        "8244281028448200", // 103
        "C2A2D2AA968A8600", // 104
        "1010101010101000", // 105
        "1038549254381000", // 106
        "1010103854929200", // 107
        "1018141210101000", // 108
        "C6AA92AAC6828200", // 109
        "9050381412101000", // 110
        "C4ACD4A890808000", // 111
        "80808090A8C48200", // 112
        "4040407844444400", // 113
        "6050485060504800", // 114
        "40444C5464440400", // 115
        "1038549210101000", // 116
        "6050484444444400", // 117
        "10BA541010101000", // 118
        "6050485060404000", // 119
        "9254381010101000", // 120
        "E0D0A8949A969200", // 121
        "1028442810284400", // 122
        "E7C324E7E7E7E7FF", // 123
        "E7E7E7E724C3E7FF", // 124
        "E7C324E724C3E7FF", // 125
        "C399F9F3E7FFE7FF", // 126
    ];

    private static readonly ImmutableArray<ImmutableArray<byte>> CharacterGraphicsGlyphRows =
        ImmutableArray.CreateRange(GlyphHexRows.Select(row => ImmutableArray.CreateRange(Convert.FromHexString(row))));

    /// <summary>Returns whether the interpreter provides this requested font.</summary>
    public static bool IsAvailable(ushort font) => font is NormalFont or CharacterGraphicsFont or CourierFont;

    /// <summary>Returns the preferred host typeface for a font, if it is an ordinary text font.</summary>
    public static string? GetPreferredTypefaceName(ushort font) => font == CourierFont ? "Courier New" : null;

    /// <summary>Returns eight 8-bit rows for one printable ASCII glyph; bit 7 is the leftmost pixel.</summary>
    public static ImmutableArray<byte> GetCharacterGraphicsGlyph(byte character)
    {
        if (character is < FirstCharacterGraphicsCode or > LastCharacterGraphicsCode)
        {
            throw new ArgumentOutOfRangeException(nameof(character), character,
                $"Font 3 defines character codes {FirstCharacterGraphicsCode} through {LastCharacterGraphicsCode}.");
        }

        return CharacterGraphicsGlyphRows[character - FirstCharacterGraphicsCode];
    }

    /// <summary>Tests one pixel in a font 3 glyph.</summary>
    public static bool IsCharacterGraphicsPixelSet(byte character, int row, int column)
    {
        if (row is < 0 or >= CharacterGraphicsHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        if (column is < 0 or >= CharacterGraphicsWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        return (GetCharacterGraphicsGlyph(character)[row] & (1 << (7 - column))) != 0;
    }
}
