using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Pulse.Core.Services;

public sealed record ParsedToastNotification(
    string AppName,
    string Title,
    string Subtitle,
    string IconGlyph,
    bool IsHighPriority);

/// <summary>
/// Safely parses Windows notification toast XML payloads (as stored in wpndatabase.db)
/// with DTD processing disabled and normalizes application names for compact island display.
/// </summary>
public static class ToastNotificationParser
{
    private static readonly XmlReaderSettings SafeXmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true
    };

    public static ParsedToastNotification? TryParse(string? primaryId, byte[]? payloadBytes)
    {
        if (payloadBytes is null || payloadBytes.Length == 0)
        {
            return null;
        }

        string xml;
        if (payloadBytes.Length >= 2 && payloadBytes[0] == 0xFF && payloadBytes[1] == 0xFE)
        {
            xml = Encoding.Unicode.GetString(payloadBytes, 2, payloadBytes.Length - 2);
        }
        else if (payloadBytes.Length >= 3 && payloadBytes[0] == 0xEF && payloadBytes[1] == 0xBB && payloadBytes[2] == 0xBF)
        {
            xml = Encoding.UTF8.GetString(payloadBytes, 3, payloadBytes.Length - 3);
        }
        else
        {
            xml = Encoding.UTF8.GetString(payloadBytes);
        }

        return TryParse(primaryId, xml);
    }

    public static ParsedToastNotification? TryParse(string? primaryId, string? payloadXml)
    {
        if (string.IsNullOrWhiteSpace(payloadXml))
        {
            return null;
        }

        var trimmed = payloadXml.TrimStart('\uFEFF').TrimEnd('\0').Trim();
        if (trimmed.Length == 0 || !trimmed.StartsWith('<'))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(primaryId) &&
            (primaryId.Contains("Pulse.DynamicIsland", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(primaryId.Trim(), "Pulse", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        XDocument document;
        try
        {
            using var stringReader = new StringReader(trimmed);
            using var xmlReader = XmlReader.Create(stringReader, SafeXmlSettings);
            document = XDocument.Load(xmlReader, LoadOptions.None);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            return null;
        }

        var toastElement = document.Root;
        if (toastElement is null || !string.Equals(toastElement.Name.LocalName, "toast", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suppressAttr = toastElement.Attribute("suppressPopup")?.Value;
        if (string.Equals(suppressAttr, "true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var scenario = toastElement.Attribute("scenario")?.Value ?? string.Empty;
        var isHighPriority = scenario is "incomingCall" or "alarm" or "urgent";

        var textLines = new List<string>();
        string? attributionApp = null;

        var binding = toastElement
            .Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "binding", StringComparison.OrdinalIgnoreCase));

        if (binding is not null)
        {
            foreach (var textElement in binding.Descendants()
                         .Where(e => string.Equals(e.Name.LocalName, "text", StringComparison.OrdinalIgnoreCase)))
            {
                var content = CollapseWhitespace(textElement.Value);
                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                var placement = textElement.Attribute("placement")?.Value;
                if (string.Equals(placement, "attribution", StringComparison.OrdinalIgnoreCase))
                {
                    attributionApp ??= content;
                    continue;
                }

                textLines.Add(content);
            }
        }

        if (textLines.Count == 0)
        {
            return null;
        }

        var appName = NormalizeAppName(primaryId);
        if (string.IsNullOrEmpty(appName) && !string.IsNullOrEmpty(attributionApp))
        {
            appName = attributionApp;
        }

        var heading = textLines[0];
        var body = textLines.Count > 1
            ? string.Join(" · ", textLines.Skip(1))
            : string.Empty;

        string title;
        string subtitle;
        if (string.IsNullOrEmpty(appName) || string.Equals(heading, appName, StringComparison.OrdinalIgnoreCase))
        {
            title = heading;
            subtitle = body;
        }
        else if (!string.IsNullOrEmpty(body))
        {
            title = heading.StartsWith(appName, StringComparison.OrdinalIgnoreCase)
                ? heading
                : $"{appName}: {heading}";
            subtitle = body;
        }
        else
        {
            title = appName;
            subtitle = heading;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new ParsedToastNotification(
            AppName: string.IsNullOrEmpty(appName) ? "Windows" : appName,
            Title: title,
            Subtitle: subtitle,
            IconGlyph: GetGlyphForApp(primaryId, isHighPriority),
            IsHighPriority: isHighPriority);
    }

    public static string NormalizeAppName(string? primaryId)
    {
        if (string.IsNullOrWhiteSpace(primaryId))
        {
            return string.Empty;
        }

        var id = primaryId.Trim();
        if (id.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) return "WhatsApp";
        if (id.Contains("Discord", StringComparison.OrdinalIgnoreCase)) return "Discord";
        if (id.Contains("Telegram", StringComparison.OrdinalIgnoreCase)) return "Telegram";
        if (id.Contains("Slack", StringComparison.OrdinalIgnoreCase)) return "Slack";
        if (id.Contains("Teams", StringComparison.OrdinalIgnoreCase)) return "Microsoft Teams";
        if (id.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (id.Contains("Edge", StringComparison.OrdinalIgnoreCase) || id.Contains("MSEdge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (id.Contains("Firefox", StringComparison.OrdinalIgnoreCase) || id.Contains("Mozilla", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (id.Contains("Steam", StringComparison.OrdinalIgnoreCase) || id.Contains("Valve", StringComparison.OrdinalIgnoreCase)) return "Steam";
        if (id.Contains("Outlook", StringComparison.OrdinalIgnoreCase) || id.Contains("Mail", StringComparison.OrdinalIgnoreCase)) return "Posta";
        if (id.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase)) return "Windows Update";
        if (id.Contains("Defender", StringComparison.OrdinalIgnoreCase) || id.Contains("SecHealth", StringComparison.OrdinalIgnoreCase)) return "Windows Güvenliği";
        if (id.Contains("ScreenSketch", StringComparison.OrdinalIgnoreCase)) return "Ekran Alıntısı";
        if (id.Contains("Explorer", StringComparison.OrdinalIgnoreCase)) return "Dosya Gezgini";

        var bangIndex = id.IndexOf('!');
        if (bangIndex > 0)
        {
            id = id[..bangIndex];
        }

        var underscoreIndex = id.IndexOf('_');
        if (underscoreIndex > 0)
        {
            id = id[..underscoreIndex];
        }

        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            id = id[..^4];
        }

        var lastDot = id.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < id.Length - 1)
        {
            id = id[(lastDot + 1)..];
        }

        return id.Replace("App", string.Empty, StringComparison.Ordinal).Trim();
    }

    private static string GetGlyphForApp(string? primaryId, bool isHighPriority)
    {
        if (isHighPriority)
        {
            return "\uE717";
        }

        if (string.IsNullOrWhiteSpace(primaryId))
        {
            return "\uE7E7";
        }

        if (primaryId.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
            primaryId.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
            primaryId.Contains("Telegram", StringComparison.OrdinalIgnoreCase) ||
            primaryId.Contains("Slack", StringComparison.OrdinalIgnoreCase) ||
            primaryId.Contains("Teams", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE8BD";
        }

        if (primaryId.Contains("Mail", StringComparison.OrdinalIgnoreCase) ||
            primaryId.Contains("Outlook", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE715";
        }

        if (primaryId.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase) ||
            primaryId.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
            primaryId.Contains("SecHealth", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE83D";
        }

        return "\uE7E7";
    }

    private static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        var inWhitespace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
            }
            else
            {
                sb.Append(ch);
                inWhitespace = false;
            }
        }

        return sb.ToString();
    }
}

