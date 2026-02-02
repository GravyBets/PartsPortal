using MaterialReqAppV3.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MaterialReqAppV3.Services
{
    public class TemplatesService
    {
        private readonly string _path;

        public TemplatesService()
        {
            string appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MaterialReqAppV3");

            Directory.CreateDirectory(appDir);
            _path = Path.Combine(appDir, "templates.json");
        }

        public List<PartTemplate> Load(string warehouseKey)
        {
            var path = GetTemplatesPath(warehouseKey);
            if (!File.Exists(path)) return new List<PartTemplate>();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<PartTemplate>>(json) ?? new List<PartTemplate>();
        }


        public void Save(string warehouseKey, List<PartTemplate> templates)
        {
            var path = GetTemplatesPath(warehouseKey);
            var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }


        public void Upsert(List<PartTemplate> templates, PartTemplate t)
        {
            var existing = templates.FirstOrDefault(x =>
                string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Lines = t.Lines;
            }
            else
            {
                templates.Add(t);
            }
        }

        public void Delete(List<PartTemplate> templates, string name)
        {
            templates.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private string GetTemplatesPath(string warehouseKey)
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir);
            return Path.Combine(dataDir, $"templates_{warehouseKey}.json");
        }

    }
}
