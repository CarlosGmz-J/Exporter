using System.Numerics;
using System.Text;

namespace Exporter {
    //Reads the images exactly like SchuleApi2 (ImagesApi, Image and FileExtension):
    //every image has its own folder <root>\imageFolder\<number>, the image is in the file "1"
    //and the description in the file "2". Every byte of both files is shifted by one.
    public class SchuleImageStore {
        public class StoredImage {
            public int Number;
            public string Description = "";
            public string FilePath = "";
        }

        private readonly List<StoredImage> images;

        private SchuleImageStore(List<StoredImage> images) {
            this.images = images;
        }

        public static string GetImageFolder(string root) {
            return Path.Combine(root, "imageFolder");
        }

        public static bool IsImageStore(string root) {
            return Directory.Exists(GetImageFolder(root));
        }

        //same as ImagesApi.preloadThreaded and Image.load(path)
        public static SchuleImageStore Load(string root) {
            string imageFolder = GetImageFolder(root);
            List<StoredImage> images = new List<StoredImage>();

            foreach (string folder in Directory.GetDirectories(imageFolder)) {
                int number;

                if (!int.TryParse(new DirectoryInfo(folder).Name, out number))
                    continue;

                images.Add(new StoredImage {
                    Number = number,
                    FilePath = imageFolder + "\\" + number + "\\1"
                });
            }

            Parallel.ForEach(images, image => {
                byte[]? description = ReadFileByteShift(imageFolder + "\\" + image.Number + "\\2");

                if (description != null)
                    image.Description = Encoding.Default.GetString(description);
            });

            images.Sort((a, b) => a.Number.CompareTo(b.Number));

            return new SchuleImageStore(images);
        }

        public int Count => images.Count;

        //all images whose description contains the filter
        public List<StoredImage> GetImages(string filter) {
            return images
                .Where(image => filter.Length == 0 || image.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        //copied from FileExtension.ReadFileByteShift in SchuleApi2
        public static byte[]? ReadFileByteShift(string path) {
            byte[]? bytes = null;

            try {
                bytes = File.ReadAllBytes(path);

                ShiftBytes(bytes, bytes.Length, -1);
            } catch (Exception e) {
                Console.WriteLine(e.StackTrace);
            }

            return bytes;
        }

        //adds delta (1 or -1) to the first length bytes, 255 + 1 wraps to 0 and 0 - 1 wraps to 255
        internal static void ShiftBytes(byte[] bytes, int length, int delta) {
            int i = 0;

            if (Vector.IsHardwareAccelerated) {
                Vector<byte> step = new Vector<byte>((byte)(delta & 0xFF));
                int width = Vector<byte>.Count;

                for (; i <= length - width; i += width) {
                    (new Vector<byte>(bytes, i) + step).CopyTo(bytes, i);
                }
            }

            for (; i < length; i++) {
                bytes[i] = unchecked((byte)(bytes[i] + delta));
            }
        }
    }
}
