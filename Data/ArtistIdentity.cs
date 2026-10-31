using System.Text;

namespace AneedWeb.Data;

public static class ArtistIdentity
{
    public static long FromName(string name)
    {
        var normalized = Normalize(name);
        ulong hash = 14695981039346656037UL;
        foreach (var character in normalized)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }

        return (long)(hash & 0x7FFF_FFFF_FFFF_FFFF);
    }

    public static string Normalize(string name) =>
        name.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
}
