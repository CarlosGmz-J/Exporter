using StoredImage = Exporter.SchuleImageStore.StoredImage;

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

        private readonly TextBox sourceBox = new TextBox { Dock = DockStyle.Fill };
        private readonly Button browseSourceButton = new Button { Text = "...", AutoSize = true };
        private readonly Button loadButton = new Button { Text = "Einlesen", AutoSize = true };

        private readonly TextBox filterBox = new TextBox { Dock = DockStyle.Fill, Enabled = false };
        private readonly Label countLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "noch nicht eingelesen" };

        private readonly ComboBox driveBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button refreshDrivesButton = new Button { Text = "Aktualisieren", AutoSize = true };
        private readonly CheckBox allDrivesBox = new CheckBox { Text = "alle Laufwerke", AutoSize = true, Anchor = AnchorStyles.Left };

        private readonly TextBox folderBox = new TextBox { Dock = DockStyle.Fill, Text = "Bilder" };
        private readonly CheckBox overwriteBox = new CheckBox { Text = "vorhandene überschreiben", AutoSize = true, Anchor = AnchorStyles.Left };

        private readonly TextBox descriptionsBox = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox sameTagsBox = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox personsBox = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox tagsBox = new TextBox { Dock = DockStyle.Fill };
        private readonly List<Button> fileButtons = new List<Button>();

        private readonly Button copyButton = new Button { Text = "Kopieren", AutoSize = true, Enabled = false };
        private readonly Button cancelButton = new Button { Text = "Abbrechen", AutoSize = true, Enabled = false };
        private readonly ProgressBar progressBar = new ProgressBar { Dock = DockStyle.Fill };

        private readonly Label statusLabel = new Label { AutoSize = true, Dock = DockStyle.Fill };

        private SchuleImageStore? store;
        private List<StoredImage> images = new List<StoredImage>();
        private CancellationTokenSource? cancel;

        public Form1() {
            InitializeComponent();

            Text = "Bilder auf Datenträger kopieren";
            Width = 720;
            Height = 420;
            MinimumSize = new Size(600, 400);

            sourceBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hiddenStorerFolder");

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

            layout.Controls.Add(label("Bilder-Ordner (SchuleApi):"), 0, 0);
            layout.Controls.Add(sourceBox, 1, 0);
            layout.Controls.Add(browseSourceButton, 2, 0);
            layout.Controls.Add(loadButton, 3, 0);

            layout.Controls.Add(label("Suche in Beschreibung:"), 0, 1);
            layout.Controls.Add(filterBox, 1, 1);
            layout.Controls.Add(countLabel, 2, 1);
            layout.SetColumnSpan(countLabel, 2);

            layout.Controls.Add(label("Ziel-Laufwerk:"), 0, 2);
            layout.Controls.Add(driveBox, 1, 2);
            layout.Controls.Add(refreshDrivesButton, 2, 2);
            layout.Controls.Add(allDrivesBox, 3, 2);

            layout.Controls.Add(label("Ordner auf dem Ziel:"), 0, 3);
            layout.Controls.Add(folderBox, 1, 3);
            layout.Controls.Add(overwriteBox, 2, 3);
            layout.SetColumnSpan(overwriteBox, 2);

            addFileRow(layout, 4, "Beschreibungen (optional):", descriptionsBox);
            addFileRow(layout, 5, "Gleiche Tags:", sameTagsBox);
            addFileRow(layout, 6, "Personen:", personsBox);
            addFileRow(layout, 7, "Tags:", tagsBox);

            layout.Controls.Add(progressBar, 0, 8);
            layout.SetColumnSpan(progressBar, 2);
            layout.Controls.Add(copyButton, 2, 8);
            layout.Controls.Add(cancelButton, 3, 8);

            layout.Controls.Add(statusLabel, 0, 9);
            layout.SetColumnSpan(statusLabel, 4);

            Controls.Add(layout);

            browseSourceButton.Click += (s, e) => browseSource();
            loadButton.Click += async (s, e) => await loadImages();
            filterBox.TextChanged += (s, e) => applyFilter();
            refreshDrivesButton.Click += (s, e) => refreshDrives();
            allDrivesBox.CheckedChanged += (s, e) => refreshDrives();
            copyButton.Click += async (s, e) => await copyImages();
            cancelButton.Click += (s, e) => cancel?.Cancel();

            refreshDrives();
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
                Description = "Den Ordner wählen, der \"imageFolder\" enthält",
                SelectedPath = sourceBox.Text
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
                sourceBox.Text = dialog.SelectedPath;
        }

        private async Task loadImages() {
            string root = sourceBox.Text.Trim();

                        if (!SchuleImageStore.IsImageStore(root)) {
                MessageBox.Show(this, "In diesem Ordner gibt es keinen Unterordner \"imageFolder\".", "Bilder einlesen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            loadButton.Enabled = false;
            copyButton.Enabled = false;
            statusLabel.Text = "Bilder werden eingelesen...";

            try {
                store = await Task.Run(() => SchuleImageStore.Load(root));

                filterBox.Enabled = true;
                applyFilter();

                statusLabel.Text = store.Count + " Bilder eingelesen.";
            } catch (Exception e) {
                statusLabel.Text = "Fehler beim Einlesen: " + e.Message;
            } finally {
                loadButton.Enabled = true;
            }
        }

        private void applyFilter() {
            if (store == null)
                return;

            images = store.GetImages(filterBox.Text.Trim());

            countLabel.Text = images.Count + " von " + store.Count + " Bildern";
            copyButton.Enabled = images.Count > 0;
        }

        private void refreshDrives() {
            string? selected = (driveBox.SelectedItem as DriveItem)?.Drive.Name;

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

        private async Task copyImages() {
            if (store == null || images.Count == 0)
                return;

            if (driveBox.SelectedItem is not DriveItem item) {
                MessageBox.Show(this, "Bitte ein Ziel-Laufwerk wählen.", "Kopieren", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DriveInfo drive = item.Drive;
            string destination = Path.Combine(drive.RootDirectory.FullName, folderBox.Text.Trim());

            //images are copied from the same SchuleApi folder, never into it
            if (Path.GetFullPath(destination).StartsWith(Path.GetFullPath(sourceBox.Text.Trim()), StringComparison.OrdinalIgnoreCase)) {
                MessageBox.Show(this, "Das Ziel liegt im Bilder-Ordner selbst.", "Kopieren", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<StoredImage> toCopy = images.ToList();
            bool overwrite = overwriteBox.Checked;

            long needed = ImageCopier.GetSize(toCopy);
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
                    "Die Bilder brauchen " + formatSize(needed) + ", auf " + drive.Name + " sind nur " + formatSize(free) + " frei.\n\nTrotzdem so viele wie möglich kopieren?",
                    "Zu wenig Platz", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                    return;
            }

            DescriptionCategorizer? categorizer = null;
            string descriptionsFile = descriptionsBox.Text.Trim();

            if (descriptionsFile.Length > 0) {
                string[] files = { descriptionsFile, sameTagsBox.Text.Trim(), personsBox.Text.Trim(), tagsBox.Text.Trim() };

                foreach (string file in files) {
                    if (file.Length > 0 && !File.Exists(file)) {
                        MessageBox.Show(this, "Die Datei gibt es nicht:\n" + file, "Beschreibungen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                try {
                    categorizer = DescriptionCategorizer.Load(files[0], files[1], files[2], files[3]);
                } catch (Exception e) {
                    MessageBox.Show(this, "Die Beschreibungen konnten nicht eingelesen werden:\n" + e.Message, "Beschreibungen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            setCopying(true);
            progressBar.Maximum = toCopy.Count;
            progressBar.Value = 0;
            statusLabel.Text = "Kopiere " + toCopy.Count + " Bilder nach " + destination + " ...";

            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            Progress<int> progress = new Progress<int>(done => progressBar.Value = done);

            try {
                ImageCopier.Result result = await Task.Run(() => ImageCopier.Copy(toCopy, destination, overwrite, progress, token));

                string text = result.Copied + " kopiert";
                if (result.Skipped > 0)
                    text += ", " + result.Skipped + " übersprungen (schon vorhanden)";
                if (result.Failed > 0)
                    text += ", " + result.Failed + " fehlgeschlagen (" + result.FirstError + ")";
                if (result.Cancelled)
                    text += ". Abgebrochen.";
                else
                    text += ".";

                if (categorizer != null) {
                    try {
                        string csv = Path.Combine(destination, "beschreibungen.csv");

                        await Task.Run(() => categorizer.WriteCsv(csv, toCopy.Select(image => image.Number)));

                        text += " Beschreibungen in beschreibungen.csv gespeichert.";
                    } catch (Exception e) {
                        text += " Beschreibungen konnten nicht gespeichert werden: " + e.Message;
                    }
                }

                statusLabel.Text = text;
            } catch (Exception e) {
                statusLabel.Text = "Fehler beim Kopieren: " + e.Message;
            } finally {
                cancel.Dispose();
                cancel = null;

                setCopying(false);
                refreshDrives();
            }
        }

        private void setCopying(bool copying) {
            loadButton.Enabled = !copying;
            browseSourceButton.Enabled = !copying;
            filterBox.Enabled = !copying;
            driveBox.Enabled = !copying;
            refreshDrivesButton.Enabled = !copying;
            allDrivesBox.Enabled = !copying;
            folderBox.Enabled = !copying;
            overwriteBox.Enabled = !copying;
            descriptionsBox.Enabled = !copying;
            sameTagsBox.Enabled = !copying;
            personsBox.Enabled = !copying;
            tagsBox.Enabled = !copying;
            foreach (Button button in fileButtons)
                button.Enabled = !copying;
            copyButton.Enabled = !copying;
            cancelButton.Enabled = copying;
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
