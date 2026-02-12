using PartsPortal.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PartsPortal.Services
{
    public class TemplatesService
    {
        private const string AppFolderName = "PartsPortal";
        private readonly string _dataDir;

        public TemplatesService()
        {
            var appDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    AppFolderName);

            _dataDir = Path.Combine(appDir, "Data");
            Directory.CreateDirectory(_dataDir);

        }

        public List<PartTemplate> Load(string warehouseKey)
        {
            var path = GetTemplatesPath(warehouseKey);

            // If you already saved templates previously (in the old BaseDirectory\Data folder),
            // this will copy them into AppData the first time you run this version.
            TryMigrateFromOldLocation(warehouseKey, path);

            if (!File.Exists(path))
                return new List<PartTemplate>();

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<PartTemplate>>(
                           json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                       ) ?? new List<PartTemplate>();
            }
            catch
            {
                // If JSON is corrupted, don't crash the app
                return new List<PartTemplate>();
            }
        }

        public void Save(string warehouseKey, List<PartTemplate> templates)
        {
            var path = GetTemplatesPath(warehouseKey);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(path, json);
            }
            catch
            {
                // optionally log to LocalAppData\PartsPortal\Logs
            }
        }


        public void Upsert(List<PartTemplate> templates, PartTemplate t)
        {
            var existing = templates.FirstOrDefault(x =>
                string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                existing.Lines = t.Lines;
            else
                templates.Add(t);
        }

        public void Delete(List<PartTemplate> templates, string name)
        {
            templates.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private string GetTemplatesPath(string warehouseKey)
        {
            warehouseKey = SanitizeKey(warehouseKey);
            return Path.Combine(_dataDir, $"templates_{warehouseKey}.json");
        }

        private void TryMigrateFromOldLocation(string warehouseKey, string newPath)
        {
            if (File.Exists(newPath)) return;

            // Candidate old locations (try in order)
            var candidates = new List<string>();

            // 1) Old portable-style location: <exe>\Data\templates_<warehouseKey>.json
            candidates.Add(Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                $"templates_{warehouseKey}.json"));

            // 2) Old LocalAppData location (common/ideal)
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PartsPortal",
                "Data",
                $"templates_{warehouseKey}.json"));

            // 3) Old Documents location (if you ever used Documents)
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PartsPortal",
                "Data",
                $"templates_{warehouseKey}.json"));

            string? oldPath = candidates.FirstOrDefault(File.Exists);
            if (oldPath == null) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                File.Copy(oldPath, newPath, overwrite: false);
            }
            catch
            {
                // Migration failure should not break the app
            }
        }


        private static string SanitizeKey(string key)
        {
            key = (key ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return "unknown";

            foreach (var c in Path.GetInvalidFileNameChars())
                key = key.Replace(c, '_');

            return key;
        }
    }
}
