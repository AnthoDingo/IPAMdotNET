using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Classeur Excel (.xlsx) minimal, sans dépendance : une feuille par tableau, première ligne en gras et figée.
/// Textes en chaînes en ligne ; nombres simples (sans zéro initial, 15 chiffres au plus) en cellules numériques.
/// </summary>
public static partial class Xlsx
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Write(IReadOnlyList<(string Name, List<string?[]> Rows)> sheets)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            StringBuilder types = new();
            StringBuilder book = new();
            StringBuilder rels = new();
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= sheets.Count; i++)
            {
                string name = SheetName(sheets[i - 1].Name, names);
                types.Append(CultureInfo.InvariantCulture, $"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                book.Append(CultureInfo.InvariantCulture, $"<sheet name=\"{SecurityElement.Escape(name)}\" sheetId=\"{i}\" r:id=\"rId{i}\"/>");
                rels.Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
                Entry(zip, $"xl/worksheets/sheet{i}.xml", Sheet(sheets[i - 1].Rows));
            }
            Entry(zip, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + types + "</Types>");
            Entry(zip, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Entry(zip, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                + "<sheets>" + book + "</sheets></workbook>");
            Entry(zip, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + rels
                + $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            // Style 1 : gras (en-têtes).
            Entry(zip, "xl/styles.xml", "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
                + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
                + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>"
                + "</styleSheet>");
        }
        return stream.ToArray();
    }

    private static string Sheet(List<string?[]> rows)
    {
        StringBuilder xml = new("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetData>");
        for (int r = 0; r < rows.Count; r++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
            for (int c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c] is not { Length: > 0 } value)
                {
                    continue;
                }
                string reference = Column(c) + (r + 1).ToString(CultureInfo.InvariantCulture);
                string style = r == 0 ? " s=\"1\"" : "";
                xml.Append(r > 0 && NumberPattern().IsMatch(value)
                    ? $"<c r=\"{reference}\"><v>{value}</v></c>"
                    : $"<c r=\"{reference}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{SecurityElement.Escape(XmlSafe(value))}</t></is></c>");
            }
            xml.Append("</row>");
        }
        return xml.Append("</sheetData></worksheet>").ToString();
    }

    /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
    private static string Column(int index)
    {
        string name = "";
        for (index++; index > 0; index = (index - 1) / 26)
        {
            name = (char)('A' + (index - 1) % 26) + name;
        }
        return name;
    }

    /// <summary>Nom de feuille Excel : 31 caractères, sans []:*?/\, unique.</summary>
    private static string SheetName(string name, HashSet<string> used)
    {
        string clean = new([.. name.Where(c => !"[]:*?/\\".Contains(c))]);
        clean = clean.Length == 0 ? "Feuille" : clean[..Math.Min(clean.Length, 31)];
        string candidate = clean;
        for (int n = 2; !used.Add(candidate); n++)
        {
            candidate = $"{clean[..Math.Min(clean.Length, 27)]} ({n})";
        }
        return candidate;
    }

    /// <summary>Retire les caractères interdits en XML 1.0 (contrôles saisis par erreur).</summary>
    private static string XmlSafe(string value) =>
        value.All(Valid) ? value : new string([.. value.Where(Valid)]);

    private static bool Valid(char c) => XmlConvert.IsXmlChar(c) || char.IsSurrogate(c);

    private static void Entry(ZipArchive zip, string path, string content)
    {
        using StreamWriter writer = new(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        writer.Write(content);
    }

    [GeneratedRegex(@"^-?(0|[1-9][0-9]{0,14})(\.[0-9]{1,10})?$")]
    private static partial Regex NumberPattern();
}
