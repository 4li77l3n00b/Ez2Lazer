// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;

namespace osu.Game.Rulesets.Mania.Mods.KrrConversion
{
    public class KrrConversionHelper
    {
        public static readonly double[] TransformSpeedValues =
            { 0.125, 0.25, 0.5, 0.75, 1, 2, 3, 4, 999 };

        public static double ComputeConvertTime(int transformSpeedIndex, double bpm)
        {
            double speed = TransformSpeedValues[Math.Clamp(transformSpeedIndex, 0, TransformSpeedValues.Length - 1)];
            return Math.Max(1, (speed * 60000 / bpm * 4) - 10);
        }

        public static int ComputeSeedFromBeatmap(ManiaBeatmap beatmap)
        {
            try
            {
                int val = (beatmap.HitObjects.Count) ^ (beatmap.TotalColumns);
                return Math.Abs(val) + 1;
            }
            catch
            {
                return 114514;
            }
        }

        public static int InferOriginalKeys(ManiaBeatmap beatmap, int fallback)
        {
            double cs = beatmap.BeatmapInfo.Difficulty.CircleSize;
            if (cs > 0)
                return Math.Max(1, (int)Math.Round(cs));

            if (beatmap.HitObjects.Count > 0)
                return beatmap.HitObjects.Max(h => h.Column) + 1;

            return fallback;
        }

        public static ManiaHitObject CloneObjectToColumn(ManiaHitObject src, int targetCol)
        {
            if (src is HoldNote hn)
            {
                return new HoldNote
                {
                    Column = targetCol,
                    StartTime = hn.StartTime,
                    EndTime = hn.EndTime,
                    Samples = hn.Samples?.ToList()
                };
            }

            return new Note
            {
                Column = targetCol,
                StartTime = src.StartTime,
                Samples = src.Samples
            };
        }
    }

    public class KrrOptions
    {
        public int TargetKeys { get; set; } = 8;
        public int MaxKeys { get; set; } = 10;
        public int MinKeys { get; set; } = 4;

        // 新增：转换速度索引，对应 TransformSpeedValues 数组
        public int TransformSpeed { get; set; } = 4;
        public int? Seed { get; set; }
    }
}
