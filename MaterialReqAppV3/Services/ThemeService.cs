using System;
using System.Linq;
using System.Windows;

namespace PartsPortal.Services
{
    public static class ThemeService
    {
        public static bool IsDark { get; private set; }

        public static event EventHandler? ThemeChanged;

        private static readonly Uri LightUri = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        private static readonly Uri DarkUri = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

        public static void Apply(bool dark)
        {
            IsDark = dark;

            var app = Application.Current;
            if (app == null) return;

            var dictionaries = app.Resources.MergedDictionaries;

            // Find current theme dictionary (if any)
            var existingTheme = dictionaries.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                 d.Source.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)));

            if (existingTheme != null)
                dictionaries.Remove(existingTheme);

            var next = new ResourceDictionary { Source = dark ? DarkUri : LightUri };

            // ✅ Theme must be LAST so it wins
            dictionaries.Add(next);

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

    }
}
