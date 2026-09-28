using System.Xml;

namespace Ytec.WindowsBackup.Core.Services;

public static class WifiProfileXmlValidator
{
    public const int MaximumProfileBytes = 1024 * 1024;
    private const string WlanProfileNamespace =
        "http://www.microsoft.com/networking/WLAN/profile/v1";

    public static void Validate(byte[] xml)
    {
        if (xml is null)
        {
            throw new ArgumentNullException(nameof(xml));
        }

        if (xml.Length == 0 || xml.Length > MaximumProfileBytes)
        {
            throw new InvalidDataException("Wi-FiプロファイルXMLのサイズが不正です。");
        }

        try
        {
            using var stream = new MemoryStream(xml, writable: false);
            using var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    MaxCharactersInDocument = MaximumProfileBytes,
                });
            reader.MoveToContent();
            if (!reader.LocalName.Equals("WLANProfile", StringComparison.Ordinal) ||
                !reader.NamespaceURI.Equals(WlanProfileNamespace, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Windows Wi-FiプロファイルXMLではありません。");
            }

            var hasProfileName = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    reader.LocalName.Equals("name", StringComparison.Ordinal) &&
                    reader.NamespaceURI.Equals(WlanProfileNamespace, StringComparison.Ordinal))
                {
                    hasProfileName =
                        !string.IsNullOrWhiteSpace(reader.ReadElementContentAsString());
                    if (hasProfileName)
                    {
                        break;
                    }
                }
            }

            if (!hasProfileName)
            {
                throw new InvalidDataException("Wi-Fiプロファイル名がありません。");
            }
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("Wi-FiプロファイルXMLを解析できません。", exception);
        }
    }
}
