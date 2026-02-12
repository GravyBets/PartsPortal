using PartsPortal.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace PartsPortal
{
    public class SettingsService
    {
        private const string AppFolderName = "PartsPortal";

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

            // Ensure EmailDirectory exists (older JSON won’t have it)
            s.EmailDirectory ??= new System.Collections.ObjectModel.ObservableCollection<EmailDirectoryEntry>();


            // ✅ Seed ONLY ONCE (first time ever)
            if (!s.EmailDirectorySeeded)
            {
                // Only seed if the directory is empty (fresh user)
                if (s.EmailDirectory.Count == 0)
                {
                    var seeds = new[]
                    {
                        new EmailDirectoryEntry { FirstName="Alex",  LastName="Pletan",     Email="alexander.pletan@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Andrew",     LastName="Bieber",     Email="andrew.bieber@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Anthony",    LastName="Marable",    Email="anthony.marable@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Brandon",    LastName="Palmer",     Email="brandon.t.palmer@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Brian",      LastName="White",      Email="brian.j.white@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Christopher",LastName="Harden",     Email="christopher.harden@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Colton",     LastName="Williams",   Email="colton.williams@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Daniel",     LastName="Nunez",      Email="daniel.nunez@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="David",      LastName="Kuipers",    Email="david.kuipers@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="David",      LastName="Murillo",    Email="david.murillo@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Dylan",      LastName="Steele",     Email="dylan.steele@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Jim",        LastName="Stamper",    Email="james.stamperjr@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Jesselle",   LastName="Juarez",     Email="jesselle.juarez@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Joseph",     LastName="Burrow",     Email="joseph.burrow@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Juan",       LastName="Nunez",      Email="juan.nunez@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Kevin",      LastName="Perez",      Email="kevin.perez@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Levi",       LastName="Heasley",    Email="levi.heasley@centerpointenergy.com" },                        
                        new EmailDirectoryEntry { FirstName="Michael",    LastName="Lindemann",  Email="michael.lindemann@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Michael",    LastName="Topping",    Email="michael.topping@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Philip",     LastName="Ingram",     Email="philip.ingram@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Rodney",     LastName="Logsdon",    Email="rodney.logsdon@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Scott",      LastName="Thomas",     Email="scott.thomas@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Shay",       LastName="Peterson",   Email="shay.peterson@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Thomas",     LastName="Huynh",      Email="thomas.huynh@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Timothy",    LastName="Gonzales",   Email="timothy.gonzales@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Travis",     LastName="Presley",    Email="travis.presley@centerpointenergy.com" },
                        new EmailDirectoryEntry { FirstName="Wilson",     LastName="Cothran",    Email="wilson.cothran@centerpointenergy.com" }
                    };

                    foreach (var entry in seeds)
                        s.EmailDirectory.Add(entry);
                }

                // Mark as seeded so we NEVER re-add defaults again
                s.EmailDirectorySeeded = true;
            }

            // ✅ Optional: auto-resolve Teams CSV path if current is missing/invalid
            if (string.IsNullOrWhiteSpace(s.CsvPath) ||
                s.CsvPath.EndsWith(".csv.url", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(s.CsvPath))
            {
                var resolved = TryResolveTeamsPartsCsvPath();
                if (!string.IsNullOrWhiteSpace(resolved))
                    s.CsvPath = resolved;
            }
        }
    }
}
