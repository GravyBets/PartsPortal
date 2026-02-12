using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PartsPortal
{
    public class PartsCatalogService
    {
        public List<Part> LoadFromCsv(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("CSV path is empty.");

            if (!File.Exists(path))
                throw new FileNotFoundException("CSV/TSV file not found.", path);

            var lines = File.ReadAllLines(path);
            var parts = new List<Part>();
            if (lines.Length == 0) return parts;

            // Detect delimiter from header line
            char delimiter = DetectDelimiter(lines[0]);

            // Read header -> index map (case-insensitive)
            var headerCols = SplitLine(lines[0], delimiter)
                .Select(h => (h ?? "").Trim())
                .ToArray();

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headerCols.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(headerCols[i]) && !map.ContainsKey(headerCols[i]))
                    map[headerCols[i]] = i;
            }

            // Require at least these headers (exact names from your file)
            if (!map.ContainsKey("Description") || !map.ContainsKey("Material") || !map.ContainsKey("Warehouse"))
                throw new Exception("CSV headers must include: Description, Material, Warehouse");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = SplitLine(line, delimiter);
                if (cols.Length < headerCols.Length) continue;

                string desc = Get(cols, map["Description"]);
                string mat = Get(cols, map["Material"]);
                string wh = Get(cols, map["Warehouse"]);

                if (string.IsNullOrWhiteSpace(mat) && string.IsNullOrWhiteSpace(desc)) continue;

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

        private static char DetectDelimiter(string headerLine)
        {
            // Your sample looks TSV (tabs). We'll support both.
            if (headerLine.Contains('\t')) return '\t';
            return ','; // default CSV
        }

        // Works for CSV (quoted commas) and TSV (no quoting needed usually)
        private static string[] SplitLine(string line, char delimiter)
        {
            // If TSV, quick split
            if (delimiter == '\t')
                return line.Split('\t');

            // CSV split with quote handling
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    // handle escaped quotes ""
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (c == delimiter && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
