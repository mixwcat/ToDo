using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Yijian.Core;
using Yijian.Ubuntu.Platform;
using static Yijian.Core.Strings;

namespace Yijian.Ubuntu;

public sealed class NoteWindow : Window {
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    sealed class PastedImage {
        public required byte[] Bytes { get; init; }
        public required string Extension { get; init; }
        public string? TempPreviewPath { get; set; }
    }

    readonly MainWindow owner;
    readonly List<PastedImage> images = [];
    readonly StackPanel imagesPanel;
    readonly TextBox editor;
    readonly SelectableTextBlock hintLabel;
    readonly Border outerBorder;
    bool isPasting;
    DateTime lastPasteTime = DateTime.MinValue;

    public NoteWindow(MainWindow owner, double initialLeft = double.NaN, double initialTop = double.NaN) {
        this.owner = owner;
        Width = 320;
        Height = 340;
        MinWidth = 220;
        MinHeight = 220;
        SystemDecorations = SystemDecorations.None;
        CanResize = true;
        WindowStartupLocation = double.IsFinite(initialLeft) && double.IsFinite(initialTop)
            ? WindowStartupLocation.Manual
            : WindowStartupLocation.CenterScreen;
        if (double.IsFinite(initialLeft) && double.IsFinite(initialTop)) {
            Position = new PixelPoint((int)initialLeft, (int)initialTop);
        }
        Icon = owner.Icon;
        MainWindow.Localize(this, TitleProperty, "小便签");

        var root = new Grid { Background = Brushes.Transparent };
        outerBorder = new Border {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = root
        };
        Content = outerBorder;

        // Window dragging on empty area / header
        outerBorder.PointerPressed += (s, e) => {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var visual = e.Source as Visual;
            if (visual != null && visual.GetSelfAndVisualAncestors().Any(x => x is Button or TextBox or ScrollBar or Image)) return;
            BeginMoveDrag(e);
            e.Handled = true;
        };

        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(main);

        // Header bar (draggable)
        var header = new Grid {
            Background = Brushes.Transparent,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(14, 10, 10, 6),
            Height = 32
        };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border {
            CornerRadius = new CornerRadius(6),
            Width = 22,
            Height = 22,
            Child = new TextBlock {
                Text = "✎",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var titleText = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        MainWindow.Localize(titleText, TextBlock.TextProperty, "小便签");
        titleRow.Children.Add(badge);
        titleRow.Children.Add(titleText);
        MainWindow.Place(header, titleRow);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        actions.Children.Add(owner.Btn("＋", "新建便签", () => owner.OpenNewNote()));
        actions.Children.Add(owner.Btn("🖼", "粘贴图片", () => _ = TryPasteImageAsync(reportWhenEmpty: true)));
        actions.Children.Add(owner.Btn("💾", "另存为", () => _ = SaveNoteAsAsync()));
        actions.Children.Add(owner.Btn("✕", "关闭便签", Close));
        MainWindow.Place(header, actions, 0, 1);
        MainWindow.Place(main, header, 0);

        // Body: text input + pasted images inside a scrollable area
        var bodyStack = new StackPanel { Spacing = 10, Margin = new Thickness(14, 4, 14, 8) };
        editor = new TextBox {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            MaxLength = 20000
        };
        MainWindow.Localize(editor, TextBox.WatermarkProperty, "输入文字，或按 Ctrl+V 粘贴图片…");
        MainWindow.Localize(editor, AutomationProperties.NameProperty, "小便签");
        editor.PastingFromClipboard += OnEditorPastingFromClipboard;
        AttachEditorContextMenu();
        bodyStack.Children.Add(editor);

        imagesPanel = new StackPanel { Spacing = 10 };
        bodyStack.Children.Add(imagesPanel);

        var scroll = new ScrollViewer {
            Content = bodyStack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        MainWindow.Place(main, scroll, 1);

        // Footer hint / status
        hintLabel = owner.Label("双击使用系统图片查看器打开", 10, "Muted", false);
        hintLabel.Margin = new Thickness(14, 2, 14, 10);
        MainWindow.Place(main, hintLabel, 2);

        // Single tunnel KeyDown handler on the window to avoid duplicate triggers
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        ApplyTheme();

        Opened += (s, e) => editor.Focus();
        Closed += (s, e) => {
            CleanupAllTempFiles();
            owner.OnNoteWindowClosed(this);
        };
    }

    void AttachEditorContextMenu() {
        editor.ContextMenu = new ContextMenu();
        editor.ContextRequested += (s, e) => {
            var menu = owner.PopupMenu();
            var hasSelection = editor.SelectionStart != editor.SelectionEnd;
            var cut = new MenuItem { Header = T("剪切"), IsEnabled = hasSelection };
            cut.Click += (s2, e2) => editor.Cut();
            var copy = new MenuItem { Header = T("复制"), IsEnabled = hasSelection };
            copy.Click += (s2, e2) => editor.Copy();
            var paste = new MenuItem { Header = T("粘贴") };
            paste.Click += async (s2, e2) => {
                if (await TryPasteImageAsync(reportWhenEmpty: false)) return;
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null) return;
                var text = await ClipboardExtensions.TryGetTextAsync(clipboard);
                if (!string.IsNullOrEmpty(text)) InsertTextAtCaret(text);
            };
            var pasteImg = new MenuItem { Header = T("粘贴图片") };
            pasteImg.Click += async (s2, e2) => await TryPasteImageAsync(reportWhenEmpty: true);
            var selectAll = new MenuItem { Header = T("全选"), IsEnabled = !string.IsNullOrEmpty(editor.Text) };
            selectAll.Click += (s2, e2) => editor.SelectAll();

            menu.Items.Add(cut);
            menu.Items.Add(copy);
            menu.Items.Add(paste);
            menu.Items.Add(pasteImg);
            menu.Items.Add(new Separator());
            menu.Items.Add(selectAll);
            editor.ContextMenu = menu;
        };
    }

    public void ApplyTheme() {
        RequestedThemeVariant = owner.RequestedThemeVariant;
        foreach (var (name, brush) in owner.Palette) {
            Resources[name] = brush;
        }
        Background = owner.B("Bg");
        Foreground = owner.B("Fg");
        outerBorder.Background = owner.B("Bg");
        outerBorder.BorderBrush = owner.B("Line");
    }

    async void OnEditorPastingFromClipboard(object? sender, RoutedEventArgs e) {
        e.Handled = true;
        if (await TryPasteImageAsync(reportWhenEmpty: false)) {
            return;
        }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        var text = await ClipboardExtensions.TryGetTextAsync(clipboard);
        if (!string.IsNullOrEmpty(text)) {
            InsertTextAtCaret(text);
        }
    }

    void InsertTextAtCaret(string pasteText) {
        var current = editor.Text ?? "";
        var selStart = Math.Clamp(editor.SelectionStart, 0, current.Length);
        var selEnd = Math.Clamp(editor.SelectionEnd, 0, current.Length);
        var start = Math.Min(selStart, selEnd);
        var end = Math.Max(selStart, selEnd);
        editor.Text = current[..start] + pasteText + current[end..];
        editor.CaretIndex = start + pasteText.Length;
        editor.SelectionStart = editor.CaretIndex;
        editor.SelectionEnd = editor.CaretIndex;
    }

    async void OnWindowKeyDown(object? sender, KeyEventArgs e) {
        if (e.Handled) return;
        if (owner.MatchesNoteShortcut(e)) {
            e.Handled = true;
            owner.OpenNewNote();
            return;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S) {
            e.Handled = true;
            await SaveNoteAsAsync();
            return;
        }
        // When editor is NOT focused, handle Ctrl+V / Shift+Insert at window level
        if (!editor.IsFocused &&
            ((e.KeyModifiers == KeyModifiers.Control && e.Key == Key.V) ||
             (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.Insert))) {
            e.Handled = true;
            await TryPasteImageAsync(reportWhenEmpty: false);
        }
    }

    async Task SaveNoteAsAsync() {
        var text = editor.Text ?? "";
        if (string.IsNullOrWhiteSpace(text) && images.Count == 0) {
            ShowNotice(T("便签内容为空。"));
            return;
        }

        try {
            var mdType = new FilePickerFileType(T("Markdown 文件")) {
                Patterns = ["*.md"],
                MimeTypes = ["text/markdown"]
            };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = T("另存为"),
                SuggestedFileName = $"note-{DateTime.Now:yyyyMMdd-HHmmss}.md",
                DefaultExtension = "md",
                FileTypeChoices = [mdType]
            });
            if (file == null) return;

            var localPath = file.TryGetLocalPath();
            var baseName = !string.IsNullOrWhiteSpace(localPath)
                ? Path.GetFileNameWithoutExtension(localPath)
                : Path.GetFileNameWithoutExtension(file.Name);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "note";

            var mdContent = await BuildMarkdownAndExportImagesAsync(text, baseName, !string.IsNullOrWhiteSpace(localPath) ? Path.GetDirectoryName(localPath) : null);
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            var outBytes = Encoding.UTF8.GetBytes(mdContent);
            await stream.WriteAsync(outBytes);
            ShowNotice(F("已保存：{0}", file.Name));
        } catch (Exception ex) {
            ShowNotice(ex.Message);
        }
    }

