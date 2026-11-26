namespace NexoLoL.Core;

public static class Storage
{
    public static async Task WriteAsync(string path,string content,CancellationToken ct=default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {await File.WriteAllTextAsync(temp,content,ct);File.Move(temp,path,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
