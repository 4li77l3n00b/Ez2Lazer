// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Mods.LAsMods;
using osu.Game.Rulesets.Mania.Objects;

namespace osu.Game.Rulesets.Mania.Mods.KrrConversion
{
    public static class KrrLNConverter
    {
        // Simplified replacements for BeatNumberGenerator
        private const double border_coefficient = 1.0 / 4.0;
        private const int border_middle_index = 64;

        private const double short_coefficient = 1.0 / 16.0;
        private const int short_middle_index = 256;

        // Apply modifications to beatmap
        private const double min_gap = 20.0; // ms gap to avoid overlap
        private const double min_len = 30.0;

        public static double GetBorderValue(int idx)
        {
            if (idx <= 0) return 0.0;
            if (idx >= border_middle_index + 1) return 999.0;

            return idx * border_coefficient;
        }

        public static double GetShortValue(int idx)
        {
            if (idx <= 0) return 0.0;
            if (idx >= short_middle_index + 1) return 999.0;

            return idx * short_coefficient;
        }

        // Full port using matrix-based pipeline adapted from krrTools
        public static void Transform(ManiaBeatmap beatmap, KrrLNOptions options)
        {
            int pLevel = options.Level;
            int pSeed = options.Seed ?? 114514;

            int seedValue = options.Seed ?? KrrConversionHelper.ComputeSeedFromBeatmap(beatmap);
            var rg = new Random(seedValue);
            var osc = new Oscillator(seedValue, 1.0 / 16.0);

            int cs = Math.Max(1, beatmap.TotalColumns);

            // precompute sorted global start times for efficient next-global lookup
            double[] allStartTimes = beatmap.HitObjects.Select(h => h.StartTime).OrderBy(t => t).ToArray();

            double getNextGlobalStart(double s)
            {
                int idx = Array.BinarySearch(allStartTimes, s);
                if (idx >= 0) // exact match -> next one
                    idx++;
                else
                    idx = ~idx; // first greater
                if (idx >= 0 && idx < allStartTimes.Length) return allStartTimes[idx];

                return s + 2000.0;
            }

            // Prepare per-column index lists (only consider ManiaHitObject-derived objects)
            var columns = new List<List<int>>();
            for (int i = 0; i < cs; i++) columns.Add(new List<int>());

            for (int i = 0; i < beatmap.HitObjects.Count; i++)
            {
                var ho = beatmap.HitObjects[i];
                // Skip existing LN if user chose not to process originals
                if (!options.ProcessOriginalIsChecked && ho is HoldNote) continue;

                int col = Math.Clamp(ho.Column, 0, cs - 1);
                columns[col].Add(i);
            }

            int borderKey = options.LengthThreshold;

            double longLevel = Math.Clamp(options.Level * 10.0, 0, 100);
            double shortLevel = GetShortValue(Math.Max(0, options.Level));

            // Collect candidate LN modifications (index -> newLength)
            var longCandidates = new List<(int index, double start, int length)>();
            var shortCandidates = new List<(int index, double start, int length)>();

            for (int c = 0; c < cs; c++)
            {
                var list = columns[c];
                list.Sort((a, b) => beatmap.HitObjects[a].StartTime.CompareTo(beatmap.HitObjects[b].StartTime));

                for (int j = 0; j < list.Count; j++)
                {
                    int idx = list[j];
                    var ho = beatmap.HitObjects[idx];
                    double start = ho.StartTime;
                    // compute available time conservatively: consider next in same column, next global start, and any overlapping hold end
                    double nextSameColStart = j + 1 < list.Count ? beatmap.HitObjects[list[j + 1]].StartTime : double.MaxValue;
                    double nextGlobalStart = getNextGlobalStart(start);
                    double nextHoldEnd = double.MaxValue;
                    // nearest end time of any following HoldNote in any column (conservative)
                    double nextAnyHoldEnd = double.MaxValue;
                    var laterHolds = beatmap.HitObjects.Skip(j + 1).OfType<HoldNote>().Select(h => h.EndTime);
                    if (laterHolds.Any()) nextAnyHoldEnd = laterHolds.Min();

                    // if next object in column is a hold, consider its end
                    if (j + 1 < list.Count)
                    {
                        if (beatmap.HitObjects[list[j + 1]] is HoldNote nextHo) nextHoldEnd = nextHo.EndTime;
                    }

                    int available = (int)Math.Max(0, Math.Min(nextSameColStart, Math.Min(nextGlobalStart, Math.Min(nextHoldEnd, nextAnyHoldEnd))) - start);

                    double beatLen = beatmap.ControlPointInfo.TimingPointAt(ho.StartTime).BeatLength;
                    double borderValue = GetBorderValue(borderKey);
                    double borderTime = borderValue * beatLen;

                    bool candidateLong = available > borderTime;

                    if (candidateLong)
                    {
                        double mean = available * longLevel / 100.0;
                        double di = borderTime;
                        int newLength;
                        if (mean < di)
                            newLength = GenerateRandom(0, di, (int)(shortLevel * beatLen), options.LongRandom, rg, osc);
                        else
                            newLength = GenerateRandom(di, available, mean, options.LongRandom, rg, osc);
                        if (newLength > available - 34) newLength = Math.Max(0, available - 34);
                        if (newLength > 0) longCandidates.Add((idx, start, newLength));
                    }
                    else
                    {
                        int newLength = GenerateRandom(0, (int)borderTime, (int)(shortLevel * beatLen), options.ShortRandom, rg, osc);
                        if (newLength > available - 34) newLength = Math.Max(0, available - 34);
                        if (newLength > 0) shortCandidates.Add((idx, start, newLength));
                    }
                }
            }

            // Apply percentage filters (deterministic scoring using RNG + oscillator)
            longCandidates = MarkByPercentagePerGroup(longCandidates, options.LongPercentage, rg, osc);
            shortCandidates = MarkByPercentagePerGroup(shortCandidates, options.ShortPercentage, rg, osc);

            // Enforce per-start_time limits
            Func<List<(int index, double start, int length)>, int, List<(int index, double start, int length)>> enforceLimit = (list, limit) =>
            {
                if (limit <= 0) return new List<(int, double, int)>();

                var grouped = list.GroupBy(x => Math.Round(x.start, 3)).ToList();
                var outList = new List<(int, double, int)>();

                foreach (var g in grouped)
                {
                    var items = g.ToList();

                    if (items.Count <= limit) outList.AddRange(items);
                    else
                    {
                        // deterministic random selection
                        items = items.OrderBy(x => rg.Next()).Take(limit).ToList();
                        outList.AddRange(items);
                    }
                }

                return outList;
            };

            longCandidates = enforceLimit(longCandidates, Math.Max(0, options.LongLimit));
            shortCandidates = enforceLimit(shortCandidates, Math.Max(0, options.ShortLimit));

            // Merge long first so longs take precedence if conflict
            var reserved = new HashSet<int>(longCandidates.Select(x => x.index));
            var merged = new List<(int index, double start, int length)>();
            merged.AddRange(longCandidates);
            merged.AddRange(shortCandidates.Where(x => !reserved.Contains(x.index)));

            // Apply alignment if requested
            if (options.Alignment.HasValue)
                ApplyAlignmentToCandidates(merged, beatmap, options.Alignment.Value);

            foreach (var c in merged)
            {
                int idx = c.index;
                if (idx < 0 || idx >= beatmap.HitObjects.Count) continue;

                var original = beatmap.HitObjects[idx];

                // determine next event start in same column or global to avoid overlap
                double nextSameCol = double.MaxValue;

                for (int k = idx + 1; k < beatmap.HitObjects.Count; k++)
                {
                    if (beatmap.HitObjects[k].Column == original.Column)
                    {
                        nextSameCol = beatmap.HitObjects[k].StartTime;
                        break;
                    }
                }

                double nextGlobal = beatmap.HitObjects.FirstOrDefault(h => h.StartTime > original.StartTime)?.StartTime ?? double.MaxValue;
                double limit = Math.Min(nextSameCol, nextGlobal) - min_gap;

                double desiredEnd = original.StartTime + c.length;
                double newEnd = Math.Min(desiredEnd, limit);
                if (newEnd - original.StartTime < min_len) continue; // too short, skip

                if (original is HoldNote hn)
                    hn.EndTime = newEnd;
                else
                {
                    var newHold = new HoldNote
                    {
                        StartTime = original.StartTime,
                        Column = original.Column,
                        Samples = original.Samples?.ToList()
                    };
                    newHold.EndTime = newEnd;
                    beatmap.HitObjects[idx] = newHold;
                }
            }

            // metadata handling intentionally omitted (framework will manage metadata/setting UI)
        }

