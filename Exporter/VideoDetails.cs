using MetadataExtractor;

namespace Exporter {
    //resolution and recording date of a video, read from the header (mp4/mov) without reading the whole video
    public class VideoDetails {
        public int? Width;
        public int? Height;
        public DateTime? TakenAt;

        public static VideoDetails Read(string shiftedFile) {
            VideoDetails details = new VideoDetails();

            try {
                using ShiftedStream stream = new ShiftedStream(shiftedFile);

                foreach (MetadataExtractor.Directory directory in ImageMetadataReader.ReadMetadata(stream)) {
                    foreach (Tag tag in directory.Tags) {
                        int number;
                        DateTime date;

                        //a movie has a track header for every track, only the video track has a size
                        if (directory.Name.Contains("Track Header") && tag.Name == "Width" && details.Width == null && directory.TryGetInt32(tag.Type, out number) && number > 0)
                            details.Width = number;
                        else if (directory.Name.Contains("Track Header") && tag.Name == "Height" && details.Height == null && directory.TryGetInt32(tag.Type, out number) && number > 0)
                            details.Height = number;
                        else if (directory.Name.Contains("Movie Header") && tag.Name == "Created" && directory.TryGetDateTime(tag.Type, out date) && date.Year > 1904)
                            details.TakenAt = date;
                    }
                }
            } catch (Exception e) {
                Console.WriteLine(e.Message);
            }

            return details;
        }
    }
}
