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

        private DescriptionCategorizer(Dictionary<int, List<string>> descriptions, Dictionary<string, string> sameTags, HashSet<string> persons, HashSet<string> tags) {
            this.descriptions = descriptions;
            this.sameTags = sameTags;
            this.persons = persons;
            this.tags = tags;
        }

        //every file except the descriptions can be left empty
        public static DescriptionCategorizer Load(string descriptionsFile, string sameTagsFile, string personsFile, string tagsFile) {
            Dictionary<string, string> sameTags = readSameTags(sameTagsFile);

            return new DescriptionCategorizer(
                readDescriptions(descriptionsFile),
                sameTags,
                readList(personsFile, sameTags),
                readList(tagsFile, sameTags));
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

                if (persons.Contains(tag))
                    addOnce(categories.Persons, tag);
                else if (tags.Contains(tag))
                    addOnce(categories.Tags, tag);
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

        private static Dictionary<int, List<string>> readDescriptions(string file) {
            Dictionary<int, List<string>> descriptions = new Dictionary<int, List<string>>();

            foreach (string line in readLines(file)) {
                int end = 0;

                while (end < line.Length && char.IsDigit(line[end]))
                    end++;

                int number;

                if (end == 0 || !int.TryParse(line.Substring(0, end), out number))
                    continue;

                List<string> entries;

                if (!descriptions.TryGetValue(number, out entries!)) {
                    entries = new List<string>();
                    descriptions[number] = entries;
                }

                entries.AddRange(splitEntries(line.Substring(end)));
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
