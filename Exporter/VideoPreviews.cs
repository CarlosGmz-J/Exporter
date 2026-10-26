using System.Drawing.Imaging;
using Windows.Media.Editing;
using Windows.Storage;

namespace Exporter {
    //takes screenshots spread over a copied video and saves them as <previews>\<number>\0.png, 1.png, ...
    //Windows decodes the video itself (Windows.Media.Editing), so no ffmpeg is needed.
    public static class VideoPreviews {
        //a 1 minute video gets 5 previews, a 10 minute video 20, in between and above it grows linearly
        public static int GetCount(TimeSpan length) {
            double minutes = length.TotalMinutes;

            if (minutes <= 1)
                return 5;

            return (int)Math.Round(5 + (minutes - 1) * 15 / 9);
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

            StorageFile storageFile = StorageFile.GetFileFromPathAsync(Path.GetFullPath(videoFile)).AsTask().GetAwaiter().GetResult();
            MediaClip clip = MediaClip.CreateFromFileAsync(storageFile).AsTask().GetAwaiter().GetResult();

            MediaComposition composition = new MediaComposition();
            composition.Clips.Add(clip);

            TimeSpan length = clip.OriginalDuration > TimeSpan.Zero ? clip.OriginalDuration : TimeSpan.FromSeconds(lengthSeconds);
            int count = GetCount(length);

            for (int i = 0; i < count; i++) {
                if (token.IsCancellationRequested) {
                    Directory.Delete(part, true);
                    return false;
                }

                //the middle of each of the count equal parts, so neither the first nor the last (often black) frame is used
                TimeSpan time = TimeSpan.FromTicks(length.Ticks * (2 * i + 1) / (2 * count));

                //0 x 0 keeps the size of the video. The nearest key frame is used, because decoding up to the exact frame is very slow.
                using var frame = composition.GetThumbnailAsync(time, 0, 0, VideoFramePrecision.NearestKeyFrame).AsTask().GetAwaiter().GetResult();
                using MemoryStream memory = new MemoryStream();

                frame.AsStreamForRead().CopyTo(memory);
                memory.Position = 0;

                using Image image = Image.FromStream(memory);

                image.Save(Path.Combine(part, i + ".png"), ImageFormat.Png);
            }

            if (Directory.Exists(folder))
                Directory.Delete(folder, true);

            Directory.Move(part, folder);

            return true;
        }
    }
}
