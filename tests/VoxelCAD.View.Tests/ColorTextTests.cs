using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.View;
using Xunit;

namespace VoxelCAD.View.Tests;

/// <summary>
/// What a colour looks like written down, and what typing one back means. Pure,
/// which is the whole reason it is a class of its own: the row that shows it is a
/// Godot control and cannot be built without an engine.
/// </summary>
public class ColorTextTests
{
    [Fact]
    public void AnOpaqueColourIsWrittenWithoutItsAlpha()
    {
        var color = new Color(0.5f, 0.25f, 1f);

        Assert.Equal("#8040FF", ColorText.Write(color, ColorTextFormat.Hex));
        Assert.Equal("128, 64, 255", ColorText.Write(color, ColorTextFormat.Rgb));
    }

    [Fact]
    public void ATranslucentColourCarriesItsAlpha()
    {
        var color = new Color(1f, 1f, 1f, 0.5f);

        Assert.Equal("#FFFFFF80", ColorText.Write(color, ColorTextFormat.Hex));
        Assert.Equal("255, 255, 255, 128", ColorText.Write(color, ColorTextFormat.Rgb));
    }

    /// <summary>
    /// Six digits say nothing about opacity, so they must not quietly grant it:
    /// typing a shade into a pane of glass should recolour the glass, not solidify
    /// it.
    /// </summary>
    [Fact]
    public void SixHexDigitsLeaveTheAlphaAlone()
    {
        var current = new Color(0f, 0f, 0f, 0.4f);

        Assert.True(ColorText.TryRead("#FF8000", ColorTextFormat.Hex, current, out Color parsed));
        Assert.Equal(0.4f, parsed.A, 3);
        Assert.Equal(1f, parsed.R, 3);
    }

    [Fact]
    public void EightHexDigitsSetIt()
    {
        Assert.True(ColorText.TryRead("#FF800080", ColorTextFormat.Hex, Colors.White, out Color parsed));
        Assert.Equal(0.502f, parsed.A, 2);
    }

    /// <summary>
    /// What somebody pastes has its own punctuation, and refusing it teaches
    /// nothing. Commas, spaces and the units are all optional.
    /// </summary>
    [Theory]
    [InlineData("128, 64, 255")]
    [InlineData("128 64 255")]
    [InlineData("  128,64 , 255  ")]
    public void RgbIsForgivingAboutPunctuation(string text)
    {
        Assert.True(ColorText.TryRead(text, ColorTextFormat.Rgb, Colors.White, out Color parsed));
        Assert.Equal(0.502f, parsed.R, 2);
        Assert.Equal(0.251f, parsed.G, 2);
        Assert.Equal(1f, parsed.B, 2);
    }

    [Fact]
    public void HsvRoundTrips()
    {
        var color = Color.FromHsv(0.25f, 0.8f, 0.6f);
        var text = ColorText.Write(color, ColorTextFormat.Hsv);

        Assert.True(ColorText.TryRead(text, ColorTextFormat.Hsv, Colors.White, out Color parsed));

        Assert.Equal(color.R, parsed.R, 2);
        Assert.Equal(color.G, parsed.G, 2);
        Assert.Equal(color.B, parsed.B, 2);
    }

    /// <summary>A hue is an angle, so it wraps rather than clamping: 370 is 10.</summary>
    [Fact]
    public void AHueWrapsRatherThanClamping()
    {
        Assert.True(ColorText.TryRead("370, 100, 100", ColorTextFormat.Hsv, Colors.White, out Color wrapped));
        Assert.True(ColorText.TryRead("10, 100, 100", ColorTextFormat.Hsv, Colors.White, out Color plain));

        Assert.Equal(plain.G, wrapped.G, 3);
        Assert.Equal(plain.B, wrapped.B, 3);
    }

    [Theory]
    [InlineData("", ColorTextFormat.Hex)]
    [InlineData("#FFF", ColorTextFormat.Hex)]
    [InlineData("#GGGGGG", ColorTextFormat.Hex)]
    [InlineData("128, 64", ColorTextFormat.Rgb)]
    [InlineData("red", ColorTextFormat.Rgb)]
    [InlineData("nonsense", ColorTextFormat.Hsv)]
    public void NonsenseIsRefusedRatherThanGuessedAt(string text, ColorTextFormat format)
    {
        Assert.False(ColorText.TryRead(text, format, Colors.White, out _));
    }

    /// <summary>
    /// Out-of-range numbers are clamped, not refused. Somebody who types 300 meant
    /// the top of the range, and a field that erases the attempt teaches nothing.
    /// </summary>
    [Fact]
    public void AnOutOfRangeChannelIsClamped()
    {
        Assert.True(ColorText.TryRead("300, -20, 128", ColorTextFormat.Rgb, Colors.White, out Color parsed));

        Assert.Equal(1f, parsed.R, 3);
        Assert.Equal(0f, parsed.G, 3);
    }
}