        public static int GenerateRandom(double dBar, double uBar, double mBar, int pBar, Random r, Oscillator? osc)
        {
            if (pBar <= 0) return (int)mBar;

            if (pBar >= 100) pBar = 100;

            double p = pBar / 100.0;
            double d = mBar - (mBar - dBar) * p;
            double u = mBar + (uBar - mBar) * p;

            d = Math.Max(d, dBar);
            u = Math.Min(u, uBar);

            if (d >= u)
                return (int)mBar;

            // use a more peaked beta-like distribution by averaging five uniforms
            double betaRandom = (r.NextDouble() + r.NextDouble() + r.NextDouble() + r.NextDouble() + r.NextDouble()) / 5.0;

            // small periodic bias from oscillator to reproduce subtle periodic patterns
            if (osc != null)
            {
                double bias = (osc.Next() - 0.5) * 0.04; // ±0.02 bias
                betaRandom = Math.Clamp(betaRandom + bias, 0.0, 1.0);
            }

            double range = u - d;
            double mRelative = (mBar - d) / range;
            double result;
            if (betaRandom <= 0.5)
                result = d + mRelative * (betaRandom / 0.5) * range;
            else
                result = d + (mRelative + (1 - mRelative) * ((betaRandom - 0.5) / 0.5)) * range;

            int rounded = (int)Math.Round(result);
            rounded = Math.Max((int)dBar, Math.Min((int)uBar, rounded));
            return rounded;
        }

