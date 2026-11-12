using System;
using System.Collections.Generic;

namespace Nexus.Engine.ImageSubsystem.Evidence;

/// <summary>
/// Node pada struktur data BK-Tree (Burkhard-Keller Tree).
/// </summary>
public sealed class BKTreeNode
{
    public string Word { get; }
    public Dictionary<int, BKTreeNode> Children { get; } = new();

    public BKTreeNode(string word)
    {
        Word = word ?? throw new ArgumentNullException(nameof(word));
    }
}

/// <summary>
/// Pohon metrik BK-Tree in-memory untuk fuzzy search cepat berbasis Levenshtein distance.
/// Menggunakan sifat ketaksamaan segitiga (triangle inequality) untuk memangkas ruang pencarian.
/// </summary>
public sealed class BKTree
{
    public BKTreeNode? Root { get; private set; }
    public int Count { get; private set; }

    /// <summary>
    /// Menambahkan kata ke dalam BK-Tree.
    /// Mengabaikan kata kosong atau kata yang sudah ada (duplikasi).
    /// </summary>
    public bool Add(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return false;
        string normalized = word.Trim().ToUpperInvariant();

        if (Root == null)
        {
            Root = new BKTreeNode(normalized);
            Count++;
            return true;
        }

        var current = Root;
        while (true)
        {
            int dist = Levenshtein(current.Word, normalized);
            if (dist == 0)
            {
                // Kata duplikat sudah ada di dalam tree
                return false;
            }

            if (current.Children.TryGetValue(dist, out var child))
            {
                current = child;
            }
            else
            {
                current.Children[dist] = new BKTreeNode(normalized);
                Count++;
                return true;
            }
        }
    }

    /// <summary>
    /// Mencari seluruh kata yang berjarak Levenshtein &lt;= maxDistance dari query.
    /// Mengembalikan daftar pasangan kata dan jaraknya tanpa memilih satu jawaban final.
    /// </summary>
    public List<(string Word, int Distance)> Search(string query, int maxDistance = 3)
    {
        var results = new List<(string Word, int Distance)>();
        if (Root == null || string.IsNullOrWhiteSpace(query) || maxDistance < 0)
            return results;

        string normalizedQuery = query.Trim().ToUpperInvariant();
        var stack = new Stack<BKTreeNode>();
        stack.Push(Root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            int dist = Levenshtein(node.Word, normalizedQuery);

            if (dist <= maxDistance)
            {
                results.Add((node.Word, dist));
            }

            int min = dist - maxDistance;
            int max = dist + maxDistance;

            foreach (var kvp in node.Children)
            {
                if (kvp.Key >= min && kvp.Key <= max)
                {
                    stack.Push(kvp.Value);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Menghitung jarak Levenshtein standar (insert, delete, substitute).
    /// </summary>
    public static int Levenshtein(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    /// <summary>
    /// Alias publik untuk jarak Levenshtein.
    /// </summary>
    public static int LevenshteinDistance(string s, string t) => Levenshtein(s, t);

    /// <summary>
    /// Menghitung jarak Damerau-Levenshtein (Optimal String Alignment)
    /// yang mencakup 4 operasi edit dasar: insertion, deletion, substitution, dan transposition adjacent.
    /// </summary>
    public static int DamerauLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                if (i > 1 && j > 1 && s[i - 1] == t[j - 2] && s[i - 2] == t[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[n, m];
    }
}
