using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;

namespace Neemle.CSharpComposer.Tests;

// GENE-29 AD 84939297 v2 section F, stages F1 and F4: the corpus gate, light. For every embedded src/** and tests/**
// file it checks
// - OWN-a once per file: the comment lines lexed from the source equal the parsed model's comment lines plus the comment
//   lines inside opaque and inactive text (CorpusComments);
// - the UNTOUCHED parse once per file, rendered as the product renders it ("formatted": since F4 every render formats
//   the whole document; usings keep their order, see GateOptions);
// - a small seeded sample of EDITS per file: at most CORPUS_GATE_NODES_PER_FILE nodes per variant, one node per kind:
//   detach (Replace with node.Detach()), remove (Remove a list item) and move (Move a list item to the start of its
//   list).
// After each render it checks
// - P3: Cs.Parse(output) has the same coverage and diagnostic codes (for remove: no new codes and no worse coverage),
//   and its root equals the intended model under the gate's own GuardEquivalence;
// - OWN-b (edits): the edited model's comments equal the original's, minus exactly the removed subtree's for remove;
// - OWN-c: every comment of the re-parse has the same owner path, placement and text as in the intended model;
// - IDEM: rendering the re-parse gives the same text again.
// AD v2 deleted P1 (byte identity) and P2 (edit locality). Violations go to a JSON report. They fail the run only where
// CORPUS_GATE_ENFORCE asks for it: "untouched" (the CI step since F4) fails on any render error, P3 or IDEM violation of
// the untouched render and on any OWN-a violation; the edit variants stay report-only until F6. Infrastructure errors (an
// empty corpus, a gate bug, an unwritable report) always fail it.
internal sealed class CorpusGate
{
    internal const string ResourcePrefix = "Corpus/";
    const int ExamplesPerPattern = 3;
    const int PatternsListed = 40;

    internal enum Variant { Detach, Remove, Move }

    // Every render of the gate keeps the using order: sorting usings reorders the model (an option, not layout), and P3
    // compares the re-parse with the model as parsed or edited (GENE-29 F2).
    static readonly Rendering.RenderOptions GateOptions = new() { SortUsings = false };

