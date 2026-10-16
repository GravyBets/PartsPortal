using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace PartsPortal.Services
{
    public enum AppTheme
    {
        Light = 0,
        BrightBlue = 1,
        Sandstone = 2,
        Graphite = 3,
        Cobalt = 4,
        Evergreen = 5,
        Ember = 6,
        Kuipers = 7,

        [Obsolete("Use Graphite instead.")]
        Slate = Graphite,

        [Obsolete("Use Cobalt instead.")]
        Midnight = Cobalt,

        [Obsolete("Use Cobalt instead.")]
        DeepBlue = Cobalt,

        [Obsolete("Use Cobalt instead.")]
        Dark = Cobalt,

        [Obsolete("Use Kuipers instead.")]
        NeonFlux = Kuipers
    }

    public sealed class AppThemeOption
    {
        public AppTheme Theme { get; init; }

        public string DisplayName { get; init; } = "";
    }

    public static class ThemeService
    {
        private const string LightPath = "Themes/LightTheme.xaml";
        private const string BrightBluePath = "Themes/BrightBlueTheme.xaml";
        private const string SandstonePath = "Themes/SandstoneTheme.xaml";
        private const string GraphitePath = "Themes/GraphiteTheme.xaml";
        private const string CobaltPath = "Themes/CobaltTheme.xaml";
        private const string EvergreenPath = "Themes/EvergreenTheme.xaml";
        private const string EmberPath = "Themes/EmberTheme.xaml";
        private const string KuipersPath = "Themes/KuipersTheme.xaml";

        // Retired PartsPortal theme. Kept here so it is removed if an older
        // application session/resource dictionary is encountered.
        private const string LegacyDarkPath = "Themes/DarkTheme.xaml";

        private static readonly string[] KnownThemePaths =
        {
            LightPath,
            BrightBluePath,
            SandstonePath,
            GraphitePath,
            CobaltPath,
            EvergreenPath,
            EmberPath,
            KuipersPath,
            LegacyDarkPath
        };

        private static readonly string SettingsFolder =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "PartsPortal");

        private static readonly string ThemeSettingsFile =
            Path.Combine(
                SettingsFolder,
                "theme.txt");

        private static readonly Uri CompanyLogoBlackTextUri =
            new(
                "pack://application:,,,/Assets/CenterPointLogo_DarkText.png",
                UriKind.Absolute);

        private static readonly Uri CompanyLogoWhiteTextUri =
            new(
                "pack://application:,,,/Assets/CenterPointLogo_LightText.png",
                UriKind.Absolute);

        public static IReadOnlyList<AppThemeOption> ThemeOptions { get; } =
            new List<AppThemeOption>
            {
                new() { Theme = AppTheme.Light, DisplayName = "Light of 1000 Suns" },
                new() { Theme = AppTheme.BrightBlue, DisplayName = "Old Pingo Vibes" },
                new() { Theme = AppTheme.Sandstone, DisplayName = "Sandstone" },
                new() { Theme = AppTheme.Graphite, DisplayName = "Graphite" },
                new() { Theme = AppTheme.Cobalt, DisplayName = "Cobalt Blue" },
                new() { Theme = AppTheme.Evergreen, DisplayName = "Broccoli" },
                new() { Theme = AppTheme.Ember, DisplayName = "Mesa Biome" },
                new() { Theme = AppTheme.Kuipers, DisplayName = "'David Kuipers'" }
            };

        public static event EventHandler? ThemeChanged;

        public static AppTheme Current { get; private set; } =
            AppTheme.Light;

        // Compatibility for older PartsPortal code while the old IsDarkMode
        // setting ages out. Theme selection itself is now multi-theme.
        public static bool IsDark =>
            Current != AppTheme.Light &&
            Current != AppTheme.BrightBlue &&
            Current != AppTheme.Sandstone;

        public static Uri CurrentCompanyLogoUri =>
            NormalizeTheme(Current) switch
            {
                AppTheme.Light => CompanyLogoBlackTextUri,
                AppTheme.BrightBlue => CompanyLogoBlackTextUri,
                AppTheme.Sandstone => CompanyLogoBlackTextUri,
                _ => CompanyLogoWhiteTextUri
            };

        // Preserve existing light/dark settings when migrating to named themes.
        public static void ApplySavedTheme(bool legacyDarkMode = false)
        {
            var hasNamedTheme = File.Exists(ThemeSettingsFile);
            Apply(
                ReadSavedTheme(legacyDarkMode),
                saveChoice: !hasNamedTheme);
        }

        public static void Apply(AppTheme theme)
        {
            Apply(
                theme,
                saveChoice: true);
        }

        private static void Apply(
            AppTheme theme,
            bool saveChoice)
        {
            var app = Application.Current;

            if (app == null)
                return;

            var normalizedTheme =
                NormalizeTheme(theme);

            var nextPath =
                GetThemePath(normalizedTheme);

            var merged =
                app.Resources.MergedDictionaries;

            var existingThemeDictionaries =
                merged
                    .Where(IsKnownThemeDictionary)
                    .ToList();

            var nextDictionary =
                new ResourceDictionary
                {
                    Source = new Uri(
                        nextPath,
                        UriKind.Relative)
                };

            if (existingThemeDictionaries.Count > 0)
            {
                var insertionIndex =
                    merged.IndexOf(existingThemeDictionaries[0]);

                foreach (var dictionary in existingThemeDictionaries)
                    merged.Remove(dictionary);

                if (insertionIndex < 0 ||
                    insertionIndex > merged.Count)
                {
                    insertionIndex = merged.Count;
                }

                merged.Insert(
                    insertionIndex,
                    nextDictionary);
            }
            else
            {
                merged.Add(nextDictionary);
            }

            var themeActuallyChanged =
                Current != normalizedTheme;

            Current =
                normalizedTheme;

            if (saveChoice)
                SaveTheme(normalizedTheme);

            if (themeActuallyChanged)
                ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static bool IsKnownThemeDictionary(
            ResourceDictionary dictionary)
        {
            if (dictionary.Source == null)
                return false;

            var source =
                dictionary.Source.OriginalString;

            return KnownThemePaths.Any(path =>
                source.EndsWith(
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static AppTheme NormalizeTheme(
            AppTheme theme)
        {
            return theme switch
            {
                AppTheme.Light => AppTheme.Light,
                AppTheme.BrightBlue => AppTheme.BrightBlue,
                AppTheme.Sandstone => AppTheme.Sandstone,
                AppTheme.Graphite => AppTheme.Graphite,
                AppTheme.Cobalt => AppTheme.Cobalt,
                AppTheme.Evergreen => AppTheme.Evergreen,
                AppTheme.Ember => AppTheme.Ember,
                AppTheme.Kuipers => AppTheme.Kuipers,
                _ => AppTheme.Light
            };
        }

        private static string GetThemePath(
            AppTheme theme)
        {
            return NormalizeTheme(theme) switch
            {
                AppTheme.Light => LightPath,
                AppTheme.BrightBlue => BrightBluePath,
                AppTheme.Sandstone => SandstonePath,
                AppTheme.Graphite => GraphitePath,
                AppTheme.Cobalt => CobaltPath,
                AppTheme.Evergreen => EvergreenPath,
                AppTheme.Ember => EmberPath,
                AppTheme.Kuipers => KuipersPath,
                _ => LightPath
            };
        }

        private static AppTheme ReadSavedTheme(bool legacyDarkMode)
        {
            var fallback = legacyDarkMode ? AppTheme.Cobalt : AppTheme.Light;

            try
            {
                if (!File.Exists(ThemeSettingsFile))
                    return fallback;

                var raw =
                    File.ReadAllText(ThemeSettingsFile)
                        .Trim();

                if (string.IsNullOrWhiteSpace(raw))
                    return fallback;

                var migratedTheme =
                    raw.ToUpperInvariant() switch
                    {
                        "DARK" => AppTheme.Cobalt,
                        "DEEPBLUE" => AppTheme.Cobalt,
                        "MIDNIGHT" => AppTheme.Cobalt,
                        "SLATE" => AppTheme.Graphite,
                        "NEONFLUX" => AppTheme.Kuipers,
                        _ => (AppTheme?)null
                    };

                if (migratedTheme.HasValue)
                    return migratedTheme.Value;

                if (Enum.TryParse<AppTheme>(
                        raw,
                        ignoreCase: true,
                        out var parsedTheme))
                {
                    return NormalizeTheme(parsedTheme);
                }
            }
            catch
            {
                // A damaged local preference must never block startup.
            }

            return fallback;
        }

        private static void SaveTheme(
            AppTheme theme)
        {
            try
            {
                Directory.CreateDirectory(
                    SettingsFolder);

                File.WriteAllText(
                    ThemeSettingsFile,
                    GetThemeStorageName(theme));
            }
            catch
            {
                // The selected theme still remains active for this session.
            }
        }

        private static string GetThemeStorageName(
            AppTheme theme)
        {
            return NormalizeTheme(theme) switch
            {
                AppTheme.Light => nameof(AppTheme.Light),
                AppTheme.BrightBlue => nameof(AppTheme.BrightBlue),
                AppTheme.Sandstone => nameof(AppTheme.Sandstone),
                AppTheme.Graphite => nameof(AppTheme.Graphite),
                AppTheme.Cobalt => nameof(AppTheme.Cobalt),
                AppTheme.Evergreen => nameof(AppTheme.Evergreen),
                AppTheme.Ember => nameof(AppTheme.Ember),
                AppTheme.Kuipers => nameof(AppTheme.Kuipers),
                _ => nameof(AppTheme.Light)
            };
        }
    }
}
