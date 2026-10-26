using System.Text;

namespace Exporter {
    //Splits the descriptions of the images or videos into persons, tags and free texts.
    //
    //descriptions:      read from SchuleApi like it does it (image: imageFolder\<number>\2, video: videoFolder\<number>\4),
    //                   the entries are separated by commas
    //same tags file:    one group per line, "main tag, other spelling, other spelling",
    //                   every entry of a group is replaced by the first one
    //persons/tags file: entries separated by new lines or commas
    public class DescriptionCategorizer {
        public class Categories {
            public List<string> Persons = new List<string>();
            public List<string> Tags = new List<string>();
            public List<string> FreeTexts = new List<string>();
        }

        private readonly Dictionary<int, List<string>> descriptions;
        private readonly Dictionary<int, string> rawDescriptions;
        private readonly Dictionary<string, string> sameTags;
        private readonly HashSet<string> persons;
        private readonly HashSet<string> tags;

        //only for the debug file
        private readonly string[] files;

        private DescriptionCategorizer(Dictionary<int, List<string>> descriptions, Dictionary<int, string> rawDescriptions, Dictionary<string, string> sameTags, HashSet<string> persons, HashSet<string> tags, string[] files) {
            this.descriptions = descriptions;
            this.rawDescriptions = rawDescriptions;
            this.sameTags = sameTags;
            this.persons = persons;
            this.tags = tags;
            this.files = files;
        }

        //every file can be left empty
        public static DescriptionCategorizer Load(string sameTagsFile, string personsFile, string tagsFile, IEnumerable<(int Number, string Description)> schuleApiDescriptions) {
            Dictionary<string, string> sameTags = readSameTags(sameTagsFile);
            Dictionary<int, List<string>> descriptions = new Dictionary<int, List<string>>();
            Dictionary<int, string> rawDescriptions = new Dictionary<int, string>();

            foreach ((int number, string description) in schuleApiDescriptions) {
                List<string> entries = splitEntries(description.Replace('\r', ',').Replace('\n', ','));

                if (entries.Count == 0)
                    continue;

                descriptions[number] = entries;
                rawDescriptions[number] = description;
            }

            return new DescriptionCategorizer(
                descriptions,
                rawDescriptions,
                sameTags,
                readList(personsFile, sameTags),
                readList(tagsFile, sameTags),
                new[] { sameTagsFile, personsFile, tagsFile });
        }

        public int Count => descriptions.Count;

        public Categories Categorize(int number) {
            Categories categories = new Categories();
            List<string>? entries;

            if (!descriptions.TryGetValue(number, out entries))
                return categories;

            foreach (string entry in entries) {
                string tag;

                if (!sameTags.TryGetValue(entry, out tag!))
                    tag = entry;

                //the spelling of the persons and tags file is used
                string listed;

                if (persons.TryGetValue(tag, out listed!))
                    addOnce(categories.Persons, listed);
                else if (tags.TryGetValue(tag, out listed!))
                    addOnce(categories.Tags, listed);
                else
                    addOnce(categories.FreeTexts, tag);
            }

            return categories;
        }

