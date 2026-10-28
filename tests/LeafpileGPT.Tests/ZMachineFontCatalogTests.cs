namespace LeafpileGPT.Tests;

using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineFontCatalogTests
{
    [Fact]
    public void ProvidesNormalCharacterGraphicsAndCourierFontsButNoPictureFont()
    {
        Assert.True(ZMachineFontCatalog.IsAvailable(ZMachineFontCatalog.NormalFont));
        Assert.False(ZMachineFontCatalog.IsAvailable(ZMachineFontCatalog.PictureFont));
        Assert.True(ZMachineFontCatalog.IsAvailable(ZMachineFontCatalog.CharacterGraphicsFont));
        Assert.True(ZMachineFontCatalog.IsAvailable(ZMachineFontCatalog.CourierFont));
        Assert.Equal("Courier New", ZMachineFontCatalog.GetPreferredTypefaceName(ZMachineFontCatalog.CourierFont));
        Assert.Null(ZMachineFontCatalog.GetPreferredTypefaceName(ZMachineFontCatalog.CharacterGraphicsFont));
    }

    [Fact]
    public void FontThreeDefinesAllPrintableAsciiGlyphsAsEightByEightRows()
    {
        for (var character = ZMachineFontCatalog.FirstCharacterGraphicsCode;
             character <= ZMachineFontCatalog.LastCharacterGraphicsCode;
             character++)
        {
            var rows = ZMachineFontCatalog.GetCharacterGraphicsGlyph(character);
            Assert.Equal(8, rows.Length);
        }

        Assert.Equal(new byte[] { 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80 },
            ZMachineFontCatalog.GetCharacterGraphicsGlyph((byte)'#'));
        Assert.True(ZMachineFontCatalog.IsCharacterGraphicsPixelSet((byte)'#', 0, 7));
        Assert.False(ZMachineFontCatalog.IsCharacterGraphicsPixelSet((byte)'#', 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZMachineFontCatalog.GetCharacterGraphicsGlyph(31));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZMachineFontCatalog.GetCharacterGraphicsGlyph(127));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZMachineFontCatalog.IsCharacterGraphicsPixelSet((byte)'#', 8, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZMachineFontCatalog.IsCharacterGraphicsPixelSet((byte)'#', 0, 8));
    }
}
