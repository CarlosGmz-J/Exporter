using StoredVideo = Exporter.SchuleVideoStore.StoredVideo;

namespace Exporter {
    //writes the videos of a SchuleVideoStore as normal video files (and their thumbnails as jpeg) to another folder
    public static class VideoCopier {
        public class Result {
            public int Copied;
            public int Skipped;
            public int Failed;
            public bool Cancelled;
            public string? FirstError;
            //videos that are on the destination now, with the file names of the video and the thumbnail
            public List<(StoredVideo Video, string FileName, string? Thumbnail)> Files = new List<(StoredVideo Video, string FileName, string? Thumbnail)>();
        }

        private static readonly string[] extensions = { ".mp4", ".mkv", ".avi" };

        private const int bufferSize = 1 << 20;

        private static string? findCopied(string destination, int number) {
            foreach (string extension in extensions) {
                string file = Path.Combine(destination, "video_" + number + extension);

                if (File.Exists(file))
                    return file;
            }

            return null;
        }

        public static long GetSize(IEnumerable<StoredVideo> videos, string destination, bool overwrite) {
            long size = 0;

            foreach (StoredVideo video in videos) {
                if (!overwrite && findCopied(destination, video.Number) != null)
                    continue;

                foreach (string file in new[] { video.VideoFile, video.ThumbnailFile }) {
                    FileInfo info = new FileInfo(file);

                    if (info.Exists)
                        size += info.Length;
                }
            }

            return size;
        }

        public static Result Copy(IReadOnlyList<StoredVideo> videos, string destination, bool overwrite, IProgress<int> progress, CancellationToken token) {
            Result result = new Result();

            Directory.CreateDirectory(destination);

            for (int i = 0; i < videos.Count; i++) {
                if (token.IsCancellationRequested) {
                    result.Cancelled = true;
                    break;
                }

                StoredVideo video = videos[i];

                try {
                    string? copied = overwrite ? null : findCopied(destination, video.Number);
                    string? thumbnail = copyThumbnail(video, destination, overwrite);

                    if (copied != null) {
                        result.Skipped++;
                        result.Files.Add((video, Path.GetFileName(copied), thumbnail));
                    } else if (!File.Exists(video.VideoFile)) {
                        fail(result, "Video " + video.Number + " hat keine Videodatei.");
                    } else {
                        string? target = copyVideo(video, destination, token);

                        if (target == null) {
                            result.Cancelled = true;
                            break;
                        }

                        result.Copied++;
                        result.Files.Add((video, Path.GetFileName(target), thumbnail));
                    }
                } catch (IOException e) when (isDiskFull(e)) {
                    fail(result, "Der Datenträger ist voll.");
                    break;
                } catch (Exception e) {
                    fail(result, "Video " + video.Number + ": " + e.Message);
                }

                progress.Report(i + 1);
            }

            return result;
        }

        //Videos can be very big, so they are shifted back in parts instead of reading them at once.
        //The file is written as .part first, so a cancelled copy never looks like a finished video.
        private static string? copyVideo(StoredVideo video, string destination, CancellationToken token) {
            byte[] buffer = new byte[bufferSize];

            using FileStream input = new FileStream(video.VideoFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);

            int read = input.Read(buffer, 0, buffer.Length);
            SchuleImageStore.ShiftBytes(buffer, read, -1);

            string target = Path.Combine(destination, "video_" + video.Number + getExtension(buffer, read));
            string part = target + ".part";

            using (FileStream output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize)) {
                while (read > 0) {
                    if (token.IsCancellationRequested)
                        break;

                    output.Write(buffer, 0, read);

                    read = input.Read(buffer, 0, buffer.Length);
                    SchuleImageStore.ShiftBytes(buffer, read, -1);
                }
            }

            if (token.IsCancellationRequested) {
                File.Delete(part);
                return null;
            }

            File.Move(part, target, true);

            return target;
        }

        private static string? copyThumbnail(StoredVideo video, string destination, bool overwrite) {
            string target = Path.Combine(destination, "video_" + video.Number + ".jpg");

            if (File.Exists(target) && !overwrite)
                return Path.GetFileName(target);

            byte[]? bytes = SchuleImageStore.ReadFileByteShift(video.ThumbnailFile);

            if (bytes == null)
                return null;

            File.WriteAllBytes(target, bytes);

            return Path.GetFileName(target);
        }

        //SchuleApi saves the videos as mp4, other containers are recognized by their first bytes
        private static string getExtension(byte[] bytes, int length) {
            if (length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
                return ".mkv";
            if (length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'A' && bytes[9] == 'V' && bytes[10] == 'I')
                return ".avi";

            return ".mp4";
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
    }
}