        //Writes step by step how the descriptions of the first images or videos are split,
        //so it can be checked why an entry ends up as person, tag or free text.
        public void WriteDebugFile(string file, string kind, IEnumerable<int> numbers, int max = 100) {
            StringBuilder text = new StringBuilder();

            text.AppendLine("Debug der Beschreibungen (" + kind + "), " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("Die Beschreibungen kommen aus der SchuleApi. Einträge stehen in [Klammern], damit Leerzeichen sichtbar sind. Unsichtbare Zeichen werden als <U+XXXX> gezeigt.");
            text.AppendLine();

            string[] names = { "Gleiche Tags", "Personen", "Tags" };
            for (int i = 0; i < files.Length; i++)
                text.AppendLine(names[i] + ": " + (string.IsNullOrWhiteSpace(files[i]) ? "(keine Datei)" : files[i]));

            text.AppendLine();
            text.AppendLine("Eingelesen: " + descriptions.Count + " " + kind + " mit Beschreibung, " + sameTags.Count + " Einträge in Gruppen gleicher Tags, " + persons.Count + " Personen, " + tags.Count + " Tags");
            text.AppendLine("Personen (erste 30): " + string.Join(" ", persons.Take(30).Select(show)));
            text.AppendLine("Tags (erste 30): " + string.Join(" ", tags.Take(30).Select(show)));
            text.AppendLine("Gleiche Tags (erste 30): " + string.Join(" ", sameTags.Take(30).Select(pair => show(pair.Key) + "->" + show(pair.Value))));

            List<int> list = numbers.ToList();
            List<int> without = list.Where(number => !descriptions.ContainsKey(number)).ToList();

            text.AppendLine();
            text.AppendLine(list.Count + " " + kind + " in diesem Lauf, davon " + without.Count + " ohne Beschreibung.");
            if (without.Count > 0)
                text.AppendLine("Nummern ohne Beschreibung (erste 30): " + string.Join(", ", without.Take(30)));

            foreach (int number in list.Where(descriptions.ContainsKey).Take(max)) {
                text.AppendLine();
                text.AppendLine("=== Nummer " + number);
                text.AppendLine("Beschreibung: " + show(rawDescriptions[number]));

                foreach (string entry in descriptions[number]) {
                    string tag;
                    string step = show(entry);

                    if (sameTags.TryGetValue(entry, out tag!))
                        step += " -> gleiche Tags -> " + show(tag);
                    else
                        tag = entry;

                    string listed;

                    if (persons.TryGetValue(tag, out listed!))
                        step += " -> Person " + show(listed);
                    else if (tags.TryGetValue(tag, out listed!))
                        step += " -> Tag " + show(listed);
                    else
                        step += " -> Freitext (weder in Personen noch in Tags gefunden)";

                    text.AppendLine("  " + step);
                }

                Categories categories = Categorize(number);
                text.AppendLine("  Ergebnis: Personen " + string.Join(", ", categories.Persons) + " | Tags " + string.Join(", ", categories.Tags) + " | Freitexte " + string.Join(", ", categories.FreeTexts));
            }

            File.WriteAllText(file, text.ToString(), new UTF8Encoding(true));
        }

        //shows the text in brackets, invisible or unusual characters are written as their code
        private static string show(string text) {
            StringBuilder shown = new StringBuilder("[");

            foreach (char c in text) {
                if (char.IsControl(c) || c == ' ' || c == '﻿' || c == '�' || c == '​')
                    shown.Append("<U+" + ((int)c).ToString("X4") + ">");
                else
                    shown.Append(c);
            }

            return shown.Append(']').ToString();
        }

        private static void addOnce(List<string> list, string entry) {
            if (!list.Contains(entry, StringComparer.OrdinalIgnoreCase))
                list.Add(entry);
        }

        private static List<string> splitEntries(string text) {
            return text.Split(',')
                .Select(entry => entry.Trim())
                .Where(entry => entry.Length > 0)
                .ToList();
        }

        private static IEnumerable<string> readLines(string file) {
            if (string.IsNullOrWhiteSpace(file))
                return Enumerable.Empty<string>();

            return File.ReadAllLines(file)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
        }

        private static Dictionary<string, string> readSameTags(string file) {
            Dictionary<string, string> sameTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in readLines(file)) {
                List<string> group = splitEntries(line);

                if (group.Count == 0)
                    continue;

                foreach (string entry in group)
                    sameTags[entry] = group[0];
            }

            return sameTags;
        }

        //the entries are merged with the same tags too, so "Hunde" in the list also finds "Hund"
        private static HashSet<string> readList(string file, Dictionary<string, string> sameTags) {
            HashSet<string> list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in readLines(file)) {
                foreach (string entry in splitEntries(line)) {
                    string tag;

                    list.Add(sameTags.TryGetValue(entry, out tag!) ? tag : entry);
                }
            }

            return list;
        }
    }
}
