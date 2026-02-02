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

            // If you want: keep draft default true unless explicitly set
            // (bool already defaults to false in C#, but you set it true in the model;
            // this just protects against old files that might have it missing/false if you ever change defaults)
        }

    }
}