    // Settings come from the environment so the CI step, the manual job and a local run differ without code changes:
    // CORPUS_GATE_REPORT (report path),
    // CORPUS_GATE_VARIANTS (comma-separated: detach, remove, move; default detach,remove,move; none = untouched
    // checks only),
    // CORPUS_GATE_NODES_PER_FILE (edited nodes per file and variant, one per node kind, seeded; default 5; all = every
    // eligible node),
    // CORPUS_GATE_UNTOUCHED (formatted or none; default formatted),
    // CORPUS_GATE_ENFORCE (untouched or none; default none),
    // CORPUS_GATE_SEED (default 29),
    // CORPUS_GATE_FILES (semicolon-separated substrings selecting files, for a local run on a few files) and
    // CORPUS_GATE_PARALLELISM (worker count, default the processor count).
    internal sealed record Settings(string ReportPath, string[] Files, int Parallelism, Variant[] Variants, int NodesPerFile,
        bool Untouched, bool Enforce, int Seed)
    {
        internal const int AllNodes = int.MaxValue;

        internal static Settings FromEnvironment()
        {
            static string? Get(string name) => Environment.GetEnvironmentVariable(name);
            string? report = Get("CORPUS_GATE_REPORT"), files = Get("CORPUS_GATE_FILES"), parallelism = Get("CORPUS_GATE_PARALLELISM"),
                variants = Get("CORPUS_GATE_VARIANTS"), nodes = Get("CORPUS_GATE_NODES_PER_FILE"), untouched = Get("CORPUS_GATE_UNTOUCHED"),
                enforce = Get("CORPUS_GATE_ENFORCE"), seed = Get("CORPUS_GATE_SEED");
            return new Settings(
                string.IsNullOrWhiteSpace(report) ? Path.Combine(AppContext.BaseDirectory, "corpus-gate.json") : report,
                string.IsNullOrWhiteSpace(files) ? [] : files.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                int.TryParse(parallelism, out int workers) && workers > 0 ? workers : Environment.ProcessorCount,
                string.IsNullOrWhiteSpace(variants) ? [Variant.Detach, Variant.Remove, Variant.Move] :
                    variants.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) ? [] : variants.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(v => Enum.Parse<Variant>(v, ignoreCase: true)).Distinct().ToArray(),
                nodes?.Trim().ToLowerInvariant() switch
                {
                    null or "" => 5,
                    "all" => AllNodes,
                    var n when int.TryParse(n, out int count) && count >= 0 => count,
                    _ => throw new InvalidOperationException("CORPUS_GATE_NODES_PER_FILE must be a count or all, not " + nodes + ".")
                },
                untouched?.Trim().ToLowerInvariant() switch
                {
                    null or "" or "formatted" => true,
                    "none" => false,
                    _ => throw new InvalidOperationException("CORPUS_GATE_UNTOUCHED must be formatted or none, not " + untouched + ".")
                },
                enforce?.Trim().ToLowerInvariant() switch
                {
                    null or "" or "none" => false,
                    "untouched" => true,
                    _ => throw new InvalidOperationException("CORPUS_GATE_ENFORCE must be untouched or none, not " + enforce + ".")
                },
                int.TryParse(seed, out int value) ? value : 29);
        }
    }

    internal sealed record Violation(string Property, string File, string Path, string Kind, string Pattern, string Intended, string Actual);

    sealed record Candidate(NodePath Path, CsNode Node, CsNode Parent, string Kind, int Order);

    // One row of the report: an untouched render mode or an edit variant.
    sealed class Tally(string name, string description)
    {
        internal readonly string Name = name, Description = description;
        internal int Checked, Rejected, RenderErrors, Emit001, IdenticalOutput, ReparseErrors,
            P3Checked, P3Violations, OwnBChecked, OwnBViolations, OwnCChecked, OwnCViolations, IdemChecked, IdemViolations;
        internal long Ticks;
        internal readonly Dictionary<string, int> Kinds = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> Patterns = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, List<Violation>> Examples = new(StringComparer.Ordinal);

        internal void Add(Violation violation)
        {
            string key = violation.Property + " " + violation.Pattern;
            Patterns[key] = Patterns.GetValueOrDefault(key) + 1;
            var list = Examples.TryGetValue(key, out var found) ? found : Examples[key] = [];
            if (list.Count < ExamplesPerPattern) list.Add(violation);
        }
    }

    readonly Settings settings;
    readonly IReadOnlyDictionary<string, string> resources;
    readonly ConcurrentBag<string> gateErrors = [];
    readonly Tally ownA = new("ownParse", "OWN-a: source comment lines equal the parsed model's comment lines");
    readonly Dictionary<string, Tally> untouched = new();
    readonly Dictionary<Variant, Tally> variants = new();
    int parsed, parseErrors, ownAMissing, ownAExtra;
    long chars, nodes;

    internal CorpusGate(Settings settings)
    {
        this.settings = settings;
        resources = typeof(CorpusGate).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToDictionary(name => name[ResourcePrefix.Length..].Replace('\\', '/'), name => name, StringComparer.Ordinal);
        if (settings.Untouched)
            untouched["formatted"] = new("formatted", "the untouched parse rendered by CsDocument.Render() with SortUsings off, and the re-parse rendered the same way for IDEM");
        foreach (var variant in settings.Variants)
            variants[variant] = new(Name(variant), variant switch
            {
                Variant.Detach => "Replace(path, node.Detach())",
                Variant.Remove => "Remove(path) of a list item",
                _ => "Move(path, parent, index of the list's first item) of a list item that is not first"
            });
    }

    internal int CorpusSize => resources.Count;

    internal IReadOnlyCollection<string> GateErrors => gateErrors;

    // GENE-29 F4 (AD F): with CORPUS_GATE_ENFORCE=untouched, the untouched render must have no render error, no reparse
    // error, no P3 and no IDEM violation, and no file may break OWN-a. Empty when nothing is enforced.
    internal IReadOnlyList<string> Enforcement()
    {
        var failures = new List<string>();
        if (!settings.Enforce) return failures;
        if (ownA.OwnCViolations != 0) failures.Add("OWN-a: " + ownA.OwnCViolations + " file(s)");
        foreach (var t in untouched.Values)
            foreach (var (what, count) in new[] { ("render errors", t.RenderErrors), ("reparse errors", t.ReparseErrors), ("P3", t.P3Violations), ("IDEM", t.IdemViolations) })
                if (count != 0) failures.Add("untouched " + t.Name + " " + what + ": " + count);
        return failures;
    }

    internal JsonObject Run(Action<string> log)
    {
        var wall = Stopwatch.StartNew();
        var files = resources.Keys.Where(file => settings.Files.Length == 0 || settings.Files.Any(f => file.Contains(f, StringComparison.Ordinal)))
            .Select(file => (File: file, Bytes: Read(file))).OrderByDescending(f => f.Bytes.Length).ThenBy(f => f.File, StringComparer.Ordinal).ToArray();
        var options = new ParallelOptions { MaxDegreeOfParallelism = settings.Parallelism };
        string progress = settings.ReportPath + ".progress.log";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(progress))!);
        File.WriteAllText(progress, $"{files.Length} files{Environment.NewLine}");
        int done = 0, step = Math.Max(1, files.Length / 10);
        var progressLock = new object();
        // Largest files first, without buffering, so the slowest file never starts last.
        Parallel.ForEach(Partitioner.Create(files, EnumerablePartitionerOptions.NoBuffering), options, file =>
        {
            Guarded("file " + file.File, () => CheckFile(file.File, file.Bytes));
            int finished = Interlocked.Increment(ref done);
            if (finished % step == 0 || finished == files.Length)
                lock (progressLock) File.AppendAllText(progress, $"{finished}/{files.Length} files, {wall.Elapsed.TotalSeconds:F1}s{Environment.NewLine}");
        });
        wall.Stop();
        log($"Checked {files.Length} files in {wall.Elapsed.TotalSeconds:F1}s.");
        return Report(files.Length, wall.Elapsed);
    }

    void Guarded(string what, Action action)
    {
        try { action(); }
        catch (Exception error) { gateErrors.Add(what + ": " + error); }
    }

    void CheckFile(string file, byte[] bytes)
    {
        ParseResult<CsDocument> original;
        try { original = Cs.Parse(bytes); }
        catch (Exception error)
        {
            Interlocked.Increment(ref parseErrors);
            gateErrors.Add("parse " + file + ": " + error.GetType().Name + ": " + Clip(error.Message));
            return;
        }
        Interlocked.Increment(ref parsed);
        string text;
        using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true)) text = reader.ReadToEnd();
        Interlocked.Add(ref chars, text.Length);
        var root = original.Root;
        var entries = CorpusComments.Entries(root);
        OwnParse(file, text, entries);
        foreach (var (mode, tally) in untouched)
        {
            var started = Stopwatch.GetTimestamp();
            Verify(tally, file, "", "CsDocument", original, text, root, sameModel: true, relaxed: false, d => d.Render(GateOptions), expected: null);
            Add(tally, Stopwatch.GetTimestamp() - started);
        }
        if (variants.Count == 0 || settings.NodesPerFile == 0) return;
        var candidates = Candidates(root);
        Interlocked.Add(ref nodes, candidates.Count);
        var bag = CorpusComments.Bag(entries.Select(e => e.Key));
        foreach (var (variant, tally) in variants)
            foreach (var (candidate, first) in Sample(file, variant, candidates))
            {
                var started = Stopwatch.GetTimestamp();
                Edit(tally, variant, file, original, text, bag, candidate, first);
                Add(tally, Stopwatch.GetTimestamp() - started);
            }
    }

    static void Add(Tally tally, long ticks)
    {
        lock (tally) tally.Ticks += ticks;
    }

    // OWN-a: the comment lines lexed from the source against the model's, both after the gate's dedent.
    void OwnParse(string file, string text, List<CorpusComments.Entry> entries)
    {
        var source = CorpusComments.Bag(CorpusComments.SourceLines(text));
        var model = CorpusComments.Bag(entries.Where(e => !e.HiddenFromSource).SelectMany(e => e.Text.Split('\n')));
        var difference = CorpusComments.Difference(source, model);
        lock (ownA)
        {
            ownA.Checked++;
            ownA.OwnCChecked++;
            if (difference.Count == 0) return;
            ownA.OwnCViolations++;
            ownAMissing += difference.Sum(d => Math.Max(0, d.Expected - d.Actual));
            ownAExtra += difference.Sum(d => Math.Max(0, d.Actual - d.Expected));
            var (line, expected, actual) = difference[0];
            ownA.Add(new("OWN-a", file, "", "CsDocument", expected > actual ? "missing from model" : "extra in model",
                expected + "x " + Clip(line), actual + "x"));
        }
    }

    // Every non-root node outside comments and syntax trivia, with its parent, in document order.
    static List<Candidate> Candidates(CsDocument root)
    {
        var result = new List<Candidate>();
        var parents = new List<CsNode>();
        var skipped = new List<bool>();
        foreach (var (path, node) in root.Descendants())
        {
            int depth = path.Segments.Count;
            if (parents.Count > depth) { parents.RemoveRange(depth, parents.Count - depth); skipped.RemoveRange(depth, skipped.Count - depth); }
            bool skip = depth > 0 && skipped[depth - 1] || node is CsComment or CsSyntaxTrivia;
            parents.Add(node);
            skipped.Add(skip);
            if (depth == 0 || skip) continue;
            result.Add(new(path, node, parents[depth - 1], GuardEquivalence.Kind(node), result.Count));
        }
        return result;
    }

    // Seeded order, then the first eligible node of each kind until the per-file cap.
    IEnumerable<(Candidate Candidate, int First)> Sample(string file, Variant variant, List<Candidate> candidates)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        int taken = 0;
        foreach (var candidate in candidates.OrderBy(c => Hash(file, (int)variant, c.Order, settings.Seed)).ThenBy(c => c.Order))
        {
            if (taken >= settings.NodesPerFile) yield break;
            if (settings.NodesPerFile != Settings.AllNodes && kinds.Contains(candidate.Kind)) continue;
            int first = -1;
            if (variant is Variant.Remove or Variant.Move)
            {
                first = ListFirst(candidate.Parent, candidate.Node);
                if (first < 0) continue;
                if (variant == Variant.Move && first == candidate.Path.Segments[candidate.Path.Segments.Count - 1]) continue;
            }
            kinds.Add(candidate.Kind);
            taken++;
            yield return (candidate, first);
        }
    }

    static uint Hash(string file, int variant, int order, int seed)
    {
        uint hash = 2166136261u ^ (uint)seed;
        foreach (char c in file) hash = (hash ^ c) * 16777619u;
        hash = (hash ^ (uint)variant) * 16777619u;
        hash = (hash ^ (uint)order) * 16777619u;
        return hash ^ hash >> 15;
    }

    // When `child` is an element of a list property of `parent` (other than Comments and SyntaxTrivia), the index in
    // parent.Children of that list's first element; else -1.
    internal static int ListFirst(CsNode parent, CsNode child)
    {
        foreach (var getter in GuardEquivalence.For(parent.GetType()))
        {
            if (getter.Name is nameof(CsNode.Comments) or nameof(CsNode.SyntaxTrivia)) continue;
            if (getter.Read(parent) is not IEnumerable list || list is string) continue;
            var items = list.Cast<object?>().ToList();
            if (!items.Any(item => ReferenceEquals(item, child))) continue;
            var children = parent.Children;
            for (int i = 0; i < children.Count; i++)
                if (ReferenceEquals(children[i], items[0])) return i;
            return -1;
        }
        return -1;
    }

    void Edit(Tally tally, Variant variant, string file, ParseResult<CsDocument> original, string text, Dictionary<string, int> bag,
        Candidate candidate, int first)
    {
        var root = original.Root;
        string path = string.Join('.', candidate.Path.Segments);
        lock (tally) tally.Kinds[candidate.Kind] = tally.Kinds.GetValueOrDefault(candidate.Kind) + 1;
        CsDocument edited;
        try
        {
            edited = variant switch
            {
                Variant.Detach => root.Replace(candidate.Path, candidate.Node.Detach()),
                Variant.Remove => root.Remove(candidate.Path),
                _ => root.Move(candidate.Path, new NodePath(new Values<int>(candidate.Path.Segments.Take(candidate.Path.Segments.Count - 1))), first)
            };
        }
        catch (Exception error)
        {
            // An edit the library refuses (a validation error, an unaddressable slot) is not a property violation.
            lock (tally)
            {
                tally.Checked++;
                tally.Rejected++;
                tally.Patterns["rejected " + candidate.Kind + " " + error.GetType().Name] =
                    tally.Patterns.GetValueOrDefault("rejected " + candidate.Kind + " " + error.GetType().Name) + 1;
            }
            return;
        }
        var expected = bag;
        if (variant == Variant.Remove)
        {
            expected = new Dictionary<string, int>(bag, StringComparer.Ordinal);
            foreach (var entry in CorpusComments.Entries(candidate.Node)) expected[entry.Key] = expected.GetValueOrDefault(entry.Key) - 1;
        }
        Verify(tally, file, path, candidate.Kind, original, text, edited, sameModel: variant == Variant.Detach,
            relaxed: variant == Variant.Remove, d => d.Render(GateOptions), expected);
    }

    // Renders `intended` and checks OWN-b (when `expected` is given), P3, OWN-c and IDEM. `sameModel` says the intended
    // model equals the parsed one, so an output equal to the source re-parses to it and passes trivially.
    static void Verify(Tally tally, string file, string path, string kind, ParseResult<CsDocument> original, string text,
        CsDocument intended, bool sameModel, bool relaxed, Func<CsDocument, string> render, Dictionary<string, int>? expected)
    {
        var found = new List<Violation>();
        bool ownBChecked = false, ownBBroken = false, rendered = false, identical = false, reparsedOk = false, p3Broken = false,
            ownCBroken = false, idemBroken = false;
        List<CorpusComments.Entry>? intendedEntries = null;
        string output = "";
        try
        {
            if (expected != null)
            {
                ownBChecked = true;
                intendedEntries = CorpusComments.Entries(intended);
                var difference = CorpusComments.Difference(expected.Where(e => e.Value != 0).ToDictionary(StringComparer.Ordinal),
                    CorpusComments.Bag(intendedEntries.Select(e => e.Key)));
                if (difference.Count != 0)
                {
                    ownBBroken = true;
                    var (key, want, have) = difference[0];
                    found.Add(new("OWN-b", file, path, kind, (want > have ? "lost " : "gained ") + string.Join(' ', key.Split('|').Take(key.StartsWith("Raw|", StringComparison.Ordinal) ? 1 : 2)),
                        want + "x " + Clip(key), have + "x"));
                }
            }
            try { output = render(intended); rendered = true; }
            catch (Exception error)
            {
                if (error.Message.StartsWith("EMIT001", StringComparison.Ordinal)) lock (tally) tally.Emit001++;
                found.Add(new("render", file, path, kind, kind + " " + error.GetType().Name, "", Clip(error.Message)));
                return;
            }
            if (sameModel && string.Equals(output, text, StringComparison.Ordinal)) { identical = true; return; }
            ParseResult<CsDocument> reparsed;
            try { reparsed = Cs.Parse(output); reparsedOk = true; }
            catch (Exception error)
            {
                p3Broken = true;
                found.Add(new("P3", file, path, kind, "reparse " + error.GetType().Name, "", Clip(error.Message)));
                return;
            }
            // P3
            string? cause = null, want3 = "", have3 = "", where = "";
            if (relaxed ? reparsed.Coverage > original.Coverage : reparsed.Coverage != original.Coverage)
                (cause, want3, have3) = ("coverage", original.Coverage.ToString(), reparsed.Coverage.ToString());
            else if (!CodesMatch(original, reparsed, relaxed)) (cause, want3, have3) = ("diagnostics", Codes(original), Codes(reparsed));
            else if (GuardEquivalence.Compare(intended, reparsed.Root) is { } divergence)
                (cause, want3, have3, where) = ("tree " + divergence.Pattern, divergence.Intended, divergence.Actual, divergence.Path);
            if (cause != null)
            {
                p3Broken = true;
                int at = text.AsSpan().CommonPrefixLength(output);
                found.Add(new("P3", file, path, kind, cause, Clip(want3) + (where.Length == 0 ? "" : " at " + Tail(where)) + " | " + Excerpt(text, at),
                    Clip(have3) + " | " + Excerpt(output, at)));
            }
            // OWN-c
            intendedEntries ??= CorpusComments.Entries(intended);
            var actualEntries = CorpusComments.Entries(reparsed.Root);
            var ownership = CorpusComments.Difference(CorpusComments.Bag(intendedEntries.Select(e => e.Id)), CorpusComments.Bag(actualEntries.Select(e => e.Id)));
            if (ownership.Count != 0)
            {
                ownCBroken = true;
                var gained = ownership.Where(d => d.Actual > d.Expected).Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
                var lost = ownership.FirstOrDefault(d => d.Expected > d.Actual);
                if (lost.Key != null)
                {
                    var before = intendedEntries.First(e => e.Id == lost.Key);
                    var after = actualEntries.FirstOrDefault(e => e.Text == before.Text && gained.Contains(e.Id));
                    found.Add(new("OWN-c", file, path, kind, before.Label + " on " + before.OwnerKind +
                        (after.Key == null ? " lost" : " -> " + after.Label + " on " + after.OwnerKind),
                        Tail(before.Owner) + " " + Clip(before.Text), after.Key == null ? "missing" : Tail(after.Owner)));
                }
                else
                {
                    var extra = actualEntries.First(e => gained.Contains(e.Id));
                    found.Add(new("OWN-c", file, path, kind, extra.Label + " on " + extra.OwnerKind + " extra", "", Tail(extra.Owner) + " " + Clip(extra.Text)));
                }
            }
            // IDEM
            string again;
            try { again = render(reparsed.Root); }
            catch (Exception error)
            {
                idemBroken = true;
                found.Add(new("IDEM", file, path, kind, "render " + error.GetType().Name, "", Clip(error.Message)));
                return;
            }
            if (!string.Equals(again, output, StringComparison.Ordinal))
            {
                idemBroken = true;
                int at = output.AsSpan().CommonPrefixLength(again);
                found.Add(new("IDEM", file, path, kind, Squeeze(output, at) == Squeeze(again, at) ? "whitespace-only" : "text",
                    Excerpt(output, at), Excerpt(again, at)));
            }
        }
        finally
        {
            lock (tally)
            {
                tally.Checked++;
                if (ownBChecked) { tally.OwnBChecked++; if (ownBBroken) tally.OwnBViolations++; }
                if (!rendered) tally.RenderErrors++;
                else
                {
                    if (identical) tally.IdenticalOutput++;
                    tally.P3Checked++;
                    if (p3Broken) tally.P3Violations++;
                    if (!reparsedOk && !identical) tally.ReparseErrors++;
                    if (identical || reparsedOk)
                    {
                        tally.OwnCChecked++;
                        tally.IdemChecked++;
                        if (ownCBroken) tally.OwnCViolations++;
                        if (idemBroken) tally.IdemViolations++;
                    }
                }
                foreach (var violation in found) tally.Add(violation);
            }
        }
    }

    static string Squeeze(string text, int from)
    {
        var builder = new StringBuilder();
        for (int i = Math.Max(0, from); i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i])) builder.Append(text[i]);
        return builder.ToString();
    }

    static bool CodesMatch(ParseResult<CsDocument> original, ParseResult<CsDocument> reparsed, bool relaxed)
    {
        var want = CorpusComments.Bag(original.Diagnostics.Select(d => d.Code));
        var have = CorpusComments.Bag(reparsed.Diagnostics.Select(d => d.Code));
        return relaxed ? have.All(h => want.GetValueOrDefault(h.Key) >= h.Value) : CorpusComments.Difference(want, have).Count == 0;
    }

    static string Codes(ParseResult<CsDocument> result) => string.Join(",", result.Diagnostics.Select(d => d.Code));

    static string Tail(string path) => path.Length <= 100 ? path : "..." + path[^97..];

    static string Excerpt(string text, int at)
    {
        at = Math.Clamp(at, 0, text.Length);
        int from = Math.Max(0, at - 40), to = Math.Min(text.Length, at + 40);
        return text[from..at] + "‸" + text[at..to];
    }

    static string Clip(string text) => GuardEquivalence.Clip(text);

    byte[] Read(string file)
    {
        using var stream = typeof(CorpusGate).Assembly.GetManifestResourceStream(resources[file])!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    static string Name(Variant variant) => variant.ToString().ToLowerInvariant();

    static double Seconds(long ticks) => Math.Round((double)ticks / Stopwatch.Frequency, 2);

    JsonObject Report(int fileCount, TimeSpan wall) => new()
    {
        ["schema"] = 2,
        ["gate"] = "GENE-29 corpus gate (AD 84939297 v2 section F, stages F1 and F4)",
        ["mode"] = settings.Enforce ? "enforcing on the untouched render and OWN-a, report-only for edits" : "report-only",
        ["enforcementFailures"] = new JsonArray(Enforcement().Select(f => (JsonNode)f).ToArray()),
        ["sample"] = settings.NodesPerFile == Settings.AllNodes ? "every eligible node" :
            "at most " + settings.NodesPerFile + " edited node(s) per file and variant, one per node kind, seed " + settings.Seed,
        ["commit"] = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "",
        ["environment"] = new JsonObject
        {
            ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["processors"] = Environment.ProcessorCount,
            ["parallelism"] = settings.Parallelism,
            ["fileFilter"] = string.Join(";", settings.Files)
        },
        ["timing"] = new JsonObject
        {
            ["wallSeconds"] = Math.Round(wall.TotalSeconds, 1),
            ["cpuSeconds"] = new JsonObject(untouched.Values.Concat(variants.Values).Select(t => KeyValuePair.Create(t.Name, (JsonNode?)Seconds(t.Ticks))))
        },
        ["corpus"] = new JsonObject
        {
            ["files"] = fileCount,
            ["parsed"] = parsed,
            ["parseErrors"] = parseErrors,
            ["chars"] = chars,
            ["candidateNodes"] = nodes
        },
        ["ownParse"] = new JsonObject
        {
            ["description"] = ownA.Description,
            ["files"] = ownA.Checked,
            ["violatingFiles"] = ownA.OwnCViolations,
            ["linesMissingFromModel"] = ownAMissing,
            ["linesExtraInModel"] = ownAExtra,
            ["patterns"] = Patterns(ownA)
        },
        ["untouched"] = new JsonObject(untouched.Values.Select(t => KeyValuePair.Create(t.Name, (JsonNode?)Row(t)))),
        ["variants"] = new JsonObject(variants.Values.Select(t => KeyValuePair.Create(t.Name, (JsonNode?)Row(t)))),
        ["gateErrors"] = new JsonArray(gateErrors.Order(StringComparer.Ordinal).Select(e => (JsonNode)Clip(e)).ToArray())
    };

    static JsonObject Row(Tally t) => new()
    {
        ["description"] = t.Description,
        ["checked"] = t.Checked,
        ["rejectedEdits"] = t.Rejected,
        ["renderErrors"] = t.RenderErrors,
        ["EMIT001"] = t.Emit001,
        ["identicalOutput"] = t.IdenticalOutput,
        ["reparseErrors"] = t.ReparseErrors,
        ["P3"] = new JsonObject { ["checked"] = t.P3Checked, ["violations"] = t.P3Violations },
        ["OWN-b"] = new JsonObject { ["checked"] = t.OwnBChecked, ["violations"] = t.OwnBViolations },
        ["OWN-c"] = new JsonObject { ["checked"] = t.OwnCChecked, ["violations"] = t.OwnCViolations },
        ["IDEM"] = new JsonObject { ["checked"] = t.IdemChecked, ["violations"] = t.IdemViolations },
        ["cpuSeconds"] = Seconds(t.Ticks),
        ["kinds"] = new JsonObject(t.Kinds.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal)
            .Select(k => KeyValuePair.Create(k.Key, (JsonNode?)k.Value))),
        ["patterns"] = Patterns(t)
    };

    // The most frequent violation patterns, each with up to ExamplesPerPattern examples.
    static JsonArray Patterns(Tally t) => new(t.Patterns.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(PatternsListed)
        .Select(p => (JsonNode)new JsonObject
        {
            ["pattern"] = p.Key,
            ["count"] = p.Value,
            ["examples"] = new JsonArray((t.Examples.TryGetValue(p.Key, out var list) ? list : []).Select(v => (JsonNode)new JsonObject
            {
                ["file"] = v.File, ["path"] = v.Path, ["kind"] = v.Kind, ["intended"] = v.Intended, ["actual"] = v.Actual
            }).ToArray())
        }).ToArray());

    internal static void Write(JsonObject report, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
