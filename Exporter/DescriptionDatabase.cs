using Microsoft.Data.Sqlite;
using Categories = Exporter.DescriptionCategorizer.Categories;

namespace Exporter {
    //Stores the categorized descriptions in a SQLite database next to the images,
    //so even with very many images it can be searched quickly for persons and tags.
    public static class DescriptionDatabase {
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

        //images that are already in the database are replaced
        public static void Write(string file, DescriptionCategorizer categorizer, IEnumerable<(int Number, string FileName)> images) {
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder {
                DataSource = file,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };

            using SqliteConnection connection = new SqliteConnection(builder.ToString());
            connection.Open();

            execute(connection, schema);

            using SqliteTransaction transaction = connection.BeginTransaction();

            SqliteCommand clear = command(connection, transaction,
                "DELETE FROM image_persons WHERE image = $image; DELETE FROM image_tags WHERE image = $image; DELETE FROM free_texts WHERE image = $image;",
                "$image");
            SqliteCommand addImage = command(connection, transaction,
                "INSERT INTO images (number, file) VALUES ($image, $file) ON CONFLICT(number) DO UPDATE SET file = excluded.file;",
                "$image", "$file");
            SqliteCommand addPerson = command(connection, transaction,
                "INSERT OR IGNORE INTO persons (name) VALUES ($name); INSERT OR IGNORE INTO image_persons (image, person) SELECT $image, id FROM persons WHERE name = $name;",
                "$image", "$name");
            SqliteCommand addTag = command(connection, transaction,
                "INSERT OR IGNORE INTO tags (name) VALUES ($name); INSERT OR IGNORE INTO image_tags (image, tag) SELECT $image, id FROM tags WHERE name = $name;",
                "$image", "$name");
            SqliteCommand addFreeText = command(connection, transaction,
                "INSERT INTO free_texts (image, text) VALUES ($image, $name);",
                "$image", "$name");

            foreach ((int number, string fileName) in images) {
                Categories categories = categorizer.Categorize(number);

                run(clear, number);
                run(addImage, number, fileName);

                foreach (string person in categories.Persons)
                    run(addPerson, number, person);
                foreach (string tag in categories.Tags)
                    run(addTag, number, tag);
                foreach (string freeText in categories.FreeTexts)
                    run(addFreeText, number, freeText);
            }

            transaction.Commit();
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
