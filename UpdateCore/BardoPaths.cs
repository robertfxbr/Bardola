namespace NexoLoL.Updates;

public static class BardoPaths
{
    public static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Bardo.gg");
    public static string Launcher=>Path.Combine(Root,"Bardo.gg.exe");
    public static void MigrateLegacyData(string? destination=null,string? legacy=null)
    {
        string target=Path.GetFullPath(destination??Root),source=Path.GetFullPath(legacy??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NexoLoL"));
        Directory.CreateDirectory(target);string marker=Path.Combine(target,".legacy-migrated");if(File.Exists(marker))return;
        if(Directory.Exists(source)){
            foreach(string name in new[]{"preferences-v2.json","preferences.json","sessions.json","catalog.json"})Copy(Path.Combine(source,name),Path.Combine(target,name));
            foreach(string folder in new[]{"builds","icons","rune-backups"}){
                string origin=Path.Combine(source,folder);if(!Directory.Exists(origin)||File.GetAttributes(origin).HasFlag(FileAttributes.ReparsePoint))continue;
                foreach(string file in Directory.EnumerateFiles(origin,"*",SearchOption.TopDirectoryOnly))if(!File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))Copy(file,Path.Combine(target,folder,Path.GetFileName(file)));
            }
        }
        File.WriteAllText(marker,DateTimeOffset.UtcNow.ToString("O"));
    }
    static void Copy(string source,string target){if(!File.Exists(source)||File.Exists(target)||File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))return;Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(source,target,false);}
}
