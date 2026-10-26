using System.Text;

namespace Exporter {
    //Reads the videos exactly like SchuleApi2 (VideosApi and Video): every video has its own folder
    //<root>\videoFolder\<number> with the files 1 = video, 2 = thumbnail (jpeg), 3 = name,
    //4 = description and 5 = length in seconds or "minutes:seconds". Every byte is shifted by one.
    public class SchuleVideoStore {
        public class StoredVideo {
            public int Number;
            public string Name = "";
            public string Description = "";
            public int LengthSeconds;
            public string Folder = "";

            public string VideoFile => Folder + "\\1";
            public string ThumbnailFile => Folder + "\\2";
        }

        private readonly List<StoredVideo> videos;

        private SchuleVideoStore(List<StoredVideo> videos) {
            this.videos = videos;
        }

        public static string GetVideoFolder(string root) {
            return Path.Combine(root, "videoFolder");
        }

        public static bool IsVideoStore(string root) {
            return Directory.Exists(GetVideoFolder(root));
        }

        //same as VideosApi.preloadThreaded and Video.load(path)
        public static SchuleVideoStore Load(string root) {
            List<StoredVideo> videos = new List<StoredVideo>();

            foreach (string folder in Directory.GetDirectories(GetVideoFolder(root))) {
                int number;

                if (!int.TryParse(new DirectoryInfo(folder).Name, out number))
                    continue;

                videos.Add(new StoredVideo { Number = number, Folder = folder });
            }

            Parallel.ForEach(videos, video => {
                video.Name = readText(video.Folder + "\\3");
                video.Description = readText(video.Folder + "\\4");
                video.LengthSeconds = parseLength(readText(video.Folder + "\\5"));
            });

            videos.Sort((a, b) => a.Number.CompareTo(b.Number));

            return new SchuleVideoStore(videos);
        }

        public int Count => videos.Count;

        //all videos whose name or description contains the filter
        public List<StoredVideo> GetVideos(string filter) {
            return videos
                .Where(video => filter.Length == 0
                    || video.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || video.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static string readText(string file) {
            byte[]? bytes = SchuleImageStore.ReadFileByteShift(file);

            return bytes == null ? "" : Encoding.Default.GetString(bytes);
        }

        private static int parseLength(string length) {
            string[] parts = length.Trim().Split(':');
            int minutes, seconds;

            if (parts.Length == 2 && int.TryParse(parts[0], out minutes) && int.TryParse(parts[1], out seconds))
                return minutes * 60 + seconds;

            return int.TryParse(length.Trim(), out seconds) ? seconds : 0;
        }
    }
}
