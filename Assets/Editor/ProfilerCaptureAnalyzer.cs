using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

// Dumps whatever the Profiler window currently holds (or a saved .raw/.data capture) into a
// plain-text report: frame-time stats, CPU/GPU-bound verdict, top markers by self time, top
// GC allocators, and a breakdown of the worst frames. Written for handing profiler results
// to tooling/review without screenshotting the Profiler UI.
//
// Usage: record in the Profiler (play mode, ~10-15s), then Tools > Performance >
// Analyze Current Profiler Data. Report lands in <project>/ProfilerReports/.
public static class ProfilerCaptureAnalyzer
{
    const int MaxFramesAnalyzed = 3000;
    const int TopMarkerCount = 50;
    const int TopGcCount = 25;
    const int WorstFrameCount = 8;
    const int WorstFrameMarkerCount = 10;

    // Markers that are idle/wait time, not real work. Separated out so "what is slow" isn't
    // drowned by "waiting for vsync". EditorLoop is editor overhead absent from builds.
    static readonly string[] WaitMarkers =
    {
        "Gfx.WaitForPresentOnGfxThread",
        "Gfx.WaitForPresent",
        "Gfx.WaitForRenderThread",
        "WaitForTargetFPS",
        "EditorLoop",
        "Semaphore.WaitForSignal",
        "PlayerLoop.WaitForLastPresentationAndUpdateTime",
    };

    class MarkerStat
    {
        public double selfMs;
        public double gcBytes;
        public double calls;
    }

    [MenuItem("Tools/Performance/Analyze Current Profiler Data")]
    public static void AnalyzeCurrent()
    {
        Analyze();
    }

    [MenuItem("Tools/Performance/Analyze Profiler Capture File...")]
    public static void AnalyzeFile()
    {
        string path = EditorUtility.OpenFilePanel("Open profiler capture", "", "raw,data");
        if (string.IsNullOrEmpty(path)) return;
        if (!ProfilerDriver.LoadProfile(path, false))
        {
            EditorUtility.DisplayDialog("Profiler Capture Analyzer",
                "Failed to load capture:\n" + path, "OK");
            return;
        }
        Analyze();
    }

