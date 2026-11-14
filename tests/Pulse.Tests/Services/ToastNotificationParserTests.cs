using System.Text;
using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class ToastNotificationParserTests
{
    [Fact]
    public void ParsesStandardMessagingToastWithFriendlyAppName()
    {
        const string xml = """
            <toast>
              <visual>
                <binding template="ToastGeneric">
                  <text>Ahmet Yılmaz</text>
                  <text>Akşam oyuna giriyor muyuz?</text>
                </binding>
              </visual>
            </toast>
            """;

        var parsed = ToastNotificationParser.TryParse("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", xml);

        Assert.NotNull(parsed);
        Assert.Equal("WhatsApp", parsed.AppName);
        Assert.Contains("Ahmet Yılmaz", parsed.Title);
        Assert.Equal("Akşam oyuna giriyor muyuz?", parsed.Subtitle);
        Assert.False(string.IsNullOrEmpty(parsed.IconGlyph));
    }

    [Fact]
    public void SkipsSuppressPopupAndOwnPulseToasts()
    {
        const string silentXml = """
            <toast suppressPopup="true">
              <visual><binding template="ToastGeneric"><text>Background sync</text></binding></visual>
            </toast>
            """;

        const string normalXml = """
            <toast>
              <visual><binding template="ToastGeneric"><text>Pulse</text><text>Self alert</text></binding></visual>
            </toast>
            """;

        Assert.Null(ToastNotificationParser.TryParse("com.discord.Discord", silentXml));
        Assert.Null(ToastNotificationParser.TryParse("Pulse.DynamicIsland", normalXml));
    }

    [Fact]
    public void HandlesUtf8BomAndMalformedXmlGracefully()
    {
        var xmlWithNull = "\uFEFF<toast><visual><binding template=\"ToastGeneric\"><text>Sistem</text><text>Güncelleme hazır</text></binding></visual></toast>\0\0";
        var bytes = Encoding.UTF8.GetBytes(xmlWithNull);

        var parsed = ToastNotificationParser.TryParse("Windows.SystemToast.WindowsUpdate.MoNotification", bytes);
        Assert.NotNull(parsed);
        Assert.Contains("Sistem", parsed.Title);
        Assert.Equal("Güncelleme hazır", parsed.Subtitle);

        var malformed = ToastNotificationParser.TryParse("Discord", "<toast><broken");
        Assert.Null(malformed);
    }

    [Fact]
    public void ProhibitsDtdAndExternalEntities()
    {
        const string xxeXml = """
            <?xml version="1.0"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///c:/windows/win.ini"> ]>
            <toast><visual><binding template="ToastGeneric"><text>&xxe;</text></binding></visual></toast>
            """;

        Assert.Null(ToastNotificationParser.TryParse("SomeApp", xxeXml));
    }

    [Fact]
    public void ParsesUtf16LeBomUrgentScenarioAndAttributionFallback()
    {
        const string xml = """
            <toast scenario="incomingCall">
              <visual>
                <binding template="ToastGeneric">
                  <text>Mesaj</text>
                  <text placement="attribution">Özelİstemci</text>
                </binding>
              </visual>
            </toast>
            """;

        var preamble = Encoding.Unicode.GetPreamble();
        var body = Encoding.Unicode.GetBytes(xml);
        var combined = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, combined, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, combined, preamble.Length, body.Length);

        var parsed = ToastNotificationParser.TryParse(string.Empty, combined);

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsHighPriority);
        Assert.Equal("Özelİstemci", parsed.AppName);
        Assert.Equal("Mesaj", parsed.Subtitle);
    }
}

