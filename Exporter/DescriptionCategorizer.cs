using System.Text;

namespace Exporter {
    //Splits the descriptions of the images into persons, tags and free texts.
    //
    //descriptions file: one line per image, "<number> entry, entry, entry"
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
        private readonly Dictionary<string, string> sameTags;
        private readonly HashSet<string> persons;
        private readonly HashSet<string> tags;

        //only for the debug file
        private readonly Dictionary<int, List<string>> rawLines = new Dictionary<int, List<string>>();
        private readonly List<string> skippedLines = new List<string>();
        private string[] files = new string[4];

        private DescriptionCategorizer(Dictionary<int, List<string>> descriptions, Dictionary<string, string> sameTags, HashSet<string> persons, HashSet<string> tags) {
            this.descriptions = descriptions;
            this.sameTags = sameTags;
            this.persons = persons;
            this.tags = tags;
        }

        //Every file can be left empty. Without a descriptions file the descriptions that are saved
        //in SchuleApi for every image (imageFolder\<number>\2) are used, entries separated by commas.
        public static DescriptionCategorizer Load(string descriptionsFile, string sameTagsFile, string personsFile, string tagsFile, IEnumerable<(int Number, string Description)> schuleApiDescriptions) {
            Dictionary<string, string> sameTags = readSameTags(sameTagsFile);

            Dictionary<int, List<string>> rawLines = new Dictionary<int, List<string>>();
            List<string> skippedLines = new List<string>();

            Dictionary<int, List<string>> descriptions = string.IsNullOrWhiteSpace(descriptionsFile)
                ? readSchuleApiDescriptions(schuleApiDescriptions, rawLines)
                : readDescriptions(descriptionsFile, rawLines, skippedLines);

            DescriptionCategorizer categorizer = new DescriptionCategorizer(
                descriptions,
                sameTags,
                readList(personsFile, sameTags),
                readList(tagsFile, sameTags));

            foreach (KeyValuePair<int, List<string>> pair in rawLines)
                categorizer.rawLines[pair.Key] = pair.Value;

            categorizer.skippedLines.AddRange(skippedLines);
            categorizer.files = new[] { descriptionsFile, sameTagsFile, personsFile, tagsFile };

            return categorizer;
        }

        //Writes step by step how the descriptions of the first images are split,
        //so it can be checked why an entry ends up as person, tag or free text.
        public void WriteDebugFile(string file, IEnumerable<int> numbers, int max = 100) {
            StringBuilder text = new StringBuilder();

            text.AppendLine("Debug der Beschreibungen, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("Einträge stehen in [Klammern], damit Leerzeichen sichtbar sind. Unsichtbare Zeichen werden als <U+XXXX> gezeigt.");
            text.AppendLine();

            string[] names = { "Beschreibungen", "Gleiche Tags", "Personen", "Tags" };
            for (int i = 0; i < files.Length; i++)
                text.AppendLine(names[i] + ": " + (string.IsNullOrWhiteSpace(files[i]) ? (i == 0 ? "(keine Datei, Beschreibungen aus der SchuleApi)" : "(keine Datei)") : files[i]));

            text.AppendLine();
            text.AppendLine("Eingelesen: " + descriptions.Count + " Bilder mit Beschreibung, " + sameTags.Count + " Einträge in Gruppen gleicher Tags, " + persons.Count + " Personen, " + tags.Count + " Tags");
            text.AppendLine("Personen (erste 30): " + string.Join(" ", persons.Take(30).Select(show)));
            text.AppendLine("Tags (erste 30): " + string.Join(" ", tags.Take(30).Select(show)));
            text.AppendLine("Gleiche Tags (erste 30): " + string.Join(" ", sameTags.Take(30).Select(pair => show(pair.Key) + "->" + show(pair.Value))));

            if (skippedLines.Count > 0) {
                text.AppendLine();
                text.AppendLine(skippedLines.Count + " Zeilen der Beschreibungen ohne Bildnummer am Anfang wurden ignoriert, die ersten:");
                foreach (string line in skippedLines.Take(20))
                    text.AppendLine("  " + show(line));
            }

            List<int> list = numbers.ToList();
            List<int> withDescription = list.Where(descriptions.ContainsKey).Take(max).ToList();
            int without = list.Count(number => !descriptions.ContainsKey(number));

            text.AppendLine();
            text.AppendLine(list.Count + " Bilder in diesem Lauf, davon " + without + " ohne Zeile in der Beschreibungsdatei.");
            if (without > 0)
                text.AppendLine("Bildnummern ohne Beschreibung (erste 30): " + string.Join(", ", list.Where(number => !descriptions.ContainsKey(number)).Take(30)));

            foreach (int number in withDescription) {
                text.AppendLine();
                text.AppendLine("=== Bild " + number);

                foreach (string line in rawLines[number])
                    text.AppendLine("Zeile: " + show(line));

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
                if (char.IsControl(c) || c == '\u00A0' || c == '\uFEFF' || c == '\uFFFD' || c == '\u200B')
                    shown.Append("<U+" + ((int)c).ToString("X4") + ">");
                else
                    shown.Append(c);
            }

            return shown.Append(']').ToString();
        }

        public int Count => descriptions.Count;

        public Categories Categorize(int number) {
            List<string>? entries;

            if (!descriptions.TryGetValue(number, out entries))
                return new Categories();

            return categorize(entries);
        }

        //for descriptions that are not in the descriptions file, e.g. of the videos
        public Categories CategorizeText(string description) {
            return categorize(splitEntries(description.Replace('\r', ',').Replace('\n', ',')));
        }

        private Categories categorize(List<string> entries) {
            Categories categories = new Categories();

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

        private static Dictionary<int, List<string>> readDescriptions(string file, Dictionary<int, List<string>> rawLines, List<string> skippedLines) {
            Dictionary<int, List<string>> descriptions = new Dictionary<int, List<string>>();

            foreach (string line in readLines(file)) {
                int end = 0;

                while (end < line.Length && char.IsDigit(line[end]))
                    end++;

                int number;

                if (end == 0 || !int.TryParse(line.Substring(0, end), out number)) {
                    skippedLines.Add(line);
                    continue;
                }

                List<string> raw;

                if (!rawLines.TryGetValue(number, out raw!)) {
                    raw = new List<string>();
                    rawLines[number] = raw;
                }

                raw.Add(line);

                List<string> entries;

                if (!descriptions.TryGetValue(number, out entries!)) {
                    entries = new List<string>();
                    descriptions[number] = entries;
                }

                entries.AddRange(splitEntries(line.Substring(end)));
            }

            return descriptions;
        }

        private static Dictionary<int, List<string>> readSchuleApiDescriptions(IEnumerable<(int Number, string Description)> images, Dictionary<int, List<string>> rawLines) {
            Dictionary<int, List<string>> descriptions = new Dictionary<int, List<string>>();

            foreach ((int number, string description) in images) {
                List<string> entries = splitEntries(description.Replace('\r', ',').Replace('\n', ','));

                if (entries.Count == 0)
                    continue;

                descriptions[number] = entries;
                rawLines[number] = new List<string> { description };
            }

            return descriptions;
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
