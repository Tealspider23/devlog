namespace Devlog.Core.Derivation;

/// <summary>
/// File extension → language, for the "languages touched" breadth metric.
/// Deliberately shallow — this is a KPI input, not a linguist-grade classifier,
/// and a wrong guess on an obscure extension costs nothing.
/// </summary>
public static class LanguageDetector
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".ts"] = "TypeScript",
        [".tsx"] = "TypeScript",
        [".js"] = "JavaScript",
        [".jsx"] = "JavaScript",
        [".py"] = "Python",
        [".java"] = "Java",
        [".go"] = "Go",
        [".rs"] = "Rust",
        [".rb"] = "Ruby",
        [".php"] = "PHP",
        [".c"] = "C",
        [".h"] = "C",
        [".cpp"] = "C++",
        [".hpp"] = "C++",
        [".sql"] = "SQL",
        [".html"] = "HTML",
        [".css"] = "CSS",
        [".scss"] = "CSS",
        [".json"] = "JSON",
        [".yaml"] = "YAML",
        [".yml"] = "YAML",
        [".md"] = "Markdown",
        [".sh"] = "Shell",
        [".ps1"] = "PowerShell",
        [".xml"] = "XML",
        [".razor"] = "Razor",
        [".cshtml"] = "Razor",
    };

    /// <summary>Distinct languages touched by a set of changed file paths, order preserved by first appearance.</summary>
    public static IReadOnlyList<string> DetectAll(IEnumerable<string> filePaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var path in filePaths)
        {
            var lang = Detect(path);
            if (lang is not null && seen.Add(lang))
            {
                result.Add(lang);
            }
        }

        return result;
    }

    public static string? Detect(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && ByExtension.TryGetValue(ext, out var lang) ? lang : null;
    }
}
