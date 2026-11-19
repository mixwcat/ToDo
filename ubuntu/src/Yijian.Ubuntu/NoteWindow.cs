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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yijian.Core;
using Yijian.Ubuntu.Platform;
using static Yijian.Core.Strings;

namespace Yijian.Ubuntu;

public sealed class NoteWindow : Window {
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    readonly MainWindow owner;
    readonly StickyNote note;
    readonly StackPanel imagesPanel;
    readonly TextBox editor;
    readonly SelectableTextBlock hintLabel;
    readonly Border outerBorder;
    bool initialized;
    bool isPasting;
    DateTime lastPasteTime = DateTime.MinValue;

    public StickyNote Note => note;

    public NoteWindow(MainWindow owner, StickyNote note) {
        this.owner = owner;
        this.note = note;
        Width = double.IsFinite(note.Width) && note.Width >= 220 ? Math.Clamp(note.Width, 220, 900) : 320;
        Height = double.IsFinite(note.Height) && note.Height >= 220 ? Math.Clamp(note.Height, 220, 900) : 340;
        MinWidth = 220;
        MinHeight = 220;
        SystemDecorations = SystemDecorations.None;
        CanResize = true;
        WindowStartupLocation = double.IsFinite(note.Left) && double.IsFinite(note.Top)
            ? WindowStartupLocation.Manual
            : WindowStartupLocation.CenterScreen;
        if (double.IsFinite(note.Left) && double.IsFinite(note.Top)) {
            Position = new PixelPoint((int)note.Left, (int)note.Top);
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
        actions.Children.Add(owner.Btn("🗑", "删除便签", () => owner.DeleteNote(this)));
        actions.Children.Add(owner.Btn("✕", "关闭便签", Close));
        MainWindow.Place(header, actions, 0, 1);
        MainWindow.Place(main, header, 0);

        // Body: text input + pasted images inside a scrollable area
        var bodyStack = new StackPanel { Spacing = 10, Margin = new Thickness(14, 4, 14, 8) };
        editor = new TextBox {
            Text = note.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            MaxLength = 20000
        };
        MainWindow.Localize(editor, TextBox.WatermarkProperty, "输入文字，或按 Ctrl+V 粘贴图片…");
        MainWindow.Localize(editor, AutomationProperties.NameProperty, "小便签");
        editor.TextChanged += (s, e) => {
            if (!initialized) return;
            note.Text = editor.Text ?? "";
            owner.NotifyNoteChanged();
        };
        editor.PastingFromClipboard += OnEditorPastingFromClipboard;
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
        hintLabel = owner.Label("双击使用系统图片查看器打开", 10, "Muted");
        hintLabel.Margin = new Thickness(14, 2, 14, 10);
        MainWindow.Place(main, hintLabel, 2);

        // Single tunnel KeyDown handler on the window to avoid duplicate triggers
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        ApplyTheme();
        LoadExistingImages();

        Opened += (s, e) => {
            initialized = true;
            note.Width = Width;
            note.Height = Height;
            note.Left = Position.X;
            note.Top = Position.Y;
            editor.Focus();
        };
        PositionChanged += (s, e) => {
            if (!initialized || WindowState != WindowState.Normal) return;
            note.Left = Position.X;
            note.Top = Position.Y;
            owner.NotifyNoteChanged();
        };
        SizeChanged += (s, e) => {
            if (!initialized || WindowState != WindowState.Normal) return;
            note.Width = Width;
            note.Height = Height;
            owner.NotifyNoteChanged();
        };
        Closed += (s, e) => owner.OnNoteWindowClosed(this);
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
        // Intercept TextBox paste so we handle image or text cleanly in a single place
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
        // When editor is NOT focused, handle Ctrl+V / Shift+Insert at window level
        if (!editor.IsFocused &&
            ((e.KeyModifiers == KeyModifiers.Control && e.Key == Key.V) ||
             (e.KeyModifiers == KeyModifiers.Shift && e.Key == Key.Insert))) {
            e.Handled = true;
            await TryPasteImageAsync(reportWhenEmpty: false);
        }
    }

    void LoadExistingImages() {
        imagesPanel.Children.Clear();
        foreach (var path in note.Images.ToList()) {
            var resolved = owner.ResolveNoteImagePath(path);
            if (File.Exists(resolved)) {
                try {
                    using var stream = File.OpenRead(resolved);
                    var bitmap = new Bitmap(stream);
                    AddImageControl(bitmap, resolved, saveToList: false);
                } catch (Exception) {
                    // Ignore corrupted image file on load
                }
            }
        }
        UpdateHintVisibility();
    }

    void UpdateHintVisibility() {
        hintLabel.IsVisible = imagesPanel.Children.Count > 0;
    }

    static bool TryResolveImageFileFromText(string? rawText, out string filePath) {
        filePath = "";
        if (string.IsNullOrWhiteSpace(rawText)) return false;
        var candidate = rawText.Trim().Trim('"', '\'');
        if (candidate.Contains('\n')) {
            candidate = candidate.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
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
                        var savedPath = CopyImageFileToConfiguredDir(localPath);
                        using var stream = File.OpenRead(savedPath);
                        var bmp = new Bitmap(stream);
                        AddImageControl(bmp, savedPath, saveToList: true);
                        pastedAny = true;
                    }
                }
                if (pastedAny) {
                    lastPasteTime = DateTime.UtcNow;
                    return true;
                }
            }

