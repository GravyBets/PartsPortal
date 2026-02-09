using System.IO;
using System.Text.Json;
using MaterialReqAppV3.Models;

namespace MaterialReqAppV3.Services
{
    public class BackorderStore
    {
        private readonly string _filePath;

        public BackorderStore(string? customPath = null)
        {
            _filePath = customPath ?? GetDefaultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        }

        public string FilePath => _filePath;

        public List<BackorderLine> Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return new List<BackorderLine>();

                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<BackorderLine>>(json) ?? new List<BackorderLine>();
            }
            catch
            {
                return new List<BackorderLine>();
            }
        }

        public void Save(IEnumerable<BackorderLine> items)
        {
            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }

        private static string GetDefaultPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MaterialReqAppV3");
            return Path.Combine(dir, "backorders.json");
        }
    }
}
