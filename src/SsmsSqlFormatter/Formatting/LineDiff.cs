using System;
using System.Collections.Generic;

namespace SsmsSqlFormatter.Formatting
{
    public enum DiffOp
    {
        Equal,
        Delete,
        Insert
    }

    public class DiffLine
    {
        public DiffOp Op { get; set; }
        public string Text { get; set; }
    }

    /// <summary>
    /// Line-level diff (classic LCS-based algorithm) between two texts, used to
    /// highlight what the formatter changed in the Preview window.
    /// </summary>
    public static class LineDiff
    {
        // Above this many line-pairs the O(n*m) DP table would get too large; fall
        // back to a plain "everything replaced" result rather than risk excessive
        // time/memory on a pathologically large script.
        private const long MaxCells = 4_000_000;

        public static List<DiffLine> Compute(string original, string formatted)
        {
            var a = SplitLines(original);
            var b = SplitLines(formatted);

            if ((long)(a.Length + 1) * (b.Length + 1) > MaxCells)
            {
                var fallback = new List<DiffLine>();
                foreach (var line in a) fallback.Add(new DiffLine { Op = DiffOp.Delete, Text = line });
                foreach (var line in b) fallback.Add(new DiffLine { Op = DiffOp.Insert, Text = line });
                return fallback;
            }

            return Diff(a, b);
        }

        private static string[] SplitLines(string s) =>
            string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Replace("\r\n", "\n").Split('\n');

        private static List<DiffLine> Diff(string[] a, string[] b)
        {
            int n = a.Length, m = b.Length;
            var dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var result = new List<DiffLine>();
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[x] == b[y])
                {
                    result.Add(new DiffLine { Op = DiffOp.Equal, Text = a[x] });
                    x++; y++;
                }
                else if (dp[x + 1, y] >= dp[x, y + 1])
                {
                    result.Add(new DiffLine { Op = DiffOp.Delete, Text = a[x] });
                    x++;
                }
                else
                {
                    result.Add(new DiffLine { Op = DiffOp.Insert, Text = b[y] });
                    y++;
                }
            }
            while (x < n) { result.Add(new DiffLine { Op = DiffOp.Delete, Text = a[x] }); x++; }
            while (y < m) { result.Add(new DiffLine { Op = DiffOp.Insert, Text = b[y] }); y++; }
            return result;
        }
    }
}
