using TelaRec.Core.Input;

namespace TelaRec.Tests;

public sealed class HotkeyGestureTests
{
    [Fact]
    public void Parses_default_recording_hotkey()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+R", out var gesture));

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, gesture.Modifiers);
        Assert.Equal(0x52, gesture.VirtualKey);
        Assert.Equal("Ctrl+Alt+R", gesture.ToString());
    }

    [Theory]
    [InlineData("control+alt+shift+f5", "Ctrl+Alt+Shift+F5")]
    [InlineData("WIN+ALT+PRINTSCREEN", "Alt+Win+PrintScreen")]
    [InlineData("Alt+Ctrl+9", "Ctrl+Alt+9")]
    public void Parsing_is_case_insensitive_and_formatting_is_canonical(string text, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(expected, gesture.ToString());
    }

    [Fact]
    public void Parsed_text_round_trips()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+P", out var gesture));
        Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var reparsed));
        Assert.Equal(gesture, reparsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("R")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+A+B")]
    [InlineData("Ctrl+Alt+?")]
    [InlineData("Ctrl+Alt+F25")]
    public void Invalid_gestures_are_rejected(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void Gesture_requires_modifier_and_key()
    {
        Assert.False(new HotkeyGesture(HotkeyModifiers.None, 0x52).IsValid);
        Assert.False(new HotkeyGesture(HotkeyModifiers.Control, 0).IsValid);
        Assert.True(new HotkeyGesture(HotkeyModifiers.Control, 0x53).IsValid);
    }
}
