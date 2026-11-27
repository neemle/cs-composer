using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;

namespace Neemle.CSharpComposer.Tests;

// GENE-29 AD 84939297 section I, stage S0: the corpus property gate in report-only mode.
// For every embedded src/** and tests/** file it checks
// - P1: untouched parse then emit is byte-identical;
// - P2: replacing each non-root node with node.Detach() through CsDocument.Replace leaves the output identical to the
//   source outside the node's envelope, widened by adjacent spaces and tabs and one line break after it;
// - P3: re-parsing that output gives the same coverage, the same diagnostic codes and a root equal to the original
//   under the gate's own GuardEquivalence.
// It also measures token-faithfulness (alternative D's precondition). Violations are written to a JSON report and never
// fail the run; only infrastructure errors (an empty corpus, a gate bug, an unwritable report) do.
// Two replacement variants run over the same nodes. "detach" is the AD I3 fresh equivalent, node.Detach(). The 0.6.x
// renderer keeps every ancestor whose model equals its original verbatim, and a detached copy equals the node it
// replaces, so that variant reaches the renderer's edit paths only where Detach changes the model. "reformat"
// (node.Detach().Reformat()) carries the equality-excluded Formatting mark, so the node really renders fresh and its
// ancestors patch around it: P2 is then the public Reformat(path) contract (bytes outside the node are untouched).
internal sealed class CorpusGate
{
    internal const string ResourcePrefix = "Corpus/";
    const int ChunkSize = 32;
    const int ExamplesPerPropertyAndKind = 200;
    const int EditDistanceLimit = 4000;

    internal enum P3Mode { Full, Sampled }

    internal enum Variant { Detach, Reformat }

    // Settings come from the environment so the CI step and a local run can differ without code changes:
    // CORPUS_GATE_REPORT (report path), CORPUS_GATE_P3 (full | sampled, the AD I7 CI fallback),
    // CORPUS_GATE_VARIANTS (comma-separated: detach, reformat; default both),
    // CORPUS_GATE_NODES (all | sampled: for tests/** files, check every node kind at least 3 times per file plus a
    // seeded 1-in-CORPUS_GATE_NODE_SAMPLE sample, default 8; a runtime budget setting beyond the AD's P3-only fallback),
    // CORPUS_GATE_SHARD (i/n: run only shard i of n, files balanced by size squared, so CI jobs can split a full run),
    // CORPUS_GATE_FILES (semicolon-separated substrings selecting files, for a local run on a few files) and
    // CORPUS_GATE_PARALLELISM (worker count, default the processor count).
    internal sealed record Settings(string ReportPath, P3Mode P3, string[] Files, int Parallelism, Variant[] Variants)
    {
        internal bool SampleNodes { get; init; }

        internal int NodeSample { get; init; } = 8;

        internal (int Index, int Count) Shard { get; init; } = (0, 1);

        internal static Settings FromEnvironment()
        {
            string? report = Environment.GetEnvironmentVariable("CORPUS_GATE_REPORT");
            string? p3 = Environment.GetEnvironmentVariable("CORPUS_GATE_P3");
            string? files = Environment.GetEnvironmentVariable("CORPUS_GATE_FILES");
            string? parallelism = Environment.GetEnvironmentVariable("CORPUS_GATE_PARALLELISM");
            string? variants = Environment.GetEnvironmentVariable("CORPUS_GATE_VARIANTS");
            string? nodes = Environment.GetEnvironmentVariable("CORPUS_GATE_NODES");
            string? nodeSample = Environment.GetEnvironmentVariable("CORPUS_GATE_NODE_SAMPLE");
            string? shard = Environment.GetEnvironmentVariable("CORPUS_GATE_SHARD");
            return new Settings(
                string.IsNullOrWhiteSpace(report) ? Path.Combine(AppContext.BaseDirectory, "corpus-gate.json") : report,
                p3?.Trim().ToLowerInvariant() switch
                {
                    null or "" or "full" => P3Mode.Full,
                    "sampled" => P3Mode.Sampled,
                    _ => throw new InvalidOperationException("CORPUS_GATE_P3 must be full or sampled, not " + p3 + ".")
                },
                string.IsNullOrWhiteSpace(files) ? [] : files.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                int.TryParse(parallelism, out int workers) && workers > 0 ? workers : Environment.ProcessorCount,
                string.IsNullOrWhiteSpace(variants) ? [Variant.Detach, Variant.Reformat] :
                    variants.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(v => Enum.Parse<Variant>(v, ignoreCase: true)).Distinct().ToArray())
            {
                SampleNodes = nodes?.Trim().ToLowerInvariant() switch
                {
                    null or "" or "all" => false,
                    "sampled" => true,
                    _ => throw new InvalidOperationException("CORPUS_GATE_NODES must be all or sampled, not " + nodes + ".")
                },
                NodeSample = int.TryParse(nodeSample, out int rate) && rate > 0 ? rate : 8,
                Shard = string.IsNullOrWhiteSpace(shard) ? (0, 1) :
                    shard.Split('/') is [var i, var n] && int.TryParse(i, out int index) && int.TryParse(n, out int count) && count > 0 && index >= 0 && index < count
                        ? (index, count) : throw new InvalidOperationException("CORPUS_GATE_SHARD must be i/n, not " + shard + ".")
            };
        }
    }

