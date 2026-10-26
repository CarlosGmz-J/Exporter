using System.Text.Json;

namespace Exporter {
    //the paths of the last run, they are filled in again when the app is opened the next time
    public class Settings {
        public string? Source { get; set; }
        public string? Drive { get; set; }
        public bool AllDrives { get; set; }
        public string? Folder { get; set; }
        public string? Descriptions { get; set; }
        public string? SameTags { get; set; }
        public string? Persons { get; set; }
        public string? Tags { get; set; }

        private static string file => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Exporter", "settings.json");

        public static Settings Load() {
            try {
                if (File.Exists(file))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) ?? new Settings();
            } catch (Exception e) {
                Console.WriteLine(e.Message);
            }

            return new Settings();
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            } catch (Exception e) {
                Console.WriteLine(e.Message);
            }
        }
    }
}