        public static List<(int index, double start, int length)> MarkByPercentagePerGroup(List<(int index, double start, int length)> list, double percentage, Random r, Oscillator osc)
        {
            if (percentage >= 100) return list;
            if (percentage <= 0) return new List<(int, double, int)>();

            var grouped = list.GroupBy(x => Math.Round(x.start, 3)).ToList();
            var outList = new List<(int index, double start, int length)>();

            foreach (var g in grouped)
            {
                var items = g.ToList();
                int keep = (int)Math.Round(items.Count * (percentage / 100.0));
                keep = Math.Max(0, Math.Min(keep, items.Count));

                if (keep == items.Count) outList.AddRange(items);
                else if (keep > 0)
                {
                    // Score each candidate deterministically using RNG and oscillator to bias selection
                    var scored = items.Select(it => new { it.index, it.start, it.length, score = r.NextDouble() * 0.85 + osc.Next() * 0.15 }).OrderByDescending(x => x.score).Take(keep)
                                      .Select(x => (x.index, x.start, x.length));
                    outList.AddRange(scored);
                }
            }

            return outList;
        }

        public static void ApplyAlignmentToCandidates(List<(int index, double start, int length)> list, ManiaBeatmap beatmap, int alignmentIndex)
        {
            // alignmentIndex: 1..8 roughly mapping to 1/8..1/1 fractions
            var alignMap = new Dictionary<int, double> { { 1, 1.0 / 8 }, { 2, 1.0 / 7 }, { 3, 1.0 / 6 }, { 4, 1.0 / 5 }, { 5, 1.0 / 4 }, { 6, 1.0 / 3 }, { 7, 1.0 / 2 }, { 8, 1.0 } };
            if (!alignMap.TryGetValue(alignmentIndex, out double alignValue)) return;

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var ho = beatmap.HitObjects[item.index];
                double beatLen = beatmap.ControlPointInfo.TimingPointAt(ho.StartTime).BeatLength;
                double denom = beatLen * alignValue;
                if (denom <= 0.5) continue;

                int aligned = (int)(Math.Round(item.length / denom) * denom);
                if (aligned < 30) aligned = item.length; // keep if too small after rounding
                list[i] = (item.index, item.start, aligned);
            }
        }
    }
}
