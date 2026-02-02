using System;
using System.Linq;
using System.Windows;

namespace MaterialReqAppV3.Services
{
    public static class ThemeService
    {
        public static bool IsDark { get; private set; }

        private static readonly Uri LightUri = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        private static readonly Uri DarkUri = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

        public static void Apply(bool dark)
        {
            IsDark = dark;

            var app = Application.Current;
            if (app == null) return;

            var dictionaries = app.Resources.MergedDictionaries;

            // Remove existing theme dictionary if present
            var existingTheme = dictionaries
                .FirstOrDefault(d =>
                    d.Source != null &&
                    (d.Source.OriginalString.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                     d.Source.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)));

            if (existingTheme != null)
                dictionaries.Remove(existingTheme);

            // Add the requested theme dictionary
            var next = new ResourceDictionary
            {
                Source = dark ? DarkUri : LightUri
            };

            dictionaries.Insert(0, next); // keep theme first so it wins
        }
    }
}
