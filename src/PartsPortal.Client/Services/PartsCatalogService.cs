using PartsPortal.Models;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PartsPortal.Services
{
    public class PartsCatalogService
    {
        public List<Part> LoadFromCsv(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("CSV path is empty.");

            if (!File.Exists(path))
                throw new FileNotFoundException("CSV/TSV file not found.", path);

            // Detect delimiter from first non-empty line
            var delimiter = DetectDelimiter(path);

            var parts = new List<Part>();

            using var parser = new TextFieldParser(path);
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(delimiter);
            parser.HasFieldsEnclosedInQuotes = true;
            parser.TrimWhiteSpace = false; // we'll Trim() ourselves consistently

            // Read header
            string[]? header = ReadNonEmptyFields(parser);
            if (header == null)
                return parts;

            var headerCols = header.Select(h => (h ?? "").Trim()).ToArray();

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headerCols.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(headerCols[i]) && !map.ContainsKey(headerCols[i]))
                    map[headerCols[i]] = i;
            }

            // Require these headers
            if (!map.ContainsKey("Description") || !map.ContainsKey("Material") || !map.ContainsKey("Warehouse"))
            {
                var found = string.Join(", ", headerCols.Where(h => !string.IsNullOrWhiteSpace(h)));
                throw new Exception(
                    "CSV headers must include: Description, Material, Warehouse.\n" +
                    $"Found: {found}");
            }

            int descIdx = map["Description"];
            int matIdx = map["Material"];
            int whIdx = map["Warehouse"];

            // Read rows
            while (!parser.EndOfData)
            {
                string[]? fields;
                try
                {
                    fields = parser.ReadFields();
                }
                catch (MalformedLineException)
                {
                    // Skip malformed row rather than crashing entire load
                    continue;
                }

                if (fields == null) continue;
                if (fields.All(string.IsNullOrWhiteSpace)) continue;

                string desc = Get(fields, descIdx);
                string mat = Get(fields, matIdx);
                string wh = Get(fields, whIdx);

                if (string.IsNullOrWhiteSpace(mat) && string.IsNullOrWhiteSpace(desc))
                    continue;

                parts.Add(new Part
                {
                    Description = desc,
                    Material = mat,
                    Warehouse = wh
                });
            }

            return parts;
        }

        private static string Get(string[] cols, int idx)
            => (idx >= 0 && idx < cols.Length) ? (cols[idx] ?? "").Trim() : "";

        private static string DetectDelimiter(string path)
        {
            using var sr = new StreamReader(path);

            while (!sr.EndOfStream)
            {
                var line = sr.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // TSV is common for your parts file
                if (line.Contains('\t')) return "\t";

                return ","; // default CSV
            }

            return ","; // fallback
        }

        private static string[]? ReadNonEmptyFields(TextFieldParser parser)
        {
            while (!parser.EndOfData)
            {
                string[]? fields;
                try
                {
                    fields = parser.ReadFields();
                }
                catch (MalformedLineException)
                {
                    continue;
                }

                if (fields == null) continue;
                if (fields.All(string.IsNullOrWhiteSpace)) continue;

                return fields;
            }
            return null;
        }
    }
}
