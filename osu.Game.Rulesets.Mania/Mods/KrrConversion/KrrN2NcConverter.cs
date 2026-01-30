using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Mods.LAsMods;
using osu.Game.Rulesets.Mania.Objects;

namespace osu.Game.Rulesets.Mania.Mods.KrrConversion
{
    public static class KrrN2NcConverter
    {
        public static void Transform(ManiaBeatmap beatmap, KrrOptions? options)
        {
            int targetKeys = options?.TargetKeys ?? beatmap.TotalColumns;
            int maxKeys    = options?.MaxKeys    ?? targetKeys;
            int minKeys    = options?.MinKeys    ?? 1;
            int seedValue  = options?.Seed       ?? KrrConversionHelper.ComputeSeedFromBeatmap(beatmap);

            // 推断原始键数
            int originalKeys = KrrConversionHelper.InferOriginalKeys(beatmap, targetKeys);
            if (originalKeys == targetKeys) return;

            var rng = new Random(seedValue);
            var osc = new Oscillator(seedValue);

            // 1. 列映射
            var mapped = MapColumns(beatmap.HitObjects, originalKeys, targetKeys, osc);

            // 2. 构建桶
            var buckets = BuildBuckets(mapped, targetKeys);

            // 3. 填补空桶
            FillEmptyBuckets(buckets, beatmap.HitObjects.ToList(), originalKeys, targetKeys, rng);

            // 4. 密度控制
            var finalObjects = AdjustBuckets(buckets, beatmap.HitObjects.Count, originalKeys, targetKeys, minKeys, maxKeys, rng);

            // 5. 更新谱面对象
            beatmap.HitObjects.Clear();
            beatmap.HitObjects.AddRange(finalObjects.OrderBy(h => h.StartTime).ThenBy(h => h.Column));

            // 6. 最后更新谱面总列数，避免越界
            try
            {
                beatmap.Stages.Clear();
                beatmap.Stages.Add(new StageDefinition(targetKeys));
                beatmap.Difficulty.CircleSize = targetKeys;
                Logger.Log($"[N2NcConverter] Updated beatmap stages and Difficulty.CircleSize to {targetKeys}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[N2NcConverter] Failed to update stages: {ex.Message}");
            }
        }

        public static List<(ManiaHitObject obj, int newCol)> MapColumns(IEnumerable<ManiaHitObject> objects, int originalKeys, int targetKeys, Oscillator osc)
        {
            var mapped = new List<(ManiaHitObject, int)>();

            foreach (var obj in objects)
            {
                int origCol = Math.Clamp(obj.Column, 0, originalKeys - 1);
                double proportion = originalKeys > 1 ? origCol / (double)(originalKeys - 1) : 0.0;
                double exact = proportion * (targetKeys - 1);
                int floor = (int)Math.Floor(exact);
                double frac = exact - floor;
                int newCol = floor;

                if (frac > 0.5) newCol = floor + 1;
                else if (Math.Abs(frac - 0.5) <= 1e-9 && osc.Next() > 0.5)
                    newCol = floor + 1;

                newCol = Math.Clamp(newCol, 0, targetKeys - 1);
                mapped.Add((obj, newCol));
            }

            return mapped;
        }

        public static Dictionary<int, List<ManiaHitObject>> BuildBuckets(List<(ManiaHitObject obj, int newCol)> mapped, int targetKeys)
        {
            var buckets = new Dictionary<int, List<ManiaHitObject>>();
            for (int i = 0; i < targetKeys; i++) buckets[i] = new List<ManiaHitObject>();

            foreach (var kv in mapped)
                buckets[kv.newCol].Add(kv.obj);

            return buckets;
        }

        public static void FillEmptyBuckets(Dictionary<int, List<ManiaHitObject>> buckets, List<ManiaHitObject> sourceObjects, int originalKeys, int targetKeys, Random rng)
        {
            if (targetKeys <= originalKeys) return;

            for (int t = 0; t < targetKeys; t++)
            {
                if (buckets[t].Count == 0)
                {
                    int approxSource = (int)Math.Round(t * (originalKeys - 1) / (double)(targetKeys - 1));
                    approxSource = Math.Clamp(approxSource, 0, originalKeys - 1);

                    var candidates = sourceObjects.Where(h => h.Column == approxSource).ToList();

                    if (candidates.Count > 0)
                    {
                        var pick = candidates[rng.Next(candidates.Count)];
                        buckets[t].Add(KrrConversionHelper.CloneObjectToColumn(pick, t));
                    }
                }
            }
        }

        public static List<ManiaHitObject> AdjustBuckets(Dictionary<int, List<ManiaHitObject>> buckets, int totalObjects, int originalKeys, int targetKeys, int minKeys, int maxKeys, Random rng)
        {
            var finalObjects = new List<ManiaHitObject>();
            int globalMax = Math.Max(1, maxKeys);
            int globalMin = Math.Max(0, minKeys);

            foreach (var kv in buckets)
            {
                var list = kv.Value.OrderBy(h => h.StartTime).ToList();
                int expectedPerBucket = (int)Math.Ceiling(totalObjects / (double)targetKeys);

                // 压缩过多
                if (originalKeys > targetKeys && list.Count > expectedPerBucket * 2)
                    list = list.OrderBy(x => rng.Next()).Take(expectedPerBucket).OrderBy(x => x.StartTime).ToList();

                // 超过最大值
                if (list.Count > globalMax)
                    list = list.OrderBy(x => rng.Next()).Take(globalMax).OrderBy(x => x.StartTime).ToList();

                // 少于最小值
                else if (list.Count < globalMin && list.Count > 0)
                {
                    int idx = 0;

                    while (list.Count < globalMin)
                    {
                        list.Add(KrrConversionHelper.CloneObjectToColumn(list[idx % list.Count], kv.Key));
                        idx++;
                    }

                    list = list.OrderBy(x => x.StartTime).ToList();
                }

                foreach (var h in list)
                {
                    h.Column = kv.Key;
                    finalObjects.Add(h);
                }
            }

            return finalObjects;
        }
    }
}
