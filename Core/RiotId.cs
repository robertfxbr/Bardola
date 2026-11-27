namespace NexoLoL.Core;

public static class RiotId
{
    public static bool TrySplit(string? value,out string name,out string tag)
    {
        var parts=(value??"").Trim().Split('#');
        name=parts[0].Trim();
        tag=parts.Length==2?parts[1].Trim():"";
        return name.Length is >=1 and <=32 && tag.Length is >=1 and <=8 &&
            !name.Any(c=>char.IsControl(c) || c is '/' or '\\') && name is not ("." or "..") &&
            tag.All(char.IsLetterOrDigit);
    }
}
