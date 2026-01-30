using MaterialReqAppV3.Models;
using System;
using System.IO;
using System.Text.Json;

namespace MaterialReqAppV3
{
    public class SettingsService
    {
        private const string AppFolderName = "MaterialReqAppV3";

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

                    // Always ensure EmployeeId is set
                    if (string.IsNullOrWhiteSpace(s.EmployeeId))
                        s.EmployeeId = user;

                    return s;
                }
                catch
                {
                    // fall through to default
                }
            }

            return new UserSettings { EmployeeId = user };
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
    }
}
