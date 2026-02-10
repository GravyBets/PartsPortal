using MaterialReqAppV3.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace MaterialReqAppV3
{
    public class SettingsService
    {
        private const string AppFolderName = "MaterialReqAppV3";

        private static string? TryResolveTeamsPartsCsvPath()
        {
            // OneDrive for Business first (Teams shortcut/sync lands here)
            string? oneDrive = Environment.GetEnvironmentVariable("OneDriveCommercial")
                           ?? Environment.GetEnvironmentVariable("OneDrive");

            if (string.IsNullOrWhiteSpace(oneDrive))
                return null;

            // Common folder shapes depending on "Sync" vs "Add shortcut"
            string[] candidates =
            {
        Path.Combine(oneDrive, "Warehouse Parts", "PartsList.csv"),
        Path.Combine(oneDrive, "Smart Grid Communications", "Warehouse Parts", "PartsList.csv"),
        Path.Combine(oneDrive, "Radio Communications", "Smart Grid Communications", "Warehouse Parts", "PartsList.csv"),
        Path.Combine(oneDrive, "Radio Communications", "Warehouse Parts", "PartsList.csv"),
    };

            foreach (var c in candidates)
                if (File.Exists(c))
                    return c;

            // Last resort: search by filename (can be slower on huge OneDrive folders)
            try
            {
                var found = Directory.EnumerateFiles(oneDrive, "PartsList.csv", SearchOption.AllDirectories)
                    .FirstOrDefault(p => p.Contains("Warehouse Parts", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(found) && File.Exists(found))
                    return found;
            }
            catch { }

            return null;
        }


        public string GetSettingsPath()
        {
            string user = Environment.UserName;

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName);

            return Path.Combine(dir, $"settings_{user}.json");
        }

        public UserSettings Load()
        {
            string path = GetSettingsPath();
            string user = Environment.UserName;

            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var s = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();

                    ApplyDefaults(s, user);
                    return s;
                }
                catch
                {
                    // fall through to default
                }
            }

            var fresh = new UserSettings { EmployeeId = user };
            ApplyDefaults(fresh, user);
            return fresh;
        }


        public void Save(UserSettings settings)
        {
            string path = GetSettingsPath();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }

        private static void ApplyDefaults(UserSettings s, string user)
        {
            // Always ensure EmployeeId is set
            if (string.IsNullOrWhiteSpace(s.EmployeeId))
                s.EmployeeId = user;

            // Email defaults / migration
            if (string.IsNullOrWhiteSpace(s.EmailSubjectTemplate))
                s.EmailSubjectTemplate = "Material Requisition - {Warehouse} - {Date}";

            // ✅ Seed per-user email directory if empty (first run)
            s.EmailDirectory ??= new List<string>();

            if (s.EmailDirectory.Count == 0)
            {
                // Put your shop defaults here (one per line)
                s.EmailDirectory.AddRange(new[]
                {
            "smartgridradio@centerpointenergy.com",
            "guy1@centerpointenergy.com",
            "guy2@centerpointenergy.com"
        });
            }
        }


    }
}
