namespace Essenthos.Core.Utils;

public static class SimilarityUtils
{
    
    public static int GetLevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;

        if (n == 0)
        {
            return m;
        }

        if (m == 0)
        {
            return n;
        }

        var p = new int[n + 1];
        var d = new int[n + 1];

        int i;
        int j;

        for (i = 0; i <= n; i++)
        {
            p[i] = i;
        }

        for (j = 1; j <= m; j++)
        {
            var tJ = t[j - 1];
            d[0] = j;

            for (i = 1; i <= n; i++)
            {
                var cost = s[i - 1] == tJ ? 0 : 1;
                d[i] = Math.Min(Math.Min(d[i - 1] + 1, p[i] + 1), p[i - 1] + cost);
            }

            (p, d) = (d, p);
        }

        // our last action in the above loop was to switch d and p, so p now 
        // actually has the most recent cost counts
        return p[n];
    }

}