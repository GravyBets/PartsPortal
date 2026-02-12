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
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
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

            // Old location: <exe>\Data\templates_<warehouseKey>.json
            var oldPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Data",
                $"templates_{warehouseKey}.json");

            if (!File.Exists(oldPath)) return;

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
