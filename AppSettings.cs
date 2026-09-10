using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace WindowScatter
{
    public class AppSettings
    {
        public string Hotkey { get; set; } = "Win+W";
        public bool EnableHotCorners { get; set; } = true;
        public string HotCornerPosition { get; set; } = "TopLeft";
        public int HotCornerDelay { get; set; } = 500;
        public double AnimationSpeed { get; set; } = 0.25;

        /// <summary>Launch automatically at logon (HKCU Run key). Applied on startup.</summary>
        public bool RunOnStartup { get; set; } = false;

        /// <summary>"auto" = DirectComposition when supported, "dcomp" = force-on, "legacy" = force old DWM-thumbnail path.</summary>
        public string Renderer { get; set; } = "auto";

        private static string SettingsPath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }

            var defaultSettings = new AppSettings();
            defaultSettings.Save();
            return defaultSettings;
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "WindowScatter";

        /// <summary>
        /// Makes the registry match <see cref="RunOnStartup"/> so the app (including
        /// the plain .exe release) survives restarts (#15).
        /// </summary>
        public void ApplyRunOnStartup()
        {
            try
            {
                string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                    return;

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                {
                    if (key == null)
                        return;

                    if (RunOnStartup)
                        key.SetValue(RunValueName, "\"" + exePath + "\"");
                    else if (key.GetValue(RunValueName) != null)
                        key.DeleteValue(RunValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply RunOnStartup: {ex.Message}");
            }
        }
    }
}
