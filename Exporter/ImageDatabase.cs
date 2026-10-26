using Microsoft.Data.Sqlite;
using Categories = Exporter.DescriptionCategorizer.Categories;
using StoredImage = Exporter.SchuleImageStore.StoredImage;

namespace Exporter {
    //Stores the copied images with their categorized descriptions and details in a SQLite database
    //next to the images, so even with very many images it can be searched quickly.
    public static class ImageDatabase {
        private const string schema = @"
            CREATE TABLE IF NOT EXISTS images (number INTEGER PRIMARY KEY, file TEXT);
            CREATE TABLE IF NOT EXISTS persons (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS tags (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS image_persons (image INTEGER NOT NULL REFERENCES images(number), person INTEGER NOT NULL REFERENCES persons(id), PRIMARY KEY (image, person));
            CREATE TABLE IF NOT EXISTS image_tags (image INTEGER NOT NULL REFERENCES images(number), tag INTEGER NOT NULL REFERENCES tags(id), PRIMARY KEY (image, tag));
            CREATE TABLE IF NOT EXISTS free_texts (image INTEGER NOT NULL REFERENCES images(number), text TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS image_persons_person ON image_persons(person);
            CREATE INDEX IF NOT EXISTS image_tags_tag ON image_tags(tag);
            CREATE INDEX IF NOT EXISTS free_texts_image ON free_texts(image);";

        //One row per image with all persons, tags and free texts joined by commas, for looking at the data.
        //The data itself is in the link tables, every image can have any number of persons, tags and free texts.
        private const string overview = @"
            DROP VIEW IF EXISTS image_overview;
            CREATE VIEW image_overview AS
            SELECT i.number, i.file,
                (SELECT group_concat(p.name, ', ') FROM image_persons ip JOIN persons p ON p.id = ip.person WHERE ip.image = i.number) AS persons,
                (SELECT group_concat(t.name, ', ') FROM image_tags it JOIN tags t ON t.id = it.tag WHERE it.image = i.number) AS tags,
                (SELECT group_concat(f.text, ', ') FROM free_texts f WHERE f.image = i.number) AS free_texts,
                i.stored_at, i.changed_at, i.size, i.width, i.height, i.format, i.taken_at,
                i.camera_make, i.camera_model, i.orientation, i.latitude, i.longitude, i.altitude
            FROM images i;";

        //columns that were added later, databases from older versions get them added
        private static readonly (string Name, string Type)[] imageColumns = {
            ("stored_at", "TEXT"),
            ("changed_at", "TEXT"),
            ("size", "INTEGER"),
            ("width", "INTEGER"),
            ("height", "INTEGER"),
            ("format", "TEXT"),
            ("taken_at", "TEXT"),
            ("camera_make", "TEXT"),
            ("camera_model", "TEXT"),
            ("orientation", "TEXT"),
            ("latitude", "REAL"),
            ("longitude", "REAL"),
            ("altitude", "REAL")
        };

        private const string dateFormat = "yyyy-MM-dd HH:mm:ss";

        //Writes every image: the descriptions (when a categorizer is given), size, resolution and
        //metadata (date taken, camera, orientation, gps) are always read again and replaced.
        public static void Write(string file, DescriptionCategorizer? categorizer, IReadOnlyList<(StoredImage Image, string FileName)> images, IProgress<int> progress, CancellationToken token) {
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder {
                DataSource = file,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };

            using SqliteConnection connection = new SqliteConnection(builder.ToString());
            connection.Open();

            execute(connection, schema);
            addMissingColumns(connection);
            execute(connection, overview);

            using SqliteTransaction transaction = connection.BeginTransaction();

            SqliteCommand clearDescription = command(connection, transaction,
                "DELETE FROM image_persons WHERE image = $image; DELETE FROM image_tags WHERE image = $image; DELETE FROM free_texts WHERE image = $image;",
                "$image");
            SqliteCommand addImage = command(connection, transaction,
                "INSERT INTO images (number, file, stored_at) VALUES ($image, $file, $stored) ON CONFLICT(number) DO UPDATE SET file = excluded.file, stored_at = excluded.stored_at;",
                "$image", "$file", "$stored");
            SqliteCommand setDetails = command(connection, transaction,
                "UPDATE images SET changed_at = $changed, size = $size, width = $width, height = $height, format = $format, taken_at = $taken, " +
                "camera_make = $make, camera_model = $model, orientation = $orientation, latitude = $latitude, longitude = $longitude, altitude = $altitude WHERE number = $image;",
                "$image", "$changed", "$size", "$width", "$height", "$format", "$taken", "$make", "$model", "$orientation", "$latitude", "$longitude", "$altitude");
            SqliteCommand addPerson = command(connection, transaction,
                "INSERT OR IGNORE INTO persons (name) VALUES ($name); INSERT OR IGNORE INTO image_persons (image, person) SELECT $image, id FROM persons WHERE name = $name;",
                "$image", "$name");
            SqliteCommand addTag = command(connection, transaction,
                "INSERT OR IGNORE INTO tags (name) VALUES ($name); INSERT OR IGNORE INTO image_tags (image, tag) SELECT $image, id FROM tags WHERE name = $name;",
                "$image", "$name");
            SqliteCommand addFreeText = command(connection, transaction,
                "INSERT INTO free_texts (image, text) VALUES ($image, $name);",
                "$image", "$name");

            for (int i = 0; i < images.Count; i++) {
                if (token.IsCancellationRequested)
                    break;

                (StoredImage image, string fileName) = images[i];
                int number = image.Number;

                //the folder of the image is created when it is saved in the hiddenStorerFolder (like Image.getAge in SchuleApi)
                string storedAt = System.IO.Directory.GetCreationTime(Path.GetDirectoryName(image.FilePath)!).ToString(dateFormat);
                string changedAt = File.GetLastWriteTime(image.FilePath).ToString(dateFormat);

                run(addImage, number, fileName, storedAt);

                byte[]? bytes = SchuleImageStore.ReadFileByteShift(image.FilePath);

                if (bytes != null) {
                    ImageDetails details = ImageDetails.Read(bytes);

                    run(setDetails, number, changedAt, details.Size, orNull(details.Width), orNull(details.Height), details.Format,
                        orNull(details.TakenAt?.ToString(dateFormat)), orNull(details.CameraMake), orNull(details.CameraModel), orNull(details.Orientation),
                        orNull(details.Latitude), orNull(details.Longitude), orNull(details.Altitude));
                }

                if (categorizer != null) {
                    Categories categories = categorizer.Categorize(number);

                    run(clearDescription, number);

                    foreach (string person in categories.Persons)
                        run(addPerson, number, person);
                    foreach (string tag in categories.Tags)
                        run(addTag, number, tag);
                    foreach (string freeText in categories.FreeTexts)
                        run(addFreeText, number, freeText);
                }

                progress.Report(i + 1);
            }

            transaction.Commit();
        }

        private static object orNull(object? value) {
            return value ?? DBNull.Value;
        }

        private static void addMissingColumns(SqliteConnection connection) {
            HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (SqliteCommand info = connection.CreateCommand()) {
                info.CommandText = "PRAGMA table_info(images);";

                using SqliteDataReader reader = info.ExecuteReader();

                while (reader.Read())
                    existing.Add(reader.GetString(1));
            }

            foreach ((string name, string type) in imageColumns) {
                if (!existing.Contains(name))
                    execute(connection, "ALTER TABLE images ADD COLUMN " + name + " " + type + ";");
            }
        }

        private static void execute(SqliteConnection connection, string sql) {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static SqliteCommand command(SqliteConnection connection, SqliteTransaction transaction, string sql, params string[] parameters) {
            SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (string parameter in parameters)
                command.Parameters.Add(new SqliteParameter { ParameterName = parameter });

            return command;
        }

        private static void run(SqliteCommand command, params object[] values) {
            for (int i = 0; i < values.Length; i++)
                command.Parameters[i].Value = values[i];

            command.ExecuteNonQuery();
        }
    }
}