            // 2. Check if clipboard text is a direct local image file path
            var text = await ClipboardExtensions.TryGetTextAsync(clipboard);
            if (TryResolveImageFileFromText(text, out var pathFromText)) {
                var savedPath = CopyImageFileToConfiguredDir(pathFromText);
                using var stream = File.OpenRead(savedPath);
                var bmp = new Bitmap(stream);
                AddImageControl(bmp, savedPath, saveToList: true);
                lastPasteTime = DateTime.UtcNow;
                return true;
            }

            // 3. Try Avalonia standard bitmap clipboard extension
            var bitmap = await ClipboardExtensions.TryGetBitmapAsync(clipboard);
            if (bitmap != null) {
                var savedPath = SaveBitmapToConfiguredDir(bitmap);
                AddImageControl(bitmap, savedPath, saveToList: true);
                lastPasteTime = DateTime.UtcNow;
                return true;
            }

            // 4. Try raw image formats from clipboard (image/png, image/jpeg, image/bmp, etc.)
#pragma warning disable CS0618
            var formats = await clipboard.GetFormatsAsync();
            if (formats != null) {
                foreach (var format in formats) {
                    if (format.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                        format.Equals("PNG", StringComparison.OrdinalIgnoreCase) ||
                        format.Equals("BITMAP", StringComparison.OrdinalIgnoreCase)) {
                        var data = await clipboard.GetDataAsync(format);
                        if (data is byte[] bytes && bytes.Length > 0) {
                            using var ms = new MemoryStream(bytes);
                            var bmp = new Bitmap(ms);
                            var savedPath = SaveBitmapToConfiguredDir(bmp);
                            AddImageControl(bmp, savedPath, saveToList: true);
                            lastPasteTime = DateTime.UtcNow;
                            return true;
                        }
                        if (data is Stream stream) {
                            using var ms = new MemoryStream();
                            await stream.CopyToAsync(ms);
                            var raw = ms.ToArray();
                            if (raw.Length > 0) {
                                ms.Position = 0;
                                var bmp = new Bitmap(ms);
                                var savedPath = SaveBitmapToConfiguredDir(bmp);
                                AddImageControl(bmp, savedPath, saveToList: true);
                                lastPasteTime = DateTime.UtcNow;
                                return true;
                            }
                        }
                    }
                }
            }
#pragma warning restore CS0618
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

    string SaveBitmapToConfiguredDir(Bitmap bitmap) {
        var dir = owner.GetNoteImageDirectory();
        Directory.CreateDirectory(dir);
        var fileName = $"note-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}.png";
        var fullPath = Path.Combine(dir, fileName);
        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        bitmap.Save(fs);
        return fullPath;
    }

    string CopyImageFileToConfiguredDir(string sourcePath) {
        var dir = owner.GetNoteImageDirectory();
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var fileName = $"note-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        File.Copy(sourcePath, fullPath, overwrite: true);
        return fullPath;
    }

    void AddImageControl(Bitmap bitmap, string filePath, bool saveToList) {
        if (saveToList && note.Images.Contains(filePath)) return;

        if (saveToList) {
            note.Images.Add(filePath);
            owner.NotifyNoteChanged();
        }

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
                var resolved = owner.ResolveNoteImagePath(filePath);
                if (!File.Exists(resolved)) {
                    Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
                    using var fs = new FileStream(resolved, FileMode.Create, FileAccess.Write, FileShare.Read);
                    bitmap.Save(fs);
                }
                DesktopTools.OpenWithDefaultViewer(resolved);
            } catch (Exception ex) {
                ShowNotice(ex.Message);
            }
        }

        void RemoveImage() {
            imagesPanel.Children.Remove(container);
            note.Images.Remove(filePath);
            owner.NotifyNoteChanged();
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

    void ShowNotice(string message) {
        hintLabel.Text = message;
        hintLabel.IsVisible = true;
    }
}
