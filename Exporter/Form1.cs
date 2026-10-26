using StoredImage = Exporter.SchuleImageStore.StoredImage;
using StoredVideo = Exporter.SchuleVideoStore.StoredVideo;

namespace Exporter {
    public partial class Form1 : Form {
        private const int WM_DEVICECHANGE = 0x0219;

        private class DriveItem {
            public readonly DriveInfo Drive;

            public DriveItem(DriveInfo drive) {
                Drive = drive;
            }

            public override string ToString() {
                string label = string.IsNullOrWhiteSpace(Drive.VolumeLabel) ? Drive.DriveType.ToString() : Drive.VolumeLabel;

                return Drive.Name + "  " + label + "  (" + formatSize(Drive.AvailableFreeSpace) + " frei)";
            }
        }

        private readonly RadioButton imagesRadio = new RadioButton { Text = "Bilder", AutoSize = true, Checked = true };
        private readonly RadioButton videosRadio = new RadioButton { Text = "Videos", AutoSize = true };

        private readonly Label sourceLabel = label("Bilder-Ordner (SchuleApi):");
        private readonly TextBox sourceBox = new TextBox { Dock = DockStyle.Fill };
        private readonly Button browseSourceButton = new Button { Text = "...", AutoSize = true };
        private readonly Button loadButton = new Button { Text = "Einlesen", AutoSize = true };

        private readonly TextBox filterBox = new TextBox { Dock = DockStyle.Fill, Enabled = false };
        private readonly Label countLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "noch nicht eingelesen" };

        private readonly ComboBox driveBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button refreshDrivesButton = new Button { Text = "Aktualisieren", AutoSize = true };
        private readonly CheckBox allDrivesBox = new CheckBox { Text = "alle Laufwerke", AutoSize = true, Anchor = AnchorStyles.Left };

        private readonly TextBox folderBox = new TextBox { Dock = DockStyle.Fill };

        private readonly TextBox sameTagsBox = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox personsBox = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox tagsBox = new TextBox { Dock = DockStyle.Fill };
        private readonly List<Button> fileButtons = new List<Button>();

        private readonly Button updateButton = new Button { Text = "Nur neue kopieren", AutoSize = true, Enabled = false };
        private readonly Button copyButton = new Button { Text = "Alle neu kopieren", AutoSize = true, Enabled = false };
        private readonly Button cancelButton = new Button { Text = "Abbrechen", AutoSize = true, Enabled = false };
        private readonly ProgressBar progressBar = new ProgressBar { Dock = DockStyle.Fill };

        private readonly Label statusLabel = new Label { AutoSize = true, Dock = DockStyle.Fill };

        private SchuleImageStore? imageStore;
        private SchuleVideoStore? videoStore;
        private List<StoredImage> images = new List<StoredImage>();
        private List<StoredVideo> videos = new List<StoredVideo>();
        private CancellationTokenSource? cancel;
        private bool busy;

        //source folder and folder on the destination of the mode that is not shown at the moment
        private string otherSource = "";
        private string otherFolder = "";

        //drive of the last run, selected again as soon as it is plugged in
        private string? savedDrive;

        private bool videoMode => videosRadio.Checked;
        private string kind => videoMode ? "Videos" : "Bilder";

