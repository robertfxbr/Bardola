using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NexoLoL.Updates;

[SupportedOSPlatform("windows")]
public static class BardolaShortcuts
{
    public static void Update(string launcher,bool migrateOnly=false)
    {
        if (!File.Exists(launcher)) return;
        string legacyLauncher=Path.Combine(BardoPaths.Root,"Bardo.gg.exe");
        foreach (var location in new[]{Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.Programs})
        {
            string folder=Environment.GetFolderPath(location);
            if (string.IsNullOrEmpty(folder)) continue;
            string oldPath=Path.Combine(folder,"Bardo.gg.lnk"),newPath=Path.Combine(folder,"Bardola.lnk");
            if (migrateOnly && !File.Exists(oldPath)) continue;
            dynamic? shell=null,link=null,old=null;
            try
            {
                var type=Type.GetTypeFromProgID("WScript.Shell");
                if (type is null) continue;
                shell=Activator.CreateInstance(type);
                if (shell is null) continue;
                bool ownsOld=false;
                if (File.Exists(oldPath))
                {
                    old=shell.CreateShortcut(oldPath);
                    ownsOld=string.Equals((string)old.TargetPath,legacyLauncher,StringComparison.OrdinalIgnoreCase);
                }
                if (migrateOnly && !ownsOld) continue;
                link=shell.CreateShortcut(newPath);
                if (File.Exists(newPath) && !string.Equals((string)link.TargetPath,launcher,StringComparison.OrdinalIgnoreCase)
                    && !string.Equals((string)link.TargetPath,legacyLauncher,StringComparison.OrdinalIgnoreCase)) continue;
                link.TargetPath=launcher;
                link.WorkingDirectory=Path.GetDirectoryName(launcher);
                link.Description="Bardola · League of Legends";
                link.Save();
                if (ownsOld) File.Delete(oldPath);
            }
            catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException) { }
            finally
            {
                if (old!=null) Marshal.ReleaseComObject((object)old);
                if (link!=null) Marshal.ReleaseComObject((object)link);
                if (shell!=null) Marshal.ReleaseComObject((object)shell);
            }
        }
    }
}
