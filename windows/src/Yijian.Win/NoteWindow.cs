using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Yijian.Core;
using static Yijian.Core.Strings;

namespace Yijian.Win;

public sealed class NoteWindow : Window {
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    readonly MainWindow owner;
    readonly StickyNote note;
    readonly StackPanel imagesPanel;
    readonly TextBox editor;
    readonly SelectableText hintLabel;
    readonly Border outerBorder;
    bool initialized;
    bool isPasting;
    DateTime lastPasteTime = DateTime.MinValue;

    public StickyNote Note => note;

    public NoteWindow(MainWindow owner, StickyNote note) {
        this.owner = owner;
        this.note = note;
        Icon = owner.Icon;
        MainWindow.Localize(this, TitleProperty, "小便签");
        Width = double.IsFinite(note.Width) && note.Width >= 220 ? Math.Clamp(note.Width, 220, 900) : 320;
        Height = double.IsFinite(note.Height) && note.Height >= 220 ? Math.Clamp(note.Height, 220, 900) : 340;
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

        if (double.IsFinite(note.Left) && double.IsFinite(note.Top)) {
            Left = Math.Clamp(note.Left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(note.Top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
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
        actions.Children.Add(owner.Btn("🗑", "删除便签", () => owner.DeleteNote(this)));
        actions.Children.Add(owner.Btn("✕", "关闭便签", Close));
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var bodyStack = new StackPanel { Margin = new Thickness(14, 4, 14, 8) };
        editor = new TextBox {
            Text = note.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            MaxLength = 20000,
            Margin = new Thickness(0, 0, 0, 10)
        };
        MainWindow.Localize(editor, ToolTipProperty, "输入文字，或按 Ctrl+V 粘贴图片…");
        editor.TextChanged += (s, e) => {
            if (!initialized) return;
            note.Text = editor.Text ?? "";
            owner.NotifyNoteChanged();
        };
        DataObject.AddPastingHandler(editor, OnEditorPasting);
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
        Grid.SetRow(hintLabel, 2);
        main.Children.Add(hintLabel);

        PreviewKeyDown += OnWindowPreviewKeyDown;
        SourceInitialized += (s, e) => Native.Round(this);

        LoadExistingImages();

        Loaded += (s, e) => {
            initialized = true;
            note.Width = Width;
            note.Height = Height;
            note.Left = Left;
            note.Top = Top;
            editor.Focus();
        };
        LocationChanged += (s, e) => {
            if (!initialized || WindowState != WindowState.Normal) return;
            note.Left = Left;
            note.Top = Top;
            owner.NotifyNoteChanged();
        };
        SizeChanged += (s, e) => {
            if (!initialized || WindowState != WindowState.Normal) return;
            note.Width = Width;
            note.Height = Height;
            owner.NotifyNoteChanged();
        };
        Closed += (s, e) => {
            DataObject.RemovePastingHandler(editor, OnEditorPasting);
            owner.OnNoteWindowClosed(this);
        };
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
        if (!editor.IsKeyboardFocusWithin &&
            ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V) ||
             (Keyboard.Modifiers == ModifierKeys.Shift && e.Key == Key.Insert))) {
            if (TryPasteImage(reportWhenEmpty: false)) {
                e.Handled = true;
            }
        }
    }

    void LoadExistingImages() {
        imagesPanel.Children.Clear();
        foreach (var path in note.Images.ToList()) {
            var resolved = owner.ResolveNoteImagePath(path);
            if (File.Exists(resolved)) {
                try {
                    var bmp = LoadBitmapFromFile(resolved);
                    AddImageControl(bmp, resolved, saveToList: false);
                } catch {
                    // Ignore corrupted image file
                }
            }
        }
        UpdateHintVisibility();
    }

    void UpdateHintVisibility() {
        hintLabel.Visibility = imagesPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static BitmapImage LoadBitmapFromFile(string path) {
        var bitmap = new BitmapImage();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
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

    public bool TryPasteImage(bool reportWhenEmpty) {
        if (isPasting) return true;
        var now = DateTime.UtcNow;
        if ((now - lastPasteTime).TotalMilliseconds < 180) return true;

        isPasting = true;
        try {
            // 1. Check file drop list on clipboard (distinct image paths)
            if (Clipboard.ContainsFileDropList()) {
                var files = Clipboard.GetFileDropList();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pastedAny = false;
                foreach (var file in files) {
                    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file) &&
                        ImageExtensions.Contains(Path.GetExtension(file)) &&
                        seen.Add(Path.GetFullPath(file))) {
                        var savedPath = CopyImageFileToConfiguredDir(file);
                        var bmp = LoadBitmapFromFile(savedPath);
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
            if (Clipboard.ContainsText()) {
                var text = Clipboard.GetText();
                if (TryResolveImageFileFromText(text, out var pathFromText)) {
                    var savedPath = CopyImageFileToConfiguredDir(pathFromText);
                    var bmp = LoadBitmapFromFile(savedPath);
                    AddImageControl(bmp, savedPath, saveToList: true);
                    lastPasteTime = DateTime.UtcNow;
                    return true;
                }
            }

            // 3. Check bitmap image on clipboard
            if (Clipboard.ContainsImage()) {
                var source = Clipboard.GetImage();
                if (source != null) {
                    var savedPath = SaveBitmapToConfiguredDir(source);
                    var bmp = LoadBitmapFromFile(savedPath);
                    AddImageControl(bmp, savedPath, saveToList: true);
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

    string SaveBitmapToConfiguredDir(BitmapSource bitmap) {
        var dir = owner.GetNoteImageDirectory();
        Directory.CreateDirectory(dir);
        var fileName = $"note-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}.png";
        var fullPath = Path.Combine(dir, fileName);
        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(fs);
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

    void AddImageControl(BitmapSource bitmap, string filePath, bool saveToList) {
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
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = Cursors.Hand,
            Child = image
        };
        MainWindow.Localize(container, ToolTipProperty, "双击使用系统图片查看器打开");

        void OpenImage() {
            try {
                var resolved = owner.ResolveNoteImagePath(filePath);
                if (!File.Exists(resolved)) {
                    Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
                    using var fs = new FileStream(resolved, FileMode.Create, FileAccess.Write, FileShare.Read);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(fs);
                }
                Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true });
            } catch (Exception ex) {
                ShowNotice(T("无法打开图片：") + ex.Message);
            }
        }

        void RemoveImage() {
            imagesPanel.Children.Remove(container);
            note.Images.Remove(filePath);
            owner.NotifyNoteChanged();
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

    void ShowNotice(string message) {
        hintLabel.Text = message;
        hintLabel.Visibility = Visibility.Visible;
    }
}