        public Form1() {
            InitializeComponent();

            Text = "Bilder und Videos auf Datenträger kopieren";
            Width = 720;
            Height = 440;
            MinimumSize = new Size(600, 420);

            TableLayoutPanel layout = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 4,
                RowCount = 11
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            FlowLayoutPanel modePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            modePanel.Controls.Add(imagesRadio);
            modePanel.Controls.Add(videosRadio);

            layout.Controls.Add(label("Kopieren:"), 0, 0);
            layout.Controls.Add(modePanel, 1, 0);
            layout.SetColumnSpan(modePanel, 3);

            layout.Controls.Add(sourceLabel, 0, 1);
            layout.Controls.Add(sourceBox, 1, 1);
            layout.Controls.Add(browseSourceButton, 2, 1);
            layout.Controls.Add(loadButton, 3, 1);

            layout.Controls.Add(label("Suche in Beschreibung:"), 0, 2);
            layout.Controls.Add(filterBox, 1, 2);
            layout.Controls.Add(countLabel, 2, 2);
            layout.SetColumnSpan(countLabel, 2);

            layout.Controls.Add(label("Ziel-Laufwerk:"), 0, 3);
            layout.Controls.Add(driveBox, 1, 3);
            layout.Controls.Add(refreshDrivesButton, 2, 3);
            layout.Controls.Add(allDrivesBox, 3, 3);

            layout.Controls.Add(label("Ordner auf dem Ziel:"), 0, 4);
            layout.Controls.Add(folderBox, 1, 4);
            layout.Controls.Add(updateButton, 2, 4);
            layout.Controls.Add(copyButton, 3, 4);

            addFileRow(layout, 5, "Gleiche Tags:", sameTagsBox);
            addFileRow(layout, 6, "Personen:", personsBox);
            addFileRow(layout, 7, "Tags:", tagsBox);

            layout.Controls.Add(progressBar, 0, 8);
            layout.SetColumnSpan(progressBar, 3);
            layout.Controls.Add(cancelButton, 3, 8);

            layout.Controls.Add(statusLabel, 0, 9);
            layout.SetColumnSpan(statusLabel, 4);

            Controls.Add(layout);

            loadSettings();

            videosRadio.CheckedChanged += (s, e) => switchMode();
            browseSourceButton.Click += (s, e) => browseSource();
            loadButton.Click += async (s, e) => await load();
            filterBox.TextChanged += (s, e) => applyFilter();
            refreshDrivesButton.Click += (s, e) => refreshDrives();
            allDrivesBox.CheckedChanged += (s, e) => refreshDrives();
            //only new files are copied, the database is updated for all of them
            updateButton.Click += async (s, e) => await copy(false);
            copyButton.Click += async (s, e) => await copy(true);
            cancelButton.Click += (s, e) => cancel?.Cancel();

            showMode();
            refreshDrives();
        }

        private void loadSettings() {
            Settings settings = Settings.Load();

            string imageSource = string.IsNullOrWhiteSpace(settings.Source)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hiddenStorerFolder")
                : settings.Source;
            //SchuleApi keeps the videos in E:\hiddenStorerFolder (SchuleApi.getVideosApi)
            string videoSource = string.IsNullOrWhiteSpace(settings.VideoSource) ? "E:\\hiddenStorerFolder" : settings.VideoSource;
            string imageFolder = string.IsNullOrWhiteSpace(settings.Folder) ? "Bilder" : settings.Folder;
            string videoFolder = string.IsNullOrWhiteSpace(settings.VideoFolder) ? "Videos" : settings.VideoFolder;

            videosRadio.Checked = settings.Mode == "Videos";
            imagesRadio.Checked = !videosRadio.Checked;

            sourceBox.Text = videoMode ? videoSource : imageSource;
            folderBox.Text = videoMode ? videoFolder : imageFolder;
            otherSource = videoMode ? imageSource : videoSource;
            otherFolder = videoMode ? imageFolder : videoFolder;

            sameTagsBox.Text = settings.SameTags ?? "";
            personsBox.Text = settings.Persons ?? "";
            tagsBox.Text = settings.Tags ?? "";

            savedDrive = settings.Drive;
            allDrivesBox.Checked = settings.AllDrives;
        }

        private void saveSettings() {
            string? drive = (driveBox.SelectedItem as DriveItem)?.Drive.Name ?? savedDrive;

            new Settings {
                Mode = kind,
                Source = videoMode ? otherSource : sourceBox.Text.Trim(),
                VideoSource = videoMode ? sourceBox.Text.Trim() : otherSource,
                Drive = drive,
                AllDrives = allDrivesBox.Checked,
                Folder = videoMode ? otherFolder : folderBox.Text.Trim(),
                VideoFolder = videoMode ? folderBox.Text.Trim() : otherFolder,
                SameTags = sameTagsBox.Text.Trim(),
                Persons = personsBox.Text.Trim(),
                Tags = tagsBox.Text.Trim()
            }.Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            saveSettings();

            base.OnFormClosing(e);
        }

