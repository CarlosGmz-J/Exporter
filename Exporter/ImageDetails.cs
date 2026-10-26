using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Exporter {
    //size, resolution and the most useful metadata of an image, everything that is missing stays null
    public class ImageDetails {
        public long Size;
        public int? Width;
        public int? Height;
        public string Format = "";

        public DateTime? TakenAt;
        public string? CameraMake;
        public string? CameraModel;
        public string? Orientation;
        public double? Latitude;
        public double? Longitude;
        public double? Altitude;

        public static ImageDetails Read(byte[] bytes) {
            ImageDetails details = new ImageDetails {
                Size = bytes.Length,
                Format = ImageCopier.GetExtension(bytes).TrimStart('.')
            };

            //only the header is read, the pixels are not decoded
            try {
                using MemoryStream stream = new MemoryStream(bytes);
                using System.Drawing.Image image = System.Drawing.Image.FromStream(stream, false, false);

                details.Width = image.Width;
                details.Height = image.Height;
            } catch (Exception e) {
                Console.WriteLine(e.Message);
            }

            try {
                using MemoryStream stream = new MemoryStream(bytes);

                details.readMetadata(ImageMetadataReader.ReadMetadata(stream));
            } catch (Exception e) {
                Console.WriteLine(e.Message);
            }

            return details;
        }

        private void readMetadata(IReadOnlyList<MetadataExtractor.Directory> directories) {
            ExifIfd0Directory? ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            ExifSubIfdDirectory? subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            GpsDirectory? gps = directories.OfType<GpsDirectory>().FirstOrDefault();

            if (ifd0 != null) {
                CameraMake = text(ifd0.GetDescription(ExifDirectoryBase.TagMake));
                CameraModel = text(ifd0.GetDescription(ExifDirectoryBase.TagModel));
                Orientation = text(ifd0.GetDescription(ExifDirectoryBase.TagOrientation));
            }

            DateTime taken;

            if (subIfd != null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out taken))
                TakenAt = taken;
            else if (ifd0 != null && ifd0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out taken))
                TakenAt = taken;

            if (gps != null) {
                GeoLocation? location;

                if (gps.TryGetGeoLocation(out location) && location != null) {
                    Latitude = location.Value.Latitude;
                    Longitude = location.Value.Longitude;
                }

                Rational altitude;

                if (gps.TryGetRational(GpsDirectory.TagAltitude, out altitude)) {
                    int reference;

                    //1 means below sea level
                    bool below = gps.TryGetInt32(GpsDirectory.TagAltitudeRef, out reference) && reference == 1;

                    Altitude = below ? -altitude.ToDouble() : altitude.ToDouble();
                }
            }
        }

        private static string? text(string? value) {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