    internal sealed record Violation(string Property, string File, int Order, string Path, string Kind, string Cause,
        int Offset, int EnvelopeStart, int EnvelopeEnd, string Source, string Output);

    sealed record Located(int Start, int End, string How);

    sealed record NodeEntry(NodePath Path, CsNode Node, string Kind, Located? Envelope, bool CheckP3);

    sealed class FileState
    {
        internal required string File;
        internal required ParseResult<CsDocument> Parsed;
        internal required string Text;
        internal required NodeEntry[] Nodes;
        internal long PrepareTicks;
    }

    // Everything one replacement variant aggregates.
    sealed class Tally(Variant variant)
    {
        internal readonly Variant Variant = variant;
        internal readonly ConcurrentBag<Violation> Violations = [];
        internal readonly ConcurrentDictionary<string, Counter> ByKind = new(StringComparer.Ordinal);
        internal readonly ConcurrentDictionary<string, int> Causes = new(StringComparer.Ordinal);
        internal readonly ConcurrentDictionary<string, Counter> ByFile = new(StringComparer.Ordinal);
        internal readonly ConcurrentDictionary<string, long> FileTicks = new(StringComparer.Ordinal);
        internal readonly long[] PhaseTicks = new long[Phases.Length];
        internal long WallTicks;

        internal void Cause(string cause) => Causes.AddOrUpdate(cause, 1, (_, count) => count + 1);
    }

    sealed class Counter
    {
        internal int Nodes, P2Checked, P2Violations, P3Checked, P3Trivial, P3Sampled, P3Violations, NotAddressable,
            RenderErrors, ReparseErrors, Identical, EnvelopeSelf, EnvelopeAncestor, EnvelopeNone;

        internal void Add(Counter other)
        {
            Nodes += other.Nodes; P2Checked += other.P2Checked; P2Violations += other.P2Violations;
            P3Checked += other.P3Checked; P3Trivial += other.P3Trivial; P3Sampled += other.P3Sampled;
            P3Violations += other.P3Violations; NotAddressable += other.NotAddressable; RenderErrors += other.RenderErrors;
            ReparseErrors += other.ReparseErrors; Identical += other.Identical; EnvelopeSelf += other.EnvelopeSelf;
            EnvelopeAncestor += other.EnvelopeAncestor; EnvelopeNone += other.EnvelopeNone;
        }

        internal JsonObject Json() => new()
        {
            ["nodes"] = Nodes, ["identicalOutput"] = Identical, ["p2Checked"] = P2Checked, ["p2Violations"] = P2Violations,
            ["p3Checked"] = P3Checked, ["p3TrivialIdenticalOutput"] = P3Trivial, ["p3SkippedBySampling"] = P3Sampled,
            ["p3Violations"] = P3Violations, ["notAddressable"] = NotAddressable, ["renderErrors"] = RenderErrors,
            ["reparseErrors"] = ReparseErrors, ["envelopeSelf"] = EnvelopeSelf, ["envelopeAncestor"] = EnvelopeAncestor,
            ["envelopeNone"] = EnvelopeNone
        };
    }