        //the source and destination folder are swapped with the ones of the other mode
        private void switchMode() {
            string source = sourceBox.Text;
            string folder = folderBox.Text;

            sourceBox.Text = otherSource;
            folderBox.Text = otherFolder;
            otherSource = source;
            otherFolder = folder;

            showMode();
        }

        private void showMode() {
            sourceLabel.Text = videoMode ? "Video-Ordner (SchuleApi):" : "Bilder-Ordner (SchuleApi):";

            applyFilter();

            if ((videoMode ? (object?)videoStore : imageStore) == null)
                statusLabel.Text = kind + " noch nicht eingelesen.";
        }

        private void addFileRow(TableLayoutPanel layout, int row, string text, TextBox box) {
            Button browse = new Button { Text = "...", AutoSize = true };

            browse.Click += (s, e) => {
                using OpenFileDialog dialog = new OpenFileDialog {
                    Title = text.TrimEnd(':'),
                    Filter = "Textdateien|*.txt;*.csv|Alle Dateien|*.*"
                };

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    box.Text = dialog.FileName;
            };

            fileButtons.Add(browse);

            layout.Controls.Add(label(text), 0, row);
            layout.Controls.Add(box, 1, row);
            layout.Controls.Add(browse, 2, row);
        }

        private static Label label(string text) {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };
        }

        //a usb stick was plugged in or removed
        protected override void WndProc(ref Message m) {
            base.WndProc(ref m);

            if (m.Msg == WM_DEVICECHANGE && cancel == null)
                refreshDrives();
        }

        private void browseSource() {
            using FolderBrowserDialog dialog = new FolderBrowserDialog {
                Description = videoMode ? "Den Ordner wählen, der \"videoFolder\" enthält" : "Den Ordner wählen, der \"imageFolder\" enthält",
                SelectedPath = sourceBox.Text
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
                sourceBox.Text = dialog.SelectedPath;
        }

        //reads the images or videos with their descriptions like SchuleApi does
        private async Task load() {
            string root = sourceBox.Text.Trim();
            bool loadVideos = videoMode;

            //SchuleApi would create an empty folder in a wrong folder, so check it first
            if (loadVideos ? !SchuleVideoStore.IsVideoStore(root) : !SchuleImageStore.IsImageStore(root)) {
                MessageBox.Show(this, "In diesem Ordner gibt es keinen Unterordner \"" + (loadVideos ? "videoFolder" : "imageFolder") + "\".", kind + " einlesen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            setBusy(true);
            statusLabel.Text = kind + " werden eingelesen...";

            try {
                if (loadVideos)
                    videoStore = await Task.Run(() => SchuleVideoStore.Load(root));
                else
                    imageStore = await Task.Run(() => SchuleImageStore.Load(root));

                statusLabel.Text = (loadVideos ? videoStore!.Count : imageStore!.Count) + " " + (loadVideos ? "Videos" : "Bilder") + " eingelesen.";
            } catch (Exception e) {
                statusLabel.Text = "Fehler beim Einlesen: " + e.Message;
            } finally {
                setBusy(false);
            }
        }

        private void applyFilter() {
            string filter = filterBox.Text.Trim();
            int shown = 0;
            int total = 0;
            bool loaded;

            if (videoMode) {
                loaded = videoStore != null;

                if (videoStore != null) {
                    videos = videoStore.GetVideos(filter);
                    shown = videos.Count;
                    total = videoStore.Count;
                }
            } else {
                loaded = imageStore != null;

                if (imageStore != null) {
                    images = imageStore.GetImages(filter);
                    shown = images.Count;
                    total = imageStore.Count;
                }
            }

            countLabel.Text = loaded ? shown + " von " + total + " " + kind : "noch nicht eingelesen";
            filterBox.Enabled = loaded && !busy;
            updateButton.Enabled = shown > 0 && !busy;
            copyButton.Enabled = shown > 0 && !busy;
        }

        private void refreshDrives() {
            string? selected = (driveBox.SelectedItem as DriveItem)?.Drive.Name ?? savedDrive;

            driveBox.Items.Clear();

            foreach (DriveInfo drive in DriveInfo.GetDrives()) {
                try {
                    if (!drive.IsReady)
                        continue;

                    if (!allDrivesBox.Checked && drive.DriveType != DriveType.Removable)
                        continue;

                    driveBox.Items.Add(new DriveItem(drive));
                } catch (IOException) {
                    //the drive was removed while reading it
                }
            }

            foreach (DriveItem item in driveBox.Items) {
                if (item.Drive.Name == selected)
                    driveBox.SelectedItem = item;
            }

            if (driveBox.SelectedItem == null && driveBox.Items.Count > 0)
                driveBox.SelectedIndex = 0;

            if (driveBox.Items.Count == 0)
                statusLabel.Text = allDrivesBox.Checked ? "Kein Laufwerk gefunden." : "Kein USB-Stick gefunden. Stick einstecken oder \"alle Laufwerke\" anhaken.";
        }

        private async Task copy(bool overwrite) {
            bool copyVideos = videoMode;

            if (copyVideos ? videoStore == null || videos.Count == 0 : imageStore == null || images.Count == 0)
                return;

            if (driveBox.SelectedItem is not DriveItem item) {
                MessageBox.Show(this, "Bitte ein Ziel-Laufwerk wählen.", "Kopieren", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DriveInfo drive = item.Drive;
            string destination = Path.Combine(drive.RootDirectory.FullName, folderBox.Text.Trim());

            //the files are copied from the SchuleApi folder, never into it
            if (Path.GetFullPath(destination).StartsWith(Path.GetFullPath(sourceBox.Text.Trim()), StringComparison.OrdinalIgnoreCase)) {
                MessageBox.Show(this, "Das Ziel liegt im SchuleApi-Ordner selbst.", "Kopieren", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            //the database is next to the folder, e.g. E:\bilder.db for E:\Bilder and E:\Videos
            string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(destination)) ?? destination;
            string database = Path.Combine(parent, "bilder.db");

            List<StoredImage> imagesToCopy = images.ToList();
            List<StoredVideo> videosToCopy = videos.ToList();

            long needed = copyVideos ? VideoCopier.GetSize(videosToCopy, destination, overwrite) : ImageCopier.GetSize(imagesToCopy, destination, overwrite);
            long free;

            try {
                free = drive.AvailableFreeSpace;
            } catch (IOException) {
                statusLabel.Text = "Das Laufwerk ist nicht mehr erreichbar.";
                refreshDrives();
                return;
            }

            if (needed > free) {
                DialogResult answer = MessageBox.Show(this,
                    "Die " + kind + " brauchen " + formatSize(needed) + ", auf " + drive.Name + " sind nur " + formatSize(free) + " frei.\n\nTrotzdem so viele wie möglich kopieren?",
                    "Zu wenig Platz", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                    return;
            }

            string[] files = { sameTagsBox.Text.Trim(), personsBox.Text.Trim(), tagsBox.Text.Trim() };

            foreach (string file in files) {
                if (file.Length > 0 && !File.Exists(file)) {
                    MessageBox.Show(this, "Die Datei gibt es nicht:\n" + file, "Beschreibungen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            DescriptionCategorizer categorizer;

            try {
                //the descriptions are the ones saved in SchuleApi
                IEnumerable<(int, string)> descriptions = copyVideos
                    ? videoStore!.GetVideos("").Select(video => (video.Number, video.Description))
                    : imageStore!.GetImages("").Select(image => (image.Number, image.Description));

                categorizer = DescriptionCategorizer.Load(files[0], files[1], files[2], descriptions);
            } catch (Exception e) {
                MessageBox.Show(this, "Die Listen konnten nicht eingelesen werden:\n" + e.Message, "Beschreibungen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            saveSettings();

            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;

            setBusy(true);

            string phase = "Kopiere " + kind;
            Progress<int> progress = new Progress<int>(done => {
                progressBar.Value = Math.Min(done, progressBar.Maximum);
                statusLabel.Text = phase + ": " + done + " von " + progressBar.Maximum + " (" + done * 100 / Math.Max(1, progressBar.Maximum) + " %)";
            });

            try {
                progressBar.Maximum = Math.Max(1, copyVideos ? videosToCopy.Count : imagesToCopy.Count);
                progressBar.Value = 0;

                string text;
                bool cancelled;
                List<int> copiedNumbers;
                Func<Task> writeDatabase;

                if (copyVideos) {
                    //the previews are next to the video folder, e.g. E:\Previews\0\0.png for E:\Videos
                    string previews = Path.Combine(parent, "Previews");
                    VideoCopier.Result result = await Task.Run(() => VideoCopier.Copy(videosToCopy, destination, previews, overwrite, progress, token));

                    text = summary(result.Copied, result.Skipped, result.Failed, result.FirstError, result.Cancelled);

                    if (result.Renamed > 0)
                        text += " " + result.Renamed + " vorhandene umbenannt.";
                    if (result.PreviewsFailed > 0)
                        text += " Vorschaubilder für " + result.PreviewsFailed + " Videos fehlgeschlagen (" + result.FirstPreviewError + ").";
                    cancelled = result.Cancelled;
                    copiedNumbers = result.Files.Select(copied => copied.Video.Number).ToList();
                    writeDatabase = () => Task.Run(() => ImageDatabase.WriteVideos(database, categorizer, result.Files, progress, token));
                } else {
                    ImageCopier.Result result = await Task.Run(() => ImageCopier.Copy(imagesToCopy, destination, overwrite, progress, token));

                    text = summary(result.Copied, result.Skipped, result.Failed, result.FirstError, result.Cancelled);
                    cancelled = result.Cancelled;
                    copiedNumbers = result.Files.Select(copied => copied.Image.Number).ToList();
                    writeDatabase = () => Task.Run(() => ImageDatabase.Write(database, categorizer, result.Files, progress, token));
                }

                if (!cancelled) {
                    try {
                        phase = "Aktualisiere Datenbank";
                        progressBar.Maximum = Math.Max(1, copiedNumbers.Count);
                        progressBar.Value = 0;

                        await writeDatabase();

                        string currentKind = kind;
                        string debugFile = Path.Combine(parent, "beschreibungen_debug_" + currentKind.ToLower() + ".txt");

                        try {
                            await Task.Run(() => categorizer.WriteDebugFile(debugFile, currentKind, copiedNumbers));
                        } catch (Exception e) {
                            Console.WriteLine(e.Message);
                        }

                        text += token.IsCancellationRequested ? " Datenbank nur teilweise aktualisiert (abgebrochen)." : " Datenbank aktualisiert.";
                    } catch (Exception e) {
                        text += " Datenbank konnte nicht gespeichert werden: " + e.Message;
                    }
                }

                statusLabel.Text = text;
            } catch (Exception e) {
                statusLabel.Text = "Fehler beim Kopieren: " + e.Message;
            } finally {
                cancel.Dispose();
                cancel = null;

                setBusy(false);
                refreshDrives();
            }
        }

        private string summary(int copied, int skipped, int failed, string? firstError, bool cancelled) {
            string text = kind + ": " + copied + " kopiert";

            if (skipped > 0)
                text += ", " + skipped + " übersprungen (schon vorhanden)";
            if (failed > 0)
                text += ", " + failed + " fehlgeschlagen (" + firstError + ")";

            return text + (cancelled ? ". Abgebrochen." : ".");
        }

        //while loading or copying only cancelling is possible
        private void setBusy(bool value) {
            busy = value;

            imagesRadio.Enabled = !busy;
            videosRadio.Enabled = !busy;
            sourceBox.Enabled = !busy;
            loadButton.Enabled = !busy;
            browseSourceButton.Enabled = !busy;
            driveBox.Enabled = !busy;
            refreshDrivesButton.Enabled = !busy;
            allDrivesBox.Enabled = !busy;
            folderBox.Enabled = !busy;
            sameTagsBox.Enabled = !busy;
            personsBox.Enabled = !busy;
            tagsBox.Enabled = !busy;
            foreach (Button button in fileButtons)
                button.Enabled = !busy;
            cancelButton.Enabled = busy && cancel != null;

            applyFilter();
        }

        private static string formatSize(long bytes) {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;

            while (size >= 1024 && unit < units.Length - 1) {
                size /= 1024;
                unit++;
            }

            return size.ToString("0.#") + " " + units[unit];
        }
    }
}
