namespace Yijian.Ubuntu.Platform;

internal static class DataPaths {
    public static string Default {
        get {
            var xdg=Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var basePath=!string.IsNullOrWhiteSpace(xdg)&&Path.IsPathRooted(xdg)?xdg:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local","share");
            return Path.Combine(basePath,"yijian-todo");
        }
    }
    public static void EnsurePrivate(string path) {
        Directory.CreateDirectory(path);
        if(OperatingSystem.IsLinux())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    }
}