    static void Analyze()
    {
        int first = ProfilerDriver.firstFrameIndex;
        int last = ProfilerDriver.lastFrameIndex;
        if (first < 0 || last < first)
        {
            EditorUtility.DisplayDialog("Profiler Capture Analyzer",
                "No profiler data loaded. Record something in the Profiler window first " +
                "(or load a saved capture via Tools > Performance > Analyze Profiler Capture File...).",
                "OK");
            return;
        }

        // Analyze the most recent frames if the capture is huge.
        if (last - first + 1 > MaxFramesAnalyzed)
            first = last - MaxFramesAnalyzed + 1;

        var markerStats = new Dictionary<string, MarkerStat>(1024);
        var frameTimes = new List<(int frameIndex, float ms)>(last - first + 1);
        var frameGc = new List<double>(last - first + 1);
        var childBuffer = new List<int>(64);
        var walkStack = new Stack<int>(256);

        try
        {
            int total = last - first + 1;
            for (int f = first; f <= last; f++)
            {
                if ((f - first) % 50 == 0)
                    EditorUtility.DisplayProgressBar("Analyzing profiler capture",
                        $"Frame {f - first + 1}/{total}", (f - first) / (float)total);

                using (var view = ProfilerDriver.GetHierarchyFrameDataView(
                    f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    HierarchyFrameDataView.columnDontSort, false))
                {
                    if (view == null || !view.valid) continue;

                    frameTimes.Add((f, view.frameTimeMs));

                    double gcThisFrame = 0;
                    walkStack.Clear();
                    walkStack.Push(view.GetRootItemID());
                    while (walkStack.Count > 0)
                    {
                        int id = walkStack.Pop();
                        childBuffer.Clear();
                        view.GetItemChildren(id, childBuffer);
                        for (int c = 0; c < childBuffer.Count; c++)
                            walkStack.Push(childBuffer[c]);

                        if (id == view.GetRootItemID()) continue;

                        string name = view.GetItemName(id);
                        if (!markerStats.TryGetValue(name, out MarkerStat stat))
                        {
                            stat = new MarkerStat();
                            markerStats.Add(name, stat);
                        }
                        stat.selfMs += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                        double gc = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
                        stat.gcBytes += gc;
                        gcThisFrame += gc;
                        stat.calls += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
                    }
                    frameGc.Add(gcThisFrame);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (frameTimes.Count == 0)
        {
            EditorUtility.DisplayDialog("Profiler Capture Analyzer",
                "Loaded data contained no readable main-thread frames.", "OK");
            return;
        }

        string report = BuildReport(markerStats, frameTimes, frameGc);

        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ProfilerReports");
        Directory.CreateDirectory(dir);
        string reportPath = Path.Combine(dir, $"profiler_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(reportPath, report);

        Debug.Log($"[ProfilerCaptureAnalyzer] Report written to: {reportPath}");
        EditorUtility.RevealInFinder(reportPath);
    }

    static string BuildReport(Dictionary<string, MarkerStat> markerStats,
        List<(int frameIndex, float ms)> frameTimes, List<double> frameGc)
    {
        var sb = new StringBuilder(64 * 1024);
        int frameCount = frameTimes.Count;

        var sortedTimes = frameTimes.Select(t => t.ms).OrderBy(t => t).ToList();
        float avg = sortedTimes.Sum() / frameCount;
        float median = sortedTimes[frameCount / 2];
        float p95 = sortedTimes[Mathf.Min(frameCount - 1, Mathf.FloorToInt(frameCount * 0.95f))];
        float worst = sortedTimes[frameCount - 1];
        double avgGc = frameGc.Count > 0 ? frameGc.Average() : 0;

        sb.AppendLine("PROFILER CAPTURE REPORT");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Frames analyzed: {frameCount} (main thread)");
        sb.AppendLine();
        sb.AppendLine("== FRAME TIME ==");
        sb.AppendLine($"  avg    {avg,8:F2} ms  ({1000f / Mathf.Max(0.01f, avg):F1} FPS)");
        sb.AppendLine($"  median {median,8:F2} ms  ({1000f / Mathf.Max(0.01f, median):F1} FPS)");
        sb.AppendLine($"  p95    {p95,8:F2} ms  ({1000f / Mathf.Max(0.01f, p95):F1} FPS)");
        sb.AppendLine($"  worst  {worst,8:F2} ms  ({1000f / Mathf.Max(0.01f, worst):F1} FPS)");
        sb.AppendLine($"  GC alloc avg/frame: {avgGc / 1024.0:F1} KB");
        sb.AppendLine();

        // CPU vs GPU verdict from how the main thread spends its time.
        double waitMs = 0, editorMs = 0, gfxWaitMs = 0, targetFpsMs = 0;
        foreach (string w in WaitMarkers)
        {
            if (!markerStats.TryGetValue(w, out MarkerStat s)) continue;
            waitMs += s.selfMs;
            if (w == "EditorLoop") editorMs += s.selfMs;
            else if (w.StartsWith("Gfx.")) gfxWaitMs += s.selfMs;
            else if (w == "WaitForTargetFPS") targetFpsMs += s.selfMs;
        }
        double totalMs = sortedTimes.Sum();
        double workMs = Math.Max(0, totalMs - waitMs);
        sb.AppendLine("== WHERE THE MAIN THREAD SPENDS ITS TIME ==");
        sb.AppendLine($"  real CPU work            {workMs / frameCount,8:F2} ms/frame");
        sb.AppendLine($"  waiting on GPU (Gfx.*)   {gfxWaitMs / frameCount,8:F2} ms/frame");
        sb.AppendLine($"  vsync/target FPS wait    {targetFpsMs / frameCount,8:F2} ms/frame");
        sb.AppendLine($"  editor overhead          {editorMs / frameCount,8:F2} ms/frame (absent in builds)");
        string verdict;
        if (gfxWaitMs > workMs)
            verdict = "GPU-BOUND: the CPU spends more time waiting for the GPU than working. Optimize rendering (fullscreen effects, overdraw, shadows, resolution) before touching scripts.";
        else if (targetFpsMs / frameCount > 2.0)
            verdict = "FRAMERATE-CAPPED: significant time is spent waiting on vsync/targetFrameRate. Actual headroom exists; check Application.targetFrameRate / vsync settings before optimizing.";
        else
            verdict = "CPU-BOUND: main-thread work dominates. The top-markers list below is the optimization target.";
        sb.AppendLine($"  VERDICT: {verdict}");
        sb.AppendLine();

        sb.AppendLine($"== TOP {TopMarkerCount} MARKERS BY SELF TIME (avg ms/frame) ==");
        sb.AppendLine($"  {"avg ms",10}  {"calls/frm",10}  marker");
        foreach (var kv in markerStats.OrderByDescending(kv => kv.Value.selfMs).Take(TopMarkerCount))
            sb.AppendLine($"  {kv.Value.selfMs / frameCount,10:F3}  {kv.Value.calls / frameCount,10:F1}  {kv.Key}");
        sb.AppendLine();

        sb.AppendLine($"== TOP {TopGcCount} GC ALLOCATORS (avg KB/frame) ==");
        var gcSources = markerStats.Where(kv => kv.Value.gcBytes > 0)
            .OrderByDescending(kv => kv.Value.gcBytes).Take(TopGcCount).ToList();
        if (gcSources.Count == 0) sb.AppendLine("  (no GC allocations recorded)");
        foreach (var kv in gcSources)
            sb.AppendLine($"  {kv.Value.gcBytes / 1024.0 / frameCount,10:F3}  {kv.Key}");
        sb.AppendLine();

        sb.AppendLine($"== {WorstFrameCount} WORST FRAMES ==");
        var worstFrames = frameTimes.OrderByDescending(t => t.ms).Take(WorstFrameCount).ToList();
        var childBuffer = new List<int>(64);
        var walkStack = new Stack<int>(256);
        foreach (var (frameIndex, ms) in worstFrames)
        {
            sb.AppendLine($"  Frame {frameIndex}: {ms:F2} ms");
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(
                frameIndex, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnDontSort, false))
            {
                if (view == null || !view.valid) continue;
                var frameMarkers = new Dictionary<string, double>(256);
                walkStack.Clear();
                walkStack.Push(view.GetRootItemID());
                while (walkStack.Count > 0)
                {
                    int id = walkStack.Pop();
                    childBuffer.Clear();
                    view.GetItemChildren(id, childBuffer);
                    for (int c = 0; c < childBuffer.Count; c++)
                        walkStack.Push(childBuffer[c]);
                    if (id == view.GetRootItemID()) continue;

                    string name = view.GetItemName(id);
                    frameMarkers.TryGetValue(name, out double self);
                    frameMarkers[name] = self + view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                }
                foreach (var kv in frameMarkers.OrderByDescending(kv => kv.Value).Take(WorstFrameMarkerCount))
                    sb.AppendLine($"      {kv.Value,8:F2} ms  {kv.Key}");
            }
        }

        return sb.ToString();
    }
}
