using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using Yijian.Core;
using static Yijian.Core.Strings;

namespace Yijian.Win;

public sealed class NoteWindow : Window {
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    static readonly string[] StreamImageClipboardFormats = ["PNG", "image/png", "JFIF", "JPG", "JPEG", "image/jpeg", "GIF", "image/gif", "image/bmp", "image/webp"];

    sealed class PastedImage {
        public required byte[] Bytes { get; init; }
        public required string Extension { get; init; }
        public string? TempPreviewPath { get; set; }
    }

    readonly MainWindow owner;
    readonly List<PastedImage> images = [];
    readonly StackPanel imagesPanel;
    readonly TextBox editor;
    readonly SelectableText hintLabel;
    readonly Border outerBorder;
    bool isPasting;
    DateTime lastPasteTime = DateTime.MinValue;

    public NoteWindow(MainWindow owner, double initialLeft = double.NaN, double initialTop = double.NaN) {
        this.owner = owner;
        Icon = owner.Icon;
        MainWindow.Localize(this, TitleProperty, "小便签");
        Width = 320;
        Height = 340;
        MinWidth = 220;
        MinHeight = 220;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        FontFamily = owner.FontFamily;
        FontSize = 13;
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            GlassFrameThickness = new Thickness(0)
        });

        if (double.IsFinite(initialLeft) && double.IsFinite(initialTop)) {
            Left = Math.Clamp(initialLeft, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(initialTop, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
            WindowStartupLocation = WindowStartupLocation.Manual;
        } else {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Resources.MergedDictionaries.Add(owner.Resources);
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(ForegroundProperty, "Fg");

        var root = new Grid { Background = Brushes.Transparent };
        outerBorder = new Border {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = root
        };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "Bg");
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "Line");
        Content = outerBorder;

        outerBorder.PreviewMouseLeftButtonDown += OnDragWindow;

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(main);

        var header = new Grid { Margin = new Thickness(14, 10, 10, 6), Height = 32 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        main.Children.Add(header);

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border {
            Background = owner.B("Accent"),
            CornerRadius = new CornerRadius(6),
            Width = 22,
            Height = 22,
            Child = new TextBlock {
                Text = "✎",
                Foreground = owner.B("Bg"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var titleText = new TextBlock {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        MainWindow.Localize(titleText, TextBlock.TextProperty, "小便签");
        titleRow.Children.Add(badge);
        titleRow.Children.Add(titleText);
        header.Children.Add(titleRow);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(owner.Btn("＋", "新建便签", owner.OpenNewNote));
        actions.Children.Add(owner.Btn("🖼", "粘贴图片", () => TryPasteImage(reportWhenEmpty: true)));
        actions.Children.Add(owner.Btn("💾", "另存为", SaveNoteAs));
        actions.Children.Add(owner.Btn("✕", "关闭便签", Close));
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var bodyStack = new StackPanel { Margin = new Thickness(14, 4, 14, 8) };
        editor = new TextBox {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            MaxLength = 20000,
            Margin = new Thickness(0, 0, 0, 10)
        };
        MainWindow.Localize(editor, ToolTipProperty, "输入文字，或按 Ctrl+V 粘贴图片…");
        CommandManager.AddPreviewCanExecuteHandler(editor, OnEditorPreviewCanExecute);
        CommandManager.AddPreviewExecutedHandler(editor, OnEditorPreviewExecuted);
        DataObject.AddPastingHandler(editor, OnEditorPasting);
        AttachEditorContextMenu();
        bodyStack.Children.Add(editor);

        imagesPanel = new StackPanel();
        bodyStack.Children.Add(imagesPanel);

        var scroll = new ScrollViewer {
            Content = bodyStack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        main.Children.Add(scroll);

        hintLabel = owner.Text("双击使用系统图片查看器打开", 10, "Muted");
        hintLabel.Margin = new Thickness(14, 2, 14, 10);
        hintLabel.Visibility = Visibility.Collapsed;
        Grid.SetRow(hintLabel, 2);
        main.Children.Add(hintLabel);

        PreviewKeyDown += OnWindowPreviewKeyDown;
        SourceInitialized += (s, e) => Native.Round(this);

        Loaded += (s, e) => editor.Focus();
        Closed += (s, e) => {
            CommandManager.RemovePreviewCanExecuteHandler(editor, OnEditorPreviewCanExecute);
            CommandManager.RemovePreviewExecutedHandler(editor, OnEditorPreviewExecuted);
            DataObject.RemovePastingHandler(editor, OnEditorPasting);
            CleanupAllTempFiles();
            owner.OnNoteWindowClosed(this);
        };
    }

    void AttachEditorContextMenu() {
        var menu = new ContextMenu();
        menu.Resources.MergedDictionaries.Add(owner.Resources);
        var cutItem = new MenuItem { Icon = "✂" };
        MainWindow.Localize(cutItem, HeaderedItemsControl.HeaderProperty, "剪切");
        cutItem.Click += (s, e) => editor.Cut();
        var copyItem = new MenuItem { Icon = "⧉" };
        MainWindow.Localize(copyItem, HeaderedItemsControl.HeaderProperty, "复制");
        copyItem.Click += (s, e) => editor.Copy();
        var pasteItem = new MenuItem { Icon = "📋" };
        MainWindow.Localize(pasteItem, HeaderedItemsControl.HeaderProperty, "粘贴");
        pasteItem.Click += (s, e) => {
            if (!TryPasteImage(reportWhenEmpty: false) && Clipboard.ContainsText()) {
                editor.Paste();
            }
        };
        var pasteImgItem = new MenuItem { Icon = "🖼" };
        MainWindow.Localize(pasteImgItem, HeaderedItemsControl.HeaderProperty, "粘贴图片");
        pasteImgItem.Click += (s, e) => TryPasteImage(reportWhenEmpty: true);
        var selectAllItem = new MenuItem { Icon = "▣" };
        MainWindow.Localize(selectAllItem, HeaderedItemsControl.HeaderProperty, "全选");
        selectAllItem.Click += (s, e) => editor.SelectAll();

        menu.Opened += (s, e) => {
            cutItem.IsEnabled = editor.SelectionLength > 0;
            copyItem.IsEnabled = editor.SelectionLength > 0;
            selectAllItem.IsEnabled = !string.IsNullOrEmpty(editor.Text);
        };
        menu.Items.Add(cutItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(pasteImgItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(selectAllItem);
        editor.ContextMenu = menu;
    }

    public void RefreshTheme() {
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(owner.Resources);
        outerBorder.Background = owner.B("Bg");
        outerBorder.BorderBrush = owner.B("Line");
    }

    void OnDragWindow(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton != MouseButton.Left) return;
        var source = e.OriginalSource as DependencyObject;
        if (MainWindow.Ancestor<ButtonBase>(source) != null ||
            (MainWindow.Ancestor<TextBoxBase>(source) is TextBoxBase tb && tb == editor) ||
            MainWindow.Ancestor<ScrollBar>(source) != null ||
            MainWindow.Ancestor<Thumb>(source) != null ||
            MainWindow.Ancestor<Image>(source) != null) {
            return;
        }
        try { DragMove(); } catch (InvalidOperationException) { }
        e.Handled = true;
    }

    void OnEditorPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e) {
        if (e.Command == ApplicationCommands.Paste) {
            e.CanExecute = true;
            e.Handled = true;
        }
    }

    void OnEditorPreviewExecuted(object sender, ExecutedRoutedEventArgs e) {
        if (e.Command == ApplicationCommands.Paste) {
            if (TryPasteImage(reportWhenEmpty: false)) {
                e.Handled = true;
            }
        }
    }

    void OnEditorPasting(object sender, DataObjectPastingEventArgs e) {
        if (TryPasteImage(reportWhenEmpty: false)) {
            e.CancelCommand();
            e.Handled = true;
        }
    }

    void OnWindowPreviewKeyDown(object sender, KeyEventArgs e) {
        if (e.Handled) return;
        if (owner.MatchesNoteShortcut(e)) {
            e.Handled = true;
            owner.OpenNewNote();
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) {
            e.Handled = true;
            SaveNoteAs();
            return;
        }
        if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V) ||
            (Keyboard.Modifiers == ModifierKeys.Shift && e.Key == Key.Insert)) {
            if (TryPasteImage(reportWhenEmpty: false)) {
                e.Handled = true;
            }
        }
    }

    void SaveNoteAs() {
        var text = editor.Text ?? "";
        if (string.IsNullOrWhiteSpace(text) && images.Count == 0) {
            ShowNotice(T("便签内容为空。"));
            return;
        }

        var picker = new SaveFileDialog {
            Title = T("另存为"),
            Filter = T("Markdown 文件|*.md"),
            DefaultExt = ".md",
            FileName = $"note-{DateTime.Now:yyyyMMdd-HHmmss}.md"
        };
        if (picker.ShowDialog(this) != true || string.IsNullOrWhiteSpace(picker.FileName)) return;

        try {
            var mdPath = Path.GetFullPath(picker.FileName);
            var dir = Path.GetDirectoryName(mdPath) ?? "";
            var baseName = Path.GetFileNameWithoutExtension(mdPath);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "note";

            var sb = new StringBuilder();
            var trimmed = text.TrimEnd();
            if (trimmed.Length > 0) {
                sb.Append(trimmed);
            }

            if (images.Count > 0) {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                for (int i = 0; i < images.Count; i++) {
                    var idx = i + 1;
                    var img = images[i];
                    var imgFileName = images.Count == 1
                        ? $"{baseName}{img.Extension}"
                        : $"{baseName}-{idx}{img.Extension}";
                    var imgFullPath = Path.Combine(dir, imgFileName);
                    File.WriteAllBytes(imgFullPath, img.Bytes);

                    var linkTarget = imgFileName.IndexOfAny([' ', '(', ')']) >= 0 ? $"<{imgFileName}>" : imgFileName;
                    if (i > 0) sb.AppendLine().AppendLine();
                    sb.Append($"![image {idx}]({linkTarget})");
                }
            }

            sb.AppendLine();
            File.WriteAllText(mdPath, sb.ToString(), Encoding.UTF8);
            ShowNotice(F("已保存：{0}", Path.GetFileName(mdPath)));
        } catch (Exception ex) {
            ShowNotice(ex.Message);
        }
    }

    void UpdateHintVisibility() {
        if (imagesPanel.Children.Count > 0) {
            hintLabel.Text = T("双击使用系统图片查看器打开");
            hintLabel.Visibility = Visibility.Visible;
        } else {
            hintLabel.Visibility = Visibility.Collapsed;
        }
    }

    static BitmapImage? TryDecodeBitmapStream(Stream stream) {
        try {
            if (stream.CanSeek) stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 ? bitmap : null;
        } catch {
            return null;
        }
    }

    static BitmapSource? TryDecodeDibData(object? dibData) {
        byte[]? dibBytes = dibData switch {
            MemoryStream ms => ms.ToArray(),
            Stream s => ReadAllBytes(s),
            byte[] arr => arr,
            _ => null
        };
        if (dibBytes == null || dibBytes.Length < 40) return null;
        try {
            int headerSize = BitConverter.ToInt32(dibBytes, 0);
            if (headerSize < 40 || headerSize > dibBytes.Length) return null;
            short bpp = BitConverter.ToInt16(dibBytes, 14);
            int compression = BitConverter.ToInt32(dibBytes, 16);
            int clrUsed = BitConverter.ToInt32(dibBytes, 32);

            int colorTableSize = 0;
            if (headerSize == 40 && (compression == 3 || compression == 6)) {
                colorTableSize = compression == 6 ? 16 : 12;
            } else if (bpp <= 8 && bpp > 0) {
                int colors = clrUsed > 0 ? clrUsed : (1 << bpp);
                colorTableSize = colors * 4;
            }

            int pixelOffset = 14 + headerSize + colorTableSize;
            int fileSize = 14 + dibBytes.Length;
            using var bmpStream = new MemoryStream(fileSize);
            using var bw = new BinaryWriter(bmpStream);
            bw.Write((byte)'B');
            bw.Write((byte)'M');
            bw.Write(fileSize);
            bw.Write(0);
            bw.Write(pixelOffset);
            bw.Write(dibBytes);
            bw.Flush();
            bmpStream.Position = 0;
            return TryDecodeBitmapStream(bmpStream);
        } catch {
            return null;
        }
    }

    static byte[] ReadAllBytes(Stream stream) {
        if (stream.CanSeek) stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    static bool TryExtractImageFromHtmlClipboard(string? html, out BitmapSource? bitmap, out string? localFilePath) {
        bitmap = null;
        localFilePath = null;
        if (string.IsNullOrWhiteSpace(html)) return false;

        var match = Regex.Match(html, @"<img\b[^>]*?\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var src = match.Groups[1].Value.Trim();

        if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) {
            int commaIdx = src.IndexOf(',');
            if (commaIdx > 0 && src[..commaIdx].Contains(";base64", StringComparison.OrdinalIgnoreCase)) {
                try {
                    var bytes = Convert.FromBase64String(src[(commaIdx + 1)..].Trim());
                    using var ms = new MemoryStream(bytes);
                    bitmap = TryDecodeBitmapStream(ms);
                    return bitmap != null;
                } catch {
                    return false;
                }
            }
        }

        if (TryResolveImageFileFromText(src, out var resolved)) {
            localFilePath = resolved;
            return true;
        }
        return false;
    }

    static bool TryResolveImageFileFromText(string? rawText, out string filePath) {
        filePath = "";
        if (string.IsNullOrWhiteSpace(rawText)) return false;
        var candidate = rawText.Trim().Trim('"', '\'');
        if (candidate.Contains('\n')) {
            candidate = candidate.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim().Trim('"', '\'');
        }
        if (candidate.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(candidate, UriKind.Absolute, out var fileUri) && fileUri.IsFile) {
            candidate = fileUri.LocalPath;
        }
        if (Path.IsPathRooted(candidate) && File.Exists(candidate) &&
            ImageExtensions.Contains(Path.GetExtension(candidate))) {
            filePath = candidate;
            return true;
        }
        return false;
    }

    static IDataObject? TryGetClipboardDataObject() {
        for (int i = 0; i < 3; i++) {
            try {
                return Clipboard.GetDataObject();
            } catch (COMException) {
                Thread.Sleep(25);
            }
        }
        return null;
    }

    static BitmapSource? TryExtractBitmapFromClipboard(IDataObject? dataObject) {
        if (dataObject != null) {
            foreach (var fmt in StreamImageClipboardFormats) {
                try {
                    if (!dataObject.GetDataPresent(fmt)) continue;
                    var raw = dataObject.GetData(fmt);
                    if (raw is Stream stream) {
                        var decoded = TryDecodeBitmapStream(stream);
                        if (decoded != null) return decoded;
                    } else if (raw is byte[] bytes && bytes.Length > 0) {
                        using var ms = new MemoryStream(bytes);
                        var decoded = TryDecodeBitmapStream(ms);
                        if (decoded != null) return decoded;
                    }
                } catch {
                }
            }
        }

        try {
            if (Clipboard.ContainsImage()) {
                var img = Clipboard.GetImage();
                if (img != null && img.PixelWidth > 0 && img.PixelHeight > 0) {
                    return img;
                }
            }
        } catch {
        }

        if (dataObject != null) {
            foreach (var dibFormat in new[] { DataFormats.Dib, "Format17" }) {
                try {
                    if (!dataObject.GetDataPresent(dibFormat)) continue;
                    var dibBmp = TryDecodeDibData(dataObject.GetData(dibFormat));
                    if (dibBmp != null) return dibBmp;
                } catch {
                }
            }
        }

        return null;
    }

    public bool TryPasteImage(bool reportWhenEmpty) {
        if (isPasting) return true;
        var now = DateTime.UtcNow;
        if ((now - lastPasteTime).TotalMilliseconds < 180) return true;

        isPasting = true;
        try {
            var dataObject = TryGetClipboardDataObject();

            // 1. Check file drop list on clipboard (distinct image paths)
            if (Clipboard.ContainsFileDropList()) {
                var files = Clipboard.GetFileDropList();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pastedAny = false;
                foreach (var file in files) {
                    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file) &&
                        ImageExtensions.Contains(Path.GetExtension(file)) &&
                        seen.Add(Path.GetFullPath(file))) {
                        AddImageFromFile(file);
                        pastedAny = true;
                    }
                }
                if (pastedAny) {
                    lastPasteTime = DateTime.UtcNow;
                    return true;
                }
            }

            // 2. Check bitmap / encoded image formats on clipboard (PNG, Bitmap, DIB, etc.)
            var clipBitmap = TryExtractBitmapFromClipboard(dataObject);
            if (clipBitmap != null) {
                AddImageFromBitmap(clipBitmap);
                lastPasteTime = DateTime.UtcNow;
                return true;
            }

            // 3. Check HTML format on clipboard
            if (dataObject != null && dataObject.GetDataPresent(DataFormats.Html)) {
                var html = dataObject.GetData(DataFormats.Html) as string;
                if (TryExtractImageFromHtmlClipboard(html, out var htmlBmp, out var htmlFilePath)) {
                    if (htmlBmp != null) {
                        AddImageFromBitmap(htmlBmp);
                        lastPasteTime = DateTime.UtcNow;
                        return true;
                    }
                    if (!string.IsNullOrEmpty(htmlFilePath)) {
                        AddImageFromFile(htmlFilePath);
                        lastPasteTime = DateTime.UtcNow;
                        return true;
                    }
                }
            }

            // 4. Check if clipboard text is a direct local image file path
            if (Clipboard.ContainsText()) {
                var text = Clipboard.GetText();
                if (TryResolveImageFileFromText(text, out var pathFromText)) {
                    AddImageFromFile(pathFromText);
                    lastPasteTime = DateTime.UtcNow;
                    return true;
                }
            }
        } catch (Exception ex) {
            ShowNotice(ex.Message);
            return false;
        } finally {
            isPasting = false;
        }

        if (reportWhenEmpty) {
            ShowNotice(T("剪贴板中没有可用的图片。"));
        }
        return false;
    }

    void AddImageFromBitmap(BitmapSource bitmap) {
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(ms);
        var bytes = ms.ToArray();
        ms.Position = 0;
        var decoded = TryDecodeBitmapStream(ms) ?? bitmap;
        AddImageControl(decoded, bytes, ".png");
    }

    void AddImageFromFile(string sourcePath) {
        var bytes = File.ReadAllBytes(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        using var ms = new MemoryStream(bytes);
        var bmp = TryDecodeBitmapStream(ms) ?? throw new IOException(T("无法打开图片：") + sourcePath);
        AddImageControl(bmp, bytes, ext);
    }

    void AddImageControl(BitmapSource bitmap, byte[] bytes, string extension) {
        var entry = new PastedImage {
            Bytes = bytes,
            Extension = extension
        };
        images.Add(entry);

        var image = new Image {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            MaxHeight = 260,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var container = new Border {
            Background = owner.B("Card"),
            BorderBrush = owner.B("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = Cursors.Hand,
            Child = image
        };
        MainWindow.Localize(container, ToolTipProperty, "双击使用系统图片查看器打开");

        void OpenImage() {
            try {
                if (string.IsNullOrEmpty(entry.TempPreviewPath) || !File.Exists(entry.TempPreviewPath)) {
                    var tempDir = Path.Combine(Path.GetTempPath(), "YijianTodo-Preview");
                    Directory.CreateDirectory(tempDir);
                    var tempPath = Path.Combine(tempDir, $"preview-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}{entry.Extension}");
                    File.WriteAllBytes(tempPath, entry.Bytes);
                    entry.TempPreviewPath = tempPath;
                }
                Process.Start(new ProcessStartInfo(entry.TempPreviewPath) { UseShellExecute = true });
            } catch (Exception ex) {
                ShowNotice(T("无法打开图片：") + ex.Message);
            }
        }

        void RemoveImage() {
            imagesPanel.Children.Remove(container);
            images.Remove(entry);
            CleanupTempFile(entry.TempPreviewPath);
            entry.TempPreviewPath = null;
            UpdateHintVisibility();
        }

        container.MouseLeftButtonDown += (s, e) => {
            if (e.ClickCount == 2) {
                OpenImage();
                e.Handled = true;
            }
        };

        var menu = new ContextMenu();
        menu.Resources.MergedDictionaries.Add(owner.Resources);
        var openItem = new MenuItem { Header = T("打开图片"), Icon = "↗" };
        openItem.Click += (s, e) => OpenImage();
        var deleteItem = new MenuItem { Header = T("删除图片"), Icon = "×", Style = (Style)owner.FindResource("DeleteMenuItem") };
        deleteItem.Click += (s, e) => RemoveImage();
        menu.Items.Add(openItem);
        menu.Items.Add(deleteItem);
        container.ContextMenu = menu;

        imagesPanel.Children.Add(container);
        UpdateHintVisibility();
    }

    static void CleanupTempFile(string? path) {
        if (string.IsNullOrEmpty(path)) return;
        try {
            if (File.Exists(path)) File.Delete(path);
        } catch {
        }
    }

    void CleanupAllTempFiles() {
        foreach (var img in images) {
            CleanupTempFile(img.TempPreviewPath);
            img.TempPreviewPath = null;
        }
    }

    void ShowNotice(string message) {
        hintLabel.Text = message;
        hintLabel.Visibility = Visibility.Visible;
    }
}