    // Reflection into the 0.6.x internal SourceOrigin, used ONLY to locate a node's source envelope for P2 (the public
    // API exposes no spans). If a later stage renames it, P2 reports the nodes as unlocated instead of failing.
    static readonly PropertyInfo? OriginProperty = typeof(CsNode).GetProperty("Origin", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly Settings settings;
    readonly IReadOnlyDictionary<string, string> resources;
    readonly ConcurrentBag<Violation> violations = [];
    readonly ConcurrentBag<string> gateErrors = [];
    readonly ConcurrentBag<JsonObject> faithfulness = [];
    static readonly string[] Phases = ["replaceAndValidate", "render", "p2", "reparse", "compare"];
    int p1Checked, p1Violations, parseErrors, nodesSkipped;

    internal CorpusGate(Settings settings)
    {
        this.settings = settings;
        resources = typeof(CorpusGate).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToDictionary(name => name[ResourcePrefix.Length..].Replace('\\', '/'), name => name, StringComparer.Ordinal);
    }

    internal int CorpusSize => resources.Count;

    internal IReadOnlyCollection<string> GateErrors => gateErrors;

    internal JsonObject Run(Action<string> log)
    {
        var wall = Stopwatch.StartNew();
        var files = resources.Keys.Where(file => settings.Files.Length == 0 || settings.Files.Any(f => file.Contains(f, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal).ToArray();
        if (settings.Shard.Count > 1) files = ShardOf(files);
        var options = new ParallelOptions { MaxDegreeOfParallelism = settings.Parallelism };
        var states = new ConcurrentBag<FileState>();
        Parallel.ForEach(files, options, file => Guarded("prepare " + file, () =>
        {
            var state = Prepare(file);
            if (state != null) states.Add(state);
        }));
        var prepared = wall.Elapsed;
        log($"Prepared {states.Count} of {files.Length} files in {prepared.TotalSeconds:F1}s.");
        // Progress goes to a side file as it happens, so a run killed by a CI timeout still shows how far it got.
        string progress = settings.ReportPath + ".progress.log";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(progress))!);
        File.WriteAllText(progress, $"prepared {states.Count} files in {prepared.TotalSeconds:F1}s{Environment.NewLine}");
        var progressLock = new object();
        var tallies = new List<Tally>();
        foreach (var variant in settings.Variants)
        {
            var tally = new Tally(variant);
            tallies.Add(tally);
            foreach (var state in states) { tally.ByFile[state.File] = new Counter(); tally.FileTicks[state.File] = 0; }
            var chunks = states.SelectMany(state => Enumerable.Range(0, (state.Nodes.Length + ChunkSize - 1) / ChunkSize)
                    .Select(i => (State: state, Start: i * ChunkSize)))
                .OrderByDescending(chunk => (long)chunk.State.Text.Length * Math.Min(ChunkSize, chunk.State.Nodes.Length - chunk.Start))
                .ToArray();
            var started = Stopwatch.GetTimestamp();
            int done = 0, step = Math.Max(1, chunks.Length / 20);
            Parallel.ForEach(Partitioner.Create(chunks, EnumerablePartitionerOptions.NoBuffering), options, chunk =>
            {
                Guarded(variant + " check " + chunk.State.File + " from node " + chunk.Start, () => Check(chunk.State, chunk.Start, tally));
                int finished = Interlocked.Increment(ref done);
                if (finished % step == 0 || finished == chunks.Length)
                    lock (progressLock) File.AppendAllText(progress, $"{variant} {finished}/{chunks.Length} chunks, {wall.Elapsed.TotalSeconds:F1}s{Environment.NewLine}");
            });
            tally.WallTicks = Stopwatch.GetTimestamp() - started;
            log($"{variant}: checked {chunks.Length} chunks in {Seconds(tally.WallTicks)}s wall.");
        }
        wall.Stop();
        return Report(files.Length, states.ToArray(), tallies, wall.Elapsed, prepared);
    }

    static double Seconds(long ticks) => Math.Round((double)ticks / Stopwatch.Frequency, 2);

    // Greedy balancing by file length squared (the gate's cost grows with nodes times length), largest first.
    string[] ShardOf(string[] files)
    {
        var load = new long[settings.Shard.Count];
        var mine = new List<string>();
        foreach (var file in files.Select(f => (File: f, Length: (long)Read(f).Length)).OrderByDescending(f => f.Length).ThenBy(f => f.File, StringComparer.Ordinal))
        {
            int target = Array.IndexOf(load, load.Min());
            load[target] += file.Length * file.Length;
            if (target == settings.Shard.Index) mine.Add(file.File);
        }
        return mine.Order(StringComparer.Ordinal).ToArray();
    }

    void Guarded(string what, Action action)
    {
        try { action(); }
        catch (Exception error) { gateErrors.Add(what + ": " + error); }
    }

    FileState? Prepare(string file)
    {
        var started = Stopwatch.GetTimestamp();
        byte[] bytes = Read(file);
        ParseResult<CsDocument> parsed;
        try { parsed = Cs.Parse(bytes); }
        catch (Exception error)
        {
            Interlocked.Increment(ref parseErrors);
            Record(new("parse", file, -1, "", "CsDocument", "parse " + error.GetType().Name, 0, 0, 0, Clip(error.Message), ""));
            return null;
        }
        var root = parsed.Root;
        Interlocked.Increment(ref p1Checked);
        byte[]? emitted = null;
        string p1Error = "";
        try { emitted = root.ToBytes(); } catch (Exception error) { p1Error = error.GetType().Name + ": " + error.Message; }
        if (emitted == null || !emitted.AsSpan().SequenceEqual(bytes))
        {
            Interlocked.Increment(ref p1Violations);
            int at = emitted == null ? 0 : bytes.AsSpan().CommonPrefixLength(emitted);
            Record(new("P1", file, -1, "", "CsDocument", emitted == null ? "emit " + p1Error : "bytes", at, 0, bytes.Length,
                Excerpt(Encoding.UTF8.GetString(bytes), at), emitted == null ? Clip(p1Error) : Excerpt(Encoding.UTF8.GetString(emitted), at)));
        }
        string text = SnapshotText(root) ?? root.Render();
        var nodes = Nodes(file, root, text);
        Faithfulness(file, root, text);
        return new FileState { File = file, Parsed = parsed, Text = text, Nodes = nodes, PrepareTicks = Stopwatch.GetTimestamp() - started };
    }

    NodeEntry[] Nodes(string file, CsDocument root, string text)
    {
        object? rootSnapshot = Snapshot(root);
        var envelopes = new Dictionary<string, Located?>(StringComparer.Ordinal);
        var perKind = new Dictionary<string, int>(StringComparer.Ordinal);
        bool tests = file.StartsWith("tests/", StringComparison.Ordinal);
        bool sampled = settings.P3 == P3Mode.Sampled && tests, sampledNodes = settings.SampleNodes && tests;
        var result = new List<NodeEntry>();
        int order = 0;
        foreach (var (path, node) in root.Descendants())
        {
            var segments = path.Segments;
            string key = string.Join('.', segments);
            Located? own = Envelope(node, rootSnapshot, text.Length);
            Located? envelope = own;
            if (envelope == null && segments.Count > 0 &&
                envelopes.TryGetValue(string.Join('.', segments.Take(segments.Count - 1)), out var parent) && parent != null)
                envelope = parent with { How = "ancestor" };
            envelopes[key] = envelope;
            if (segments.Count == 0) continue;
            string kind = GuardEquivalence.Kind(node);
            int seen = perKind[kind] = perKind.GetValueOrDefault(kind) + 1;
            // AD I7 fallback: every node of src/**, and for tests/** every node kind at least 3 times per file plus a
            // seeded 1-in-8 sample.
            bool p3 = !sampled || seen <= 3 || Sample(file, order, 8);
            if (!sampledNodes || seen <= 3 || Sample(file, order, settings.NodeSample)) result.Add(new(path, node, kind, envelope, p3));
            else Interlocked.Increment(ref nodesSkipped);
            order++;
        }
        return result.ToArray();
    }

    static bool Sample(string file, int order, int rate)
    {
        uint hash = 2166136261u ^ 29u;
        foreach (char c in file) hash = (hash ^ c) * 16777619u;
        hash = (hash ^ (uint)order) * 16777619u;
        return hash % (uint)rate == 0;
    }

    static object? Origin(CsNode node) => OriginProperty?.GetValue(node);

    static object? Snapshot(CsNode node) => Origin(node) is { } origin ? origin.GetType().GetProperty("Snapshot")?.GetValue(origin) : null;

    static string? SnapshotText(CsNode root) => Snapshot(root) is { } snapshot ? snapshot.GetType().GetProperty("Text")?.GetValue(snapshot) as string : null;

    // The node's own envelope when its origin belongs to the root snapshot (directive internals and interpolation
    // holes are lexed from their own snapshots), else null.
    static Located? Envelope(CsNode node, object? rootSnapshot, int length)
    {
        if (rootSnapshot == null || Origin(node) is not { } origin) return null;
        var type = origin.GetType();
        if (!ReferenceEquals(type.GetProperty("Snapshot")?.GetValue(origin), rootSnapshot)) return null;
        if (type.GetProperty("Start")?.GetValue(origin) is not int start || type.GetProperty("End")?.GetValue(origin) is not int end) return null;
        return start < 0 || end > length || start > end ? null : new(start, end, "self");
    }

    void Faithfulness(string file, CsDocument root, string text)
    {
        string fresh;
        try { fresh = ((CsDocument)root.Detach()).Render(); }
        catch (Exception error)
        {
            faithfulness.Add(new JsonObject { ["file"] = file, ["renderError"] = error.GetType().Name + ": " + Clip(error.Message) });
            return;
        }
        var source = CorpusTokens.Scan(text);
        var rendered = CorpusTokens.Scan(fresh);
        string[] Code(List<CorpusTokens.Token> tokens) => tokens.Where(t => !t.Trivia || t.Text.StartsWith('#')).Select(t => t.Text).ToArray();
        string[] All(List<CorpusTokens.Token> tokens) => tokens.Select(t => t.Text).ToArray();
        var (code, codeFresh) = (Code(source), Code(rendered));
        var (all, allFresh) = (All(source), All(rendered));
        int codeDistance = code.SequenceEqual(codeFresh) ? 0 : CorpusTokens.EditDistance(code, codeFresh, EditDistanceLimit);
        int allDistance = all.SequenceEqual(allFresh) ? 0 : CorpusTokens.EditDistance(all, allFresh, EditDistanceLimit);
        var entry = new JsonObject
        {
            ["file"] = file, ["codeTokens"] = code.Length, ["codeTokensFresh"] = codeFresh.Length, ["codeEditDistance"] = codeDistance,
            ["allTokens"] = all.Length, ["allTokensFresh"] = allFresh.Length, ["allEditDistance"] = allDistance
        };
        if (allDistance != 0)
        {
            int at = 0;
            while (at < all.Length && at < allFresh.Length && all[at] == allFresh[at]) at++;
            entry["firstDifference"] = new JsonObject
            {
                ["tokenIndex"] = at,
                ["source"] = Clip(string.Join(" ", all.Skip(Math.Max(0, at - 3)).Take(8))),
                ["fresh"] = Clip(string.Join(" ", allFresh.Skip(Math.Max(0, at - 3)).Take(8)))
            };
        }
        faithfulness.Add(entry);
    }

    void Check(FileState state, int start, Tally tally)
    {
        var begun = Stopwatch.GetTimestamp();
        var root = state.Parsed.Root;
        var local = new Dictionary<string, Counter>(StringComparer.Ordinal);
        var fileCounter = new Counter();
        var ticks = new long[Phases.Length];
        int end = Math.Min(start + ChunkSize, state.Nodes.Length);
        for (int order = start; order < end; order++)
        {
            var entry = state.Nodes[order];
            var counter = local.TryGetValue(entry.Kind, out var found) ? found : local[entry.Kind] = new Counter();
            var node = new Counter();
            CheckNode(state, root, entry, order, node, ticks, tally);
            counter.Add(node);
            fileCounter.Add(node);
        }
        foreach (var (kind, counter) in local)
        {
            var total = tally.ByKind.GetOrAdd(kind, _ => new Counter());
            lock (total) total.Add(counter);
        }
        var perFile = tally.ByFile[state.File];
        lock (perFile) perFile.Add(fileCounter);
        for (int i = 0; i < ticks.Length; i++) Interlocked.Add(ref tally.PhaseTicks[i], ticks[i]);
        long spent = Stopwatch.GetTimestamp() - begun;
        tally.FileTicks.AddOrUpdate(state.File, spent, (_, ticksSoFar) => ticksSoFar + spent);
    }

    static void CheckNode(FileState state, CsDocument root, NodeEntry entry, int order, Counter counter, long[] ticks, Tally tally)
    {
        void Cause(string cause) => tally.Cause(cause);
        void Record(Violation violation) => tally.Violations.Add(violation);
        counter.Nodes++;
        string file = state.File, path = string.Join('.', entry.Path.Segments), text = state.Text;
        var located = entry.Envelope;
        if (located == null) counter.EnvelopeNone++;
        else if (located.How == "self") counter.EnvelopeSelf++;
        else counter.EnvelopeAncestor++;
        long t = Stopwatch.GetTimestamp();
        CsDocument edited;
        try
        {
            var fresh = entry.Node.Detach();
            edited = root.Replace(entry.Path, tally.Variant == Variant.Reformat ? fresh.Reformat() : fresh);
        }
        catch (Exception error)
        {
            counter.NotAddressable++;
            Cause("replace " + entry.Kind + " " + error.GetType().Name);
            Record(new("replace", file, order, path, entry.Kind, error.GetType().Name, 0, located?.Start ?? -1, located?.End ?? -1, Clip(error.Message), ""));
            return;
        }
        finally { ticks[0] += Stopwatch.GetTimestamp() - t; }
        t = Stopwatch.GetTimestamp();
        string output;
        try { output = edited.Render(); }
        catch (Exception error)
        {
            counter.RenderErrors++;
            Cause("render " + entry.Kind + " " + error.GetType().Name);
            Record(new("render", file, order, path, entry.Kind, error.GetType().Name, 0, located?.Start ?? -1, located?.End ?? -1, Clip(error.Message), ""));
            return;
        }
        finally { ticks[1] += Stopwatch.GetTimestamp() - t; }
        bool identical = string.Equals(output, text, StringComparison.Ordinal);
        if (identical) counter.Identical++;
        t = Stopwatch.GetTimestamp();
        if (located != null)
        {
            counter.P2Checked++;
            if (!identical && Locality(text, output, located.Start, located.End) is { } broken)
            {
                counter.P2Violations++;
                Cause("P2 " + entry.Kind + " " + broken.Cause);
                Record(new("P2", file, order, path, entry.Kind, broken.Cause, broken.Offset, located.Start, located.End,
                    Excerpt(text, broken.Offset), Excerpt(output, broken.OutputOffset)));
            }
        }
        ticks[2] += Stopwatch.GetTimestamp() - t;
        if (!entry.CheckP3) { counter.P3Sampled++; return; }
        counter.P3Checked++;
        // Parsing is deterministic, so an output equal to the source re-parses to the original tree.
        if (identical) { counter.P3Trivial++; return; }
        t = Stopwatch.GetTimestamp();
        ParseResult<CsDocument> reparsed;
        try { reparsed = Cs.Parse(output); }
        catch (Exception error)
        {
            counter.ReparseErrors++;
            counter.P3Violations++;
            Cause("P3 " + entry.Kind + " reparse " + error.GetType().Name);
            Record(new("P3", file, order, path, entry.Kind, "reparse " + error.GetType().Name, 0, located?.Start ?? -1, located?.End ?? -1, Clip(error.Message), ""));
            return;
        }
        finally { ticks[3] += Stopwatch.GetTimestamp() - t; }
        t = Stopwatch.GetTimestamp();
        string? cause = null, intended = "", actual = "", where = "";
        var original = state.Parsed;
        if (reparsed.Coverage != original.Coverage) (cause, intended, actual) = ("coverage", original.Coverage.ToString(), reparsed.Coverage.ToString());
        else if (!reparsed.Diagnostics.Select(d => d.Code).SequenceEqual(original.Diagnostics.Select(d => d.Code)))
            (cause, intended, actual) = ("diagnostics", Codes(original), Codes(reparsed));
        else if (GuardEquivalence.Compare(root, reparsed.Root) is { } divergence)
            (cause, intended, actual, where) = ("tree " + divergence.Pattern, divergence.Intended, divergence.Actual, divergence.Path);
        ticks[4] += Stopwatch.GetTimestamp() - t;
        if (cause == null) return;
        counter.P3Violations++;
        Cause("P3 " + entry.Kind + " " + cause);
        int at = Prefix(text, output);
        string at3 = where.Length == 0 ? "" : " at " + (where.Length <= 120 ? where : "..." + where[^117..]);
        Record(new("P3", file, order, path, entry.Kind, cause, at, located?.Start ?? -1, located?.End ?? -1,
            Clip(intended) + at3 + " | " + Excerpt(text, at), Clip(actual) + " | " + Excerpt(output, at)));
    }

    static string Codes(ParseResult<CsDocument> result) => string.Join(",", result.Diagnostics.Select(d => d.Code));

    internal sealed record Broken(string Cause, int Offset, int OutputOffset);

    // AD I4 P2. a = start of the space/tab run ending at the envelope start; b = end of the space/tab run starting at
    // the envelope end plus at most one line break. The output must start with source[0..a) and end with source[b..].
    internal static Broken? Locality(string source, string output, int start, int end)
    {
        int a = start;
        while (a > 0 && source[a - 1] is ' ' or '\t') a--;
        int b = end;
        while (b < source.Length && source[b] is ' ' or '\t') b++;
        if (b < source.Length && source[b] == '\r') b += b + 1 < source.Length && source[b + 1] == '\n' ? 2 : 1;
        else if (b < source.Length && source[b] == '\n') b++;
        int prefix = Prefix(source, output);
        int suffix = Suffix(source, output);
        int tail = source.Length - b;
        if (prefix >= a && suffix >= tail && output.Length >= a + tail) return null;
        string what = prefix < a ? "before" : suffix < tail ? "after" : "overlap";
        // The differing regions, with the common suffix clamped so it never overlaps the common prefix.
        int shared = Math.Min(suffix, Math.Min(source.Length, output.Length) - prefix);
        int sourceTo = source.Length - shared, outputTo = output.Length - shared;
        int offset = prefix < a ? prefix : Math.Max(0, sourceTo - 1);
        int outputOffset = prefix < a ? prefix : Math.Max(0, outputTo - 1);
        // Whitespace-only when the differing regions are equal once spaces, tabs and line breaks are removed.
        bool whitespace = Squeeze(source, prefix, sourceTo) == Squeeze(output, prefix, outputTo);
        return new(what + (whitespace ? " whitespace-only" : " text"), offset, outputOffset);
    }

    static string Squeeze(string text, int from, int to)
    {
        var builder = new StringBuilder();
        for (int i = Math.Max(0, from); i < Math.Min(to, text.Length); i++)
            if (!char.IsWhiteSpace(text[i])) builder.Append(text[i]);
        return builder.ToString();
    }

    static int Prefix(string left, string right) => left.AsSpan().CommonPrefixLength(right);

    static int Suffix(string left, string right)
    {
        int i = 0, max = Math.Min(left.Length, right.Length);
        while (i < max && left[left.Length - 1 - i] == right[right.Length - 1 - i]) i++;
        return i;
    }

    void Record(Violation violation) => violations.Add(violation);

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

    JsonObject Report(int fileCount, FileState[] states, List<Tally> tallies, TimeSpan wall, TimeSpan prepared)
    {
        var faithful = faithfulness.OrderBy(f => (string)f["file"]!, StringComparer.Ordinal).ToArray();
        int rendered = faithful.Count(f => f["renderError"] == null);
        int codeFaithful = faithful.Count(f => f["codeEditDistance"]?.GetValue<int>() == 0);
        int allFaithful = faithful.Count(f => f["allEditDistance"]?.GetValue<int>() == 0);
        long tokens = faithful.Sum(f => (long?)f["allTokens"]?.GetValue<int>() ?? 0);
        long freshTokens = faithful.Sum(f => (long?)f["allTokensFresh"]?.GetValue<int>() ?? 0);
        long distance = faithful.Sum(f => f["allEditDistance"]?.GetValue<int>() is int d and >= 0 ? d : 0);
        var p1 = violations.Where(v => v.Property is "P1" or "parse").OrderBy(v => v.File, StringComparer.Ordinal).ToArray();
        return new JsonObject
        {
            ["schema"] = 1,
            ["gate"] = "GENE-29 S0 corpus property gate",
            ["mode"] = "report-only",
            ["p3Mode"] = settings.P3 == P3Mode.Full ? "full" : "sampled",
            ["nodeMode"] = settings.SampleNodes ? "sampled 1-in-" + settings.NodeSample + " for tests/**, every kind at least 3 times per file" : "all",
            ["commit"] = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "",
            ["environment"] = new JsonObject
            {
                ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                ["processors"] = Environment.ProcessorCount,
                ["parallelism"] = settings.Parallelism,
                ["fileFilter"] = string.Join(";", settings.Files),
                ["shard"] = settings.Shard.Index + "/" + settings.Shard.Count
            },
            ["timing"] = new JsonObject
            {
                ["wallSeconds"] = Math.Round(wall.TotalSeconds, 1),
                ["prepareWallSeconds"] = Math.Round(prepared.TotalSeconds, 1),
                ["prepareCpuSeconds"] = Seconds(states.Sum(s => s.PrepareTicks)),
                ["variantWallSeconds"] = new JsonObject(tallies.Select(t => KeyValuePair.Create(Name(t.Variant), (JsonNode?)Seconds(t.WallTicks))))
            },
            ["corpus"] = new JsonObject
            {
                ["files"] = fileCount,
                ["parsed"] = states.Length,
                ["parseErrors"] = parseErrors,
                ["chars"] = states.Sum(s => (long)s.Text.Length),
                ["nodes"] = states.Sum(s => s.Nodes.Length),
                ["nodesSkippedBySampling"] = nodesSkipped
            },
            ["P1"] = new JsonObject
            {
                ["checked"] = p1Checked,
                ["violations"] = p1Violations,
                ["list"] = new JsonArray(p1.Select(Json).ToArray())
            },
            ["tokenFaithfulness"] = new JsonObject
            {
                ["definition"] = "a fresh render of the parsed model (Root.Detach().Render()) lexes to the source's tokens; code excludes comments, all includes comments and directive lines",
                ["files"] = faithful.Length,
                ["renderErrors"] = faithful.Length - rendered,
                ["codeFaithfulFiles"] = codeFaithful,
                ["allFaithfulFiles"] = allFaithful,
                ["codeFaithfulFileRate"] = faithful.Length == 0 ? 0 : Math.Round(100.0 * codeFaithful / faithful.Length, 2),
                ["allFaithfulFileRate"] = faithful.Length == 0 ? 0 : Math.Round(100.0 * allFaithful / faithful.Length, 2),
                ["allTokens"] = tokens,
                ["allTokensFresh"] = freshTokens,
                ["allEditDistance"] = distance,
                ["editDistanceLimitHits"] = faithful.Count(f => f["allEditDistance"]?.GetValue<int>() < 0),
                ["tokenAlignmentRate"] = tokens + freshTokens == 0 ? 0 : Math.Round(100.0 * (1 - (double)distance / (tokens + freshTokens)), 3),
                ["unfaithfulFiles"] = new JsonArray(faithful.Where(f => f["allEditDistance"]?.GetValue<int>() != 0).Select(f => f.DeepClone()).ToArray())
            },
            ["variants"] = new JsonObject(tallies.Select(t => KeyValuePair.Create(Name(t.Variant), (JsonNode?)VariantReport(t, states)))),
            ["gateErrors"] = new JsonArray(gateErrors.Order(StringComparer.Ordinal).Select(e => (JsonNode)Clip(e)).ToArray())
        };
    }

    static string Name(Variant variant) => variant.ToString().ToLowerInvariant();

    static JsonNode Json(Violation v) => new JsonObject
    {
        ["property"] = v.Property, ["file"] = v.File, ["path"] = v.Path, ["kind"] = v.Kind, ["cause"] = v.Cause,
        ["offset"] = v.Offset, ["envelope"] = new JsonArray(v.EnvelopeStart, v.EnvelopeEnd),
        ["source"] = v.Source, ["output"] = v.Output
    };

    static JsonObject VariantReport(Tally tally, FileState[] states)
    {
        var total = new Counter();
        foreach (var counter in tally.ByKind.Values) total.Add(counter);
        var examples = tally.Violations.OrderBy(v => v.Property, StringComparer.Ordinal).ThenBy(v => v.File, StringComparer.Ordinal)
            .ThenBy(v => v.Order).GroupBy(v => (v.Property, v.Kind)).SelectMany(g => g.Take(ExamplesPerPropertyAndKind)).ToArray();
        return new JsonObject
        {
            ["replacement"] = tally.Variant == Variant.Detach ? "node.Detach()" : "node.Detach().Reformat()",
            ["timing"] = new JsonObject
            {
                ["wallSeconds"] = Seconds(tally.WallTicks),
                ["cpuSecondsByPhase"] = new JsonObject(Phases.Select((name, i) => KeyValuePair.Create(name, (JsonNode?)Seconds(tally.PhaseTicks[i])))),
                ["slowestFiles"] = new JsonArray(tally.FileTicks.OrderByDescending(f => f.Value).Take(15).Select(f =>
                {
                    var state = states.First(s => s.File == f.Key);
                    return (JsonNode)new JsonObject { ["file"] = f.Key, ["chars"] = state.Text.Length, ["nodes"] = state.Nodes.Length, ["cpuSeconds"] = Seconds(f.Value) };
                }).ToArray())
            },
            ["totals"] = new JsonObject
            {
                ["nodes"] = total.Nodes,
                ["identicalOutput"] = total.Identical,
                ["P2"] = new JsonObject { ["checked"] = total.P2Checked, ["violations"] = total.P2Violations, ["unlocated"] = total.EnvelopeNone },
                ["P3"] = new JsonObject
                {
                    ["checked"] = total.P3Checked, ["violations"] = total.P3Violations, ["trivialIdenticalOutput"] = total.P3Trivial,
                    ["skippedBySampling"] = total.P3Sampled, ["reparseErrors"] = total.ReparseErrors
                },
                ["notAddressable"] = total.NotAddressable,
                ["renderErrors"] = total.RenderErrors,
                ["envelope"] = new JsonObject { ["self"] = total.EnvelopeSelf, ["ancestor"] = total.EnvelopeAncestor, ["none"] = total.EnvelopeNone }
            },
            ["causes"] = new JsonObject(tally.Causes.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => KeyValuePair.Create(c.Key, (JsonNode?)c.Value))),
            ["byKind"] = new JsonObject(tally.ByKind.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => KeyValuePair.Create(k.Key, (JsonNode?)k.Value.Json()))),
            ["byFile"] = new JsonObject(tally.ByFile.Where(f => f.Value.P2Violations + f.Value.P3Violations + f.Value.NotAddressable + f.Value.RenderErrors != 0)
                .OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value.Json()))),
            ["violationsListed"] = examples.Length,
            ["violationsListedLimitPerPropertyAndKind"] = ExamplesPerPropertyAndKind,
            ["violations"] = new JsonArray(examples.Select(Json).ToArray())
        };
    }

    internal static void Write(JsonObject report, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
