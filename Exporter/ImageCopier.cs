using StoredImage = Exporter.SchuleImageStore.StoredImage;

namespace Exporter {
    //writes the images of a SchuleImageStore as normal image files to another folder, e.g. on a usb stick
    public static class ImageCopier {
        public class Result {
            public int Copied;
            public int Skipped;
            public int Failed;
            public bool Cancelled;
            public string? FirstError;
            //images that are on the destination now, with their file name
            public List<(int Number, string FileName)> Files = new List<(int Number, string FileName)>();
        }

        //the stored files are as big as the real images, only every byte is shifted
        public static long GetSize(IEnumerable<StoredImage> images) {
            long size = 0;

            foreach (StoredImage image in images) {
                FileInfo file = new FileInfo(image.FilePath);

                if (file.Exists)
                    size += file.Length;
            }

            return size;
        }

        public static Result Copy(IReadOnlyList<StoredImage> images, string destination, bool overwrite, IProgress<int> progress, CancellationToken token) {
            Result result = new Result();

            Directory.CreateDirectory(destination);

            for (int i = 0; i < images.Count; i++) {
                if (token.IsCancellationRequested) {
                    result.Cancelled = true;
                    break;
                }

                StoredImage image = images[i];

                try {
                    byte[]? bytes = SchuleImageStore.ReadFileByteShift(image.FilePath);

                    if (bytes == null) {
                        fail(result, "Bild " + image.Number + " konnte nicht gelesen werden.");
                    } else {
                        string target = Path.Combine(destination, "image_" + image.Number + getExtension(bytes));

                        if (File.Exists(target) && !overwrite) {
                            result.Skipped++;
                        } else {
                            File.WriteAllBytes(target, bytes);
                            result.Copied++;
                        }

                        result.Files.Add((image.Number, Path.GetFileName(target)));
                    }
                } catch (IOException e) when (isDiskFull(e)) {
                    fail(result, "Der Datenträger ist voll.");
                    break;
                } catch (Exception e) {
                    fail(result, "Bild " + image.Number + ": " + e.Message);
                }

                progress.Report(i + 1);
            }

            return result;
        }

        private static void fail(Result result, string error) {
            result.Failed++;
            result.FirstError ??= error;
        }

        private static bool isDiskFull(IOException e) {
            const int ERROR_HANDLE_DISK_FULL = 0x27;
            const int ERROR_DISK_FULL = 0x70;

            int code = e.HResult & 0xFFFF;

            return code == ERROR_HANDLE_DISK_FULL || code == ERROR_DISK_FULL;
        }

        //SchuleApi saves bitmaps as jpeg, but images added as bytes can be anything
        private static string getExtension(byte[] bytes) {
            if (startsWith(bytes, 0x89, 0x50, 0x4E, 0x47))
                return ".png";
            if (startsWith(bytes, 0x47, 0x49, 0x46))
                return ".gif";
            if (startsWith(bytes, 0x42, 0x4D))
                return ".bmp";

            return ".jpg";
        }

        private static bool startsWith(byte[] bytes, params byte[] start) {
            if (bytes.Length < start.Length)
                return false;

            for (int i = 0; i < start.Length; i++)
                if (bytes[i] != start[i])
                    return false;

            return true;
        }
    }
}
