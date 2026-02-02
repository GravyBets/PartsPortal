using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MaterialReqAppV3.Services
{
    public class FavoritesService
    {
        private readonly string _path;

        public FavoritesService()
        {
            string appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MaterialReqAppV3");

            Directory.CreateDirectory(appDir);
            _path = Path.Combine(appDir, "favorites.json");
        }

        public HashSet<string> Load()
        {
            try
            {
                if (!File.Exists(_path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var json = File.ReadAllText(_path);
                var list = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Save(HashSet<string> favorites)
        {
            var list = new List<string>(favorites);
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
    }
}
