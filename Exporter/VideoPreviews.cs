using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Exporter {
    //takes screenshots spread over a copied video and saves them as <previews>\<number>\0.png, 1.png, ...
    //The screenshots are made by ffmpeg.exe, which is searched next to the app and then in the PATH.
    public static class VideoPreviews {
        //a 1 minute video gets 5 previews, a 10 minute video 20, in between it grows linearly and longer videos keep 20
        public static int GetCount(TimeSpan length) {
            double minutes = length.TotalMinutes;

            if (minutes <= 1)
                return 5;

            return Math.Min(20, (int)Math.Round(5 + (minutes - 1) * 15 / 9));
        }

        public static string GetFolder(string previews, int number) {
            return Path.Combine(previews, number.ToString());
        }

        //returns false if it was cancelled. The pictures are written to <number>.part first and the folder is renamed
        //at the end, so an interrupted folder never looks finished and is done again the next time.
        public static bool Create(string videoFile, string folder, int lengthSeconds, bool overwrite, CancellationToken token) {
            if (!overwrite && Directory.Exists(folder))
                return true;

            string part = folder + ".part";

            if (Directory.Exists(part))
                Directory.Delete(part, true);

            Directory.CreateDirectory(part);

            TimeSpan length = lengthSeconds > 0 ? TimeSpan.FromSeconds(lengthSeconds) : readLength(videoFile);
            int count = GetCount(length);

            for (int i = 0; i < count; i++) {
                if (token.IsCancellationRequested) {
                    Directory.Delete(part, true);
                    return false;
                }

                //the middle of each of the count equal parts, so neither the first nor the last (often black) frame is used
                TimeSpan time = TimeSpan.FromTicks(length.Ticks * (2 * i + 1) / (2 * count));
                string target = Path.Combine(part, i + ".png");

                //-ss before -i jumps straight to the position instead of decoding the video from the start
                runFfmpeg("-hide_banner -loglevel error -ss " + time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                    + " -i \"" + Path.GetFullPath(videoFile) + "\" -frames:v 1 -y \"" + target + "\"");

                if (!File.Exists(target))
                    throw new IOException("ffmpeg hat kein Bild bei " + time.ToString(@"hh\:mm\:ss") + " erzeugt.");
            }

            if (Directory.Exists(folder))
                Directory.Delete(folder, true);

            Directory.Move(part, folder);

            return true;
        }

        //only used if SchuleApi has no length for the video, ffmpeg prints "Duration: 00:10:02.35" for the input
        private static TimeSpan readLength(string videoFile) {
            string output = runFfmpeg("-hide_banner -i \"" + Path.GetFullPath(videoFile) + "\"");
            Match match = Regex.Match(output, @"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)");

            if (!match.Success)
                throw new IOException("ffmpeg konnte die Länge des Videos nicht lesen.");

            return TimeSpan.FromHours(int.Parse(match.Groups[1].Value))
                + TimeSpan.FromMinutes(int.Parse(match.Groups[2].Value))
                + TimeSpan.FromSeconds(double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        private static string findFfmpeg() {
            string local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");

            if (File.Exists(local))
                return local;

            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
                try {
                    string file = Path.Combine(folder.Trim('"'), "ffmpeg.exe");

                    if (File.Exists(file))
                        return file;
                } catch (ArgumentException) {
                }
            }

            throw new FileNotFoundException("ffmpeg.exe nicht gefunden (neben der App oder im PATH ablegen).");
        }

        private static string? ffmpeg;

        //returns what ffmpeg wrote to stderr, it is read completely so ffmpeg never blocks on a full pipe
        private static string runFfmpeg(string arguments) {
            ffmpeg ??= findFfmpeg();

            ProcessStartInfo start = new ProcessStartInfo(ffmpeg, arguments) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

            using Process process = Process.Start(start)!;

            string output = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return output;
        }
    }
}
