using System.Text;

namespace IPAMdotNet.Maintenance;

/// <summary>CSV RFC 4180 minimal : guillemets, guillemets doublés, retours à la ligne dans un champ.</summary>
public static class Csv
{
    /// <summary>Séparateur « ; » : c'est celui qu'Excel attend en français.</summary>
    public static string Write(IEnumerable<IReadOnlyList<string?>> rows, char separator = ';')
    {
        StringBuilder builder = new();
        foreach (IReadOnlyList<string?> row in rows)
        {
            builder.AppendJoin(separator, row.Select(value => Escape(value ?? "", separator)));
            builder.Append("\r\n");
        }
        return builder.ToString();
    }

    private static string Escape(string value, char separator) =>
        value.IndexOfAny([separator, '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    /// <summary>Lit un texte CSV ; le séparateur (« ; » ou « , ») est déduit de la première ligne. Les lignes vides sont ignorées.</summary>
    public static List<string[]> Parse(string text)
    {
        text = text.TrimStart('﻿');
        string firstLine = text.Split('\n', 2)[0];
        char separator = firstLine.Count(c => c == ';') >= firstLine.Count(c => c == ',') ? ';' : ',';

        List<string[]> rows = [];
        List<string> fields = [];
        StringBuilder field = new();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                EndRow();
            }
            else if (c != '\r')
            {
                field.Append(c);
            }
        }
        EndRow();
        return rows;

        void EndRow()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (fields.Any(f => f.Length > 0))
            {
                rows.Add(fields.ToArray());
            }
            fields.Clear();
        }
    }
}