    internal async Task<string> BuildMarkdownAndExportImagesAsync(string text, string baseName, string? imageOutputDir) {
        var sb = new StringBuilder();
        var trimmed = text.TrimEnd();
        if (trimmed.Length > 0) {
            sb.Append(trimmed);
        }

        if (images.Count > 0 && !string.IsNullOrWhiteSpace(imageOutputDir)) {
            Directory.CreateDirectory(imageOutputDir);
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            for (int i = 0; i < images.Count; i++) {
                var idx = i + 1;
                var img = images[i];
                var imgFileName = images.Count == 1
                    ? $"{baseName}{img.Extension}"
                    : $"{baseName}-{idx}{img.Extension}";
                var imgFullPath = Path.Combine(imageOutputDir, imgFileName);
                await File.WriteAllBytesAsync(imgFullPath, img.Bytes);

                var linkTarget = imgFileName.IndexOfAny([' ', '(', ')']) >= 0 ? $"<{imgFileName}>" : imgFileName;
                if (i > 0) sb.AppendLine().AppendLine();
                sb.Append($"![image {idx}]({linkTarget})");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }

    void UpdateHintVisibility() {
        if (imagesPanel.Children.Count > 0) {
            hintLabel.Text = T("双击使用系统图片查看器打开");
            hintLabel.IsVisible = true;
        } else {
            hintLabel.IsVisible = false;
        }
    }

    static bool TryExtractImageFromHtmlClipboard(string? html, out Bitmap? bitmap, out string? localFilePath) {
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
                    bitmap = new Bitmap(ms);
                    return bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0;
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

    static List<string> ExtractImagePathsFromUriListText(string? raw) {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (line.StartsWith('#') || line.Equals("copy", StringComparison.OrdinalIgnoreCase) || line.Equals("cut", StringComparison.OrdinalIgnoreCase)) continue;
            if (TryResolveImageFileFromText(line, out var resolved) && seen.Add(Path.GetFullPath(resolved))) {
                result.Add(resolved);
            }
        }
        return result;
    }

    public async Task<bool> TryPasteImageAsync(bool reportWhenEmpty) {
        if (isPasting) return true;
        var now = DateTime.UtcNow;
        if ((now - lastPasteTime).TotalMilliseconds < 180) return true;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return false;

        isPasting = true;
        try {
            // 1. Try pasted image files from clipboard (take only distinct local paths)
            var files = await ClipboardExtensions.TryGetFilesAsync(clipboard);
            if (files != null) {
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pastedAny = false;
                foreach (var item in files) {
                    var localPath = item.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath) &&
                        ImageExtensions.Contains(Path.GetExtension(localPath)) &&
                        seenPaths.Add(Path.GetFullPath(localPath))) {
                        AddImageFromFile(localPath);
                        pastedAny = true;
                    }
                }
                if (pastedAny) {
                    lastPasteTime = DateTime.UtcNow;
                    return true;
                }
            }

            // 2. Try Avalonia standard bitmap clipboard extension
            try {
                var bitmap = await ClipboardExtensions.TryGetBitmapAsync(clipboard);
                if (bitmap != null) {
                    AddImageFromBitmap(bitmap);
                    lastPasteTime = DateTime.UtcNow;
                    return true;
                }
            } catch {
                // Fall through to raw clipboard formats
            }

            // 3. Try raw image & file-list formats from clipboard
#pragma warning disable CS0618
            var formats = await clipboard.GetFormatsAsync();
            if (formats != null) {
                foreach (var format in formats) {
                    if (format.Contains("gnome-copied-files", StringComparison.OrdinalIgnoreCase) ||
                        format.Contains("mate-copied-files", StringComparison.OrdinalIgnoreCase) ||
                        format.Contains("kde-cutselection", StringComparison.OrdinalIgnoreCase) ||
                        format.Equals("text/uri-list", StringComparison.OrdinalIgnoreCase)) {
                        try {
                            var data = await clipboard.GetDataAsync(format);
                            string? uriText = data switch {
                                byte[] b when b.Length > 0 => Encoding.UTF8.GetString(b),
                                string s => s,
                                _ => null
                            };
                            var paths = ExtractImagePathsFromUriListText(uriText);
                            if (paths.Count > 0) {
                                foreach (var p in paths) {
                                    AddImageFromFile(p);
                                }
                                lastPasteTime = DateTime.UtcNow;
                                return true;
                            }
                        } catch { }
                    }
                }

                foreach (var format in formats) {
                    if (format.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                        format.Equals("PNG", StringComparison.OrdinalIgnoreCase) ||
                        format.Equals("BITMAP", StringComparison.OrdinalIgnoreCase) ||
                        format.Equals("CF_DIB", StringComparison.OrdinalIgnoreCase)) {
                        try {
                            var data = await clipboard.GetDataAsync(format);
                            if (data is byte[] bytes && bytes.Length > 0) {
                                using var ms = new MemoryStream(bytes);
                                var bmp = new Bitmap(ms);
                                AddImageFromBitmap(bmp);
                                lastPasteTime = DateTime.UtcNow;
                                return true;
                            }
                            if (data is Stream stream) {
                                using var ms = new MemoryStream();
                                await stream.CopyToAsync(ms);
                                if (ms.Length > 0) {
                                    ms.Position = 0;
                                    var bmp = new Bitmap(ms);
                                    AddImageFromBitmap(bmp);
                                    lastPasteTime = DateTime.UtcNow;
                                    return true;
                                }
                            }
                        } catch { }
                    }
                }

                foreach (var format in formats) {
                    if (format.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
                        format.Equals("HTML Format", StringComparison.OrdinalIgnoreCase)) {
                        try {
                            var data = await clipboard.GetDataAsync(format);
                            string? html = data switch {
                                byte[] b when b.Length > 0 => Encoding.UTF8.GetString(b),
                                string s => s,
                                _ => null
                            };
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
                        } catch { }
                    }
                }
            }
#pragma warning restore CS0618

            // 4. Check if clipboard text is a direct local image file path or file:// URI list
            var text = await ClipboardExtensions.TryGetTextAsync(clipboard);
            if (!string.IsNullOrWhiteSpace(text)) {
                var uriPaths = ExtractImagePathsFromUriListText(text);
                if (uriPaths.Count > 0) {
                    foreach (var p in uriPaths) {
                        AddImageFromFile(p);
                    }
                    lastPasteTime = DateTime.UtcNow;
                    return true;
                }
            }

            // 5. Fallback for Linux Wayland / X11 clipboard tools (wl-paste / xclip)
            var cliImageBytes = await DesktopTools.TryReadLinuxClipboardImageBytesAsync();
            if (cliImageBytes is { Length: > 0 }) {
                using var ms = new MemoryStream(cliImageBytes);
                var bmp = new Bitmap(ms);
                AddImageFromBitmap(bmp);
                lastPasteTime = DateTime.UtcNow;
                return true;
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

    void AddImageFromBitmap(Bitmap bitmap) {
        using var ms = new MemoryStream();
        bitmap.Save(ms);
        AddImageControl(bitmap, ms.ToArray(), ".png");
    }

    void AddImageFromFile(string sourcePath) {
        var bytes = File.ReadAllBytes(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        using var ms = new MemoryStream(bytes);
        var bmp = new Bitmap(ms);
        AddImageControl(bmp, bytes, ext);
    }

    void AddImageControl(Bitmap bitmap, byte[] bytes, string extension) {
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
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = image
        };
        MainWindow.Localize(container, ToolTip.TipProperty, "双击使用系统图片查看器打开");

        void OpenImage() {
            try {
                if (string.IsNullOrEmpty(entry.TempPreviewPath) || !File.Exists(entry.TempPreviewPath)) {
                    var tempDir = Path.Combine(Path.GetTempPath(), "YijianTodo-Preview");
                    Directory.CreateDirectory(tempDir);
                    var tempPath = Path.Combine(tempDir, $"preview-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}{entry.Extension}");
                    File.WriteAllBytes(tempPath, entry.Bytes);
                    entry.TempPreviewPath = tempPath;
                }
                DesktopTools.OpenWithDefaultViewer(entry.TempPreviewPath);
            } catch (Exception ex) {
                ShowNotice(ex.Message);
            }
        }

        void RemoveImage() {
            imagesPanel.Children.Remove(container);
            images.Remove(entry);
            CleanupTempFile(entry.TempPreviewPath);
            entry.TempPreviewPath = null;
            UpdateHintVisibility();
        }

        container.DoubleTapped += (s, e) => {
            OpenImage();
            e.Handled = true;
        };

        var menu = owner.PopupMenu();
        var openItem = new MenuItem { Header = T("打开图片") };
        openItem.Click += (s, e) => OpenImage();
        var deleteItem = new MenuItem { Header = T("删除图片") };
        deleteItem.Classes.Add("danger");
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
        hintLabel.IsVisible = true;
    }
}
