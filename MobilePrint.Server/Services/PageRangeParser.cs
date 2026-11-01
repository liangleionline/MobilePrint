using System.Text.RegularExpressions;

namespace MobilePrint.Server.Services;

public static class PageRangeParser
{
    /// <summary>
    /// Parse page range string like "1,3,5-7" into a sorted, deduped list of 1-based page numbers.
    /// Empty/null returns all pages (caller decides).
    /// Out of range pages are filtered. Returns (pages, warning).
    /// </summary>
    public static (List<int> pages, string? warning) Parse(string? range, int totalPages)
    {
        var result = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(range) || range.Trim() == "all" || range.Trim() == "全部")
        {
            return (Enumerable.Range(1, totalPages).ToList(), null);
        }

        var parts = range.Split(',', ' ', ';');
        bool anyValid = false;
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            var m = Regex.Match(p, @"^(\d+)(?:-(\d+))?$");
            if (!m.Success)
                throw new FormatException($"页码格式无效: '{p}'");

            int start = int.Parse(m.Groups[1].Value);
            if (m.Groups[2].Success)
            {
                int end = int.Parse(m.Groups[2].Value);
                if (start > end) throw new FormatException($"页码范围起始大于结束: '{p}'");
                for (int i = start; i <= end; i++) result.Add(i);
            }
            else
            {
                result.Add(start);
            }
            anyValid = true;
        }

        if (!anyValid) throw new FormatException("页码范围为空");

        // filter out-of-range
        var valid = result.Where(p => p >= 1 && p <= totalPages).ToList();
        string? warning = null;
        if (valid.Count < result.Count)
        {
            warning = $"文档共{totalPages}页，部分页码超出范围已忽略";
        }
        if (valid.Count == 0)
            throw new FormatException($"所选页码全部超出文档范围（共{totalPages}页）");

        return (valid, warning);
    }
}
