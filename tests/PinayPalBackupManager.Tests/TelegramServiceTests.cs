using System;
using System.Text.Json;
using PinayPalBackupManager.Services;
using Xunit;

namespace PinayPalBackupManager.Tests;

public class TelegramServiceTests
{
    [Fact]
    public void EscapeHtml_ReplacesSpecialCharacters()
    {
        string input = "<b>Test & 'Check' <123> & \"Alert\"</b>";
        string escaped = TelegramService.EscapeHtml(input);

        Assert.DoesNotContain("<123>", escaped);
        Assert.Contains("&lt;123&gt;", escaped);
        Assert.Contains("&amp;", escaped);
    }

    [Fact]
    public void EscapeHtml_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, TelegramService.EscapeHtml(null));
        Assert.Equal(string.Empty, TelegramService.EscapeHtml(""));
    }

    [Fact]
    public void GenerateConnectionQrBytes_ProducesValidPngWithPayload()
    {
        byte[] qrBytes = TelegramService.GenerateConnectionQrBytes(
            out string payloadJson,
            out string localIp,
            out int port,
            out string pin,
            out string cloudflare);

        Assert.NotNull(qrBytes);
        Assert.True(qrBytes.Length > 100, "QR code PNG byte array should not be empty.");

        // Verify PNG magic header bytes: 0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A
        Assert.Equal(0x89, qrBytes[0]);
        Assert.Equal((byte)'P', qrBytes[1]);
        Assert.Equal((byte)'N', qrBytes[2]);
        Assert.Equal((byte)'G', qrBytes[3]);

        // Verify payload JSON structure
        Assert.False(string.IsNullOrWhiteSpace(payloadJson));
        using var doc = JsonDocument.Parse(payloadJson);
        Assert.True(doc.RootElement.TryGetProperty("localUrl", out var localUrlElem));
        Assert.Contains(port.ToString(), localUrlElem.GetString());
        Assert.True(doc.RootElement.TryGetProperty("hostname", out _));
        Assert.True(doc.RootElement.TryGetProperty("pin", out _));
    }

    [Fact]
    public void NotificationSettings_HasTelegramConfigurationDefaults()
    {
        var settings = new NotificationSettings();

        Assert.False(settings.TelegramEnabled);
        Assert.Equal(string.Empty, settings.TelegramBotToken);
        Assert.Equal(string.Empty, settings.TelegramChatId);
        Assert.True(settings.NotifyOnBackupStart);
        Assert.True(settings.NotifyOnBackupSuccess);
        Assert.True(settings.NotifyOnBackupFailure);
        Assert.True(settings.NotifyOnDisconnect);
        Assert.True(settings.NotifyOnOutdated);
    }

    [Fact]
    public async Task SendMessageAsync_ReturnsError_WhenTokenOrChatIdMissing()
    {
        var (success, message) = await TelegramService.SendMessageAsync("Hello", "");
        Assert.False(success);
        Assert.Contains("not configured", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectChatIdAsync_ReturnsError_WhenTokenMissing()
    {
        var (success, chatId, message) = await TelegramService.DetectChatIdAsync("");
        Assert.False(success);
        Assert.Equal(string.Empty, chatId);
        Assert.Contains("token is required", message, StringComparison.OrdinalIgnoreCase);
    }
}
