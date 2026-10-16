using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Headless;
using Avalonia.Threading;
using TheOne.Avalonia.Controls;

namespace TheOne.DesignPreview;

internal static partial class CodeEditorPreview
{
    internal static void Contention(Action<bool, string> check, string? output)
    {
        var text = string.Join('\n',
            Enumerable.Range(1, 10000).Select(i =>
                $"var item{i:D5} = new {{ Name = \"代码行 {i}\", Value = {i} }}; // 10000-line benchmark"));
        var frames = new List<FrameSample>(1600);
        var work = new ConcurrentQueue<CodeWorkProbe.Sample>();
        var failures = new List<string>();
        var activeAnalysis = 0;
        var activeCompletion = 0;
        var maxAnalysis = 0;
        CodeWorkProbe.Observer = sample =>
        {
            work.Enqueue(sample);
            if (sample.Operation == "analysis")
            {
                var active = Interlocked.Add(ref activeAnalysis, sample.Starting ? 1 : -1);
                var observed = Volatile.Read(ref maxAnalysis);
                while (active > observed)
                {
                    var prior = Interlocked.CompareExchange(ref maxAnalysis, active, observed);
                    if (prior == observed) break;
                    observed = prior;
                }
            }

            if (sample.Operation == "completion") Interlocked.Add(ref activeCompletion, sample.Starting ? 1 : -1);
        };

        void Pulse(int milliseconds)
        {
            var end = Stopwatch.GetTimestamp() + milliseconds * Stopwatch.Frequency / 1000;
            while (Stopwatch.GetTimestamp() < end)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
        }

        void Require(bool condition, string message)
        {
            if (!condition) failures.Add(message);
        }

        try
        {
            foreach (var scenario in new[]
                     {
                         "services-off", "cold-analysis", "cancel-typing", "resume-typing", "warm-completion",
                         "wrapped-analysis"
                     })
            {
                var off = scenario == "services-off";
                var editor = new CodeEditor
                {
                    CodeType = CodeType.CSharp, Text = text + "\nitem09", IsDiagnosticsEnabled = !off,
                    IsCompletionEnabled = !off, IsSignatureHelpEnabled = !off, IsSemanticHighlightingEnabled = !off
                };
                var window = Open(editor, scenario == "wrapped-analysis" ? 600 : 1000, 700);
                var pending = new List<Task>();
                try
                {
                    editor.WordWrap = scenario == "wrapped-analysis";
                    editor.FocusEditor();
                    editor.CaretIndex = editor.Document.Lines[5000].Start + 4;
                    Flush();
                    if (scenario == "warm-completion")
                    {
                        Pump(editor.RefreshDiagnosticsAsync());
                        editor.CaretIndex = editor.Text.Length;
                        Pump(editor.RequestCompletionAsync());
                        editor.CloseCompletion();
                    }
                    else if (!off)
                    {
                        pending.Add(editor.RefreshDiagnosticsAsync());
                    }

                    void Frame(string operation, int index, Action action)
                    {
                        var bytes = GC.GetAllocatedBytesForCurrentThread();
                        var processBytes = GC.GetTotalAllocatedBytes();
                        var gen0 = GC.CollectionCount(0);
                        var gen1 = GC.CollectionCount(1);
                        var gen2 = GC.CollectionCount(2);
                        var pause = GC.GetTotalPauseDuration();
                        var analysis = Volatile.Read(ref activeAnalysis);
                        var completion = Volatile.Read(ref activeCompletion);
                        var start = Stopwatch.GetTimestamp();
                        action();
                        Flush();
                        var end = Stopwatch.GetTimestamp();
                        frames.Add(new FrameSample(scenario, operation, index, start, end,
                            GC.GetAllocatedBytesForCurrentThread() - bytes,
                            GC.GetTotalAllocatedBytes() - processBytes, GC.CollectionCount(0) - gen0,
                            GC.CollectionCount(1) - gen1,
                            GC.CollectionCount(2) - gen2, (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
                            editor.View.LastShapeMilliseconds, editor.View.LastRenderMilliseconds, analysis,
                            completion));
                    }

                    var count = scenario is "cancel-typing" or "resume-typing" ? 40 : 90;
                    for (var i = 0; i < count; i++)
                    {
                        if (scenario == "resume-typing")
                        {
                            var wait = Stopwatch.StartNew();
                            while (Volatile.Read(ref activeAnalysis) == 0 && wait.ElapsedMilliseconds < 3000) Pulse(1);
                            Require(Volatile.Read(ref activeAnalysis) == 1,
                                "CodeEditor/contention: resumed input overlaps an actual running snapshot");
                        }

                        if (scenario is "services-off" or "cancel-typing" or "resume-typing")
                            Frame("input", i, () => window.KeyTextInput("x"));
                        if (scenario is "cancel-typing" or "resume-typing")
                            pending.Add(editor.RefreshDiagnosticsAsync());
                        if (scenario == "warm-completion") pending.Add(editor.RequestCompletionAsync());
                        Frame("scroll", i, () => editor.ScrollToLine(1 + i * 109 % 9900));
                        Pulse(scenario == "cancel-typing" ? 40 : 8);
                    }

                    foreach (var task in pending) Pump(task);
                    editor.CloseCompletion();
                    if (!off)
                        Require(Pump(editor.RefreshDiagnosticsAsync()),
                            $"CodeEditor/contention: {scenario} publishes the latest revision");
                    if (scenario is "cancel-typing" or "resume-typing")
                    {
                        var obsolete = pending.OfType<Task<bool>>().Count(t => t.IsCompletedSuccessfully && !t.Result);
                        Console.WriteLine($"REQUESTS: {scenario}: issued={pending.Count}, obsolete={obsolete}");
                        Require(obsolete > 0, "CodeEditor/contention: obsolete typing snapshots are canceled");
                    }

                    Console.WriteLine($"MEASURE: {scenario}: frames={frames.Count(f => f.Scenario == scenario)}");
                }
                finally
                {
                    window.Close();
                    Flush();
                }
            }
        }
        finally
        {
            CodeWorkProbe.Observer = null;
            if (output != null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllLines(Path.Combine(output, "frames.csv"), new[]
                    {
                        "scenario,operation,index,start_ticks,end_ticks,ms,ui_bytes,process_bytes,gen0,gen1,gen2,pause_ms,shape_ms,render_ms,analysis_active,completion_active"
                    }
                    .Concat(frames.Select(f => FormattableString.Invariant(
                        $"{f.Scenario},{f.Operation},{f.Index},{f.Start},{f.End},{f.Milliseconds:F3},{f.UiBytes},{f.ProcessBytes},{f.Gen0},{f.Gen1},{f.Gen2},{f.PauseMs:F3},{f.ShapeMs:F3},{f.RenderMs:F3},{f.AnalysisActive},{f.CompletionActive}"))));
                File.WriteAllLines(Path.Combine(output, "work.csv"), new[]
                        { "id,parent,operation,starting,start_ticks,duration_ms,allocated_bytes,canceled,thread" }
                    .Concat(work.Select(w =>
                        FormattableString.Invariant(
                            $"{w.Id},{w.Parent},{w.Operation},{w.Starting},{w.Timestamp},{w.ElapsedTicks * 1000d / Stopwatch.Frequency:F3},{w.AllocatedBytes},{w.Canceled},{w.Thread}"))));
                File.WriteAllText(Path.Combine(output, "host.txt"),
                    $"{RuntimeInformation.OSDescription}\n{RuntimeInformation.FrameworkDescription}\nClock frequency: {Stopwatch.Frequency}\nUTF16: {text.Length}\nNo forced GC; normal runtime GC settings.\n");
            }
        }

        foreach (var group in frames.Where(f => f.Index >= 5).GroupBy(f => (f.Scenario, f.Operation)))
        {
            var samples = group.ToArray();
            var p95 = Percentile(samples.Select(f => f.Milliseconds).ToList(), .95);
            var message =
                $"CodeEditor/contention: {group.Key.Scenario}/{group.Key.Operation} p95 {p95:F2}ms, max {samples.Max(f => f.Milliseconds):F2}ms; GC {samples.Sum(f => f.Gen0)}/{samples.Sum(f => f.Gen1)}/{samples.Sum(f => f.Gen2)}, pauses {samples.Sum(f => f.PauseMs):F2}ms; active analysis frames {samples.Count(f => f.AnalysisActive > 0)}, completion frames {samples.Count(f => f.CompletionActive > 0)}";
            Console.WriteLine("MEASURE: " + message);
            Require(p95 < 16.7, message);
        }

        foreach (var group in work.Where(w => !w.Starting).GroupBy(w => w.Operation))
            Console.WriteLine(
                $"WORK: {group.Key}: count={group.Count()}, canceled={group.Count(w => w.Canceled)}, allocated={group.Sum(w => w.AllocatedBytes) / 1048576d:F2}MiB, p95={Percentile(group.Select(w => w.ElapsedTicks * 1000d / Stopwatch.Frequency).ToList(), .95):F2}ms");
        Require(work.Any(w => w.Operation == "analysis" && !w.Starting),
            "CodeEditor/contention: actual background analysis was observed");
        Require(work.Any(w => w.Operation == "completion" && !w.Starting),
            "CodeEditor/contention: actual semantic completion was observed");
        Require(work.Any(w => w.Operation == "analysis" && !w.Starting && w.Canceled),
            "CodeEditor/contention: running snapshots acknowledge cancellation");
        Require(maxAnalysis == 1,
            $"CodeEditor/contention: maximum concurrent expensive snapshots per editor was {maxAnalysis}, expected 1");
        Console.WriteLine($"SCHEDULER: maximum active snapshots={maxAnalysis}");
        check(failures.Count == 0,
            failures.Count == 0
                ? "CodeEditor/contention: all input/scroll p95 measurements stay below the unchanged 16.7ms threshold"
                : string.Join("\n", failures));
    }

    private readonly record struct FrameSample(
        string Scenario,
        string Operation,
        int Index,
        long Start,
        long End,
        long UiBytes,
        long ProcessBytes,
        int Gen0,
        int Gen1,
        int Gen2,
        double PauseMs,
        double ShapeMs,
        double RenderMs,
        int AnalysisActive,
        int CompletionActive)
    {
        internal double Milliseconds => (End - Start) * 1000d / Stopwatch.Frequency;
    }
}