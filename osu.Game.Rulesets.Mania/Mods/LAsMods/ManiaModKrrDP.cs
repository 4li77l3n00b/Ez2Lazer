using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Mania.Mods.KrrConversion;

namespace osu.Game.Rulesets.Mania.Mods.LAsMods
{
    public class ManiaModKrrDP : Mod, IApplicableAfterBeatmapConversion
    {
        public override string Name => "Krr DP";
        public override string Acronym => "DP";
        public override LocalisableString Description => "[KrrTool] Convert to Dual Play mode";
        public override double ScoreMultiplier => 1;
        public override ModType Type => ModType.Conversion;
        public override bool Ranked => false;
        public override bool ValidForMultiplayer => true;
        public override bool ValidForFreestyleAsRequiredMod => false;

        [SettingSource("Enable Modify Keys", "Enable key modification")]
        public BindableBool EnableModifyKeys { get; } = new BindableBool();

        [SettingSource("Modify Keys", "Target number of keys for each side")]
        public BindableInt ModifyKeys { get; } = new BindableInt(4)
        {
            MinValue = 1,
            MaxValue = 9,
        };

        [SettingSource("Left Mirror", "Mirror left side")]
        public BindableBool LMirror { get; set; } = new BindableBool(false);

        [SettingSource("Left Density", "Adjust left side density")]
        public BindableBool LDensity { get; set; } = new BindableBool(false);

        [SettingSource("Left Remove", "Remove left side")]
        public BindableBool LRemove { get; set; } = new BindableBool(false);

        [SettingSource("Left Max Keys", "Max keys for left density")]
        public BindableInt LMaxKeys { get; set; } = new BindableInt(5)
        {
            MinValue = 1,
            MaxValue = 9,
        };

        [SettingSource("Left Min Keys", "Min keys for left density")]
        public BindableInt LMinKeys { get; set; } = new BindableInt(1)
        {
            MinValue = 1,
            MaxValue = 9,
        };

        [SettingSource("Right Mirror", "Mirror right side")]
        public BindableBool RMirror { get; set; } = new BindableBool(false);

        [SettingSource("Right Density", "Adjust right side density")]
        public BindableBool RDensity { get; set; } = new BindableBool(false);

        [SettingSource("Right Remove", "Remove right side")]
        public BindableBool RRemove { get; set; } = new BindableBool(false);

        [SettingSource("Right Max Keys", "Max keys for right density")]
        public BindableInt RMaxKeys { get; set; } = new BindableInt(5)
        {
            MinValue = 1,
            MaxValue = 9,
        };

        [SettingSource("Right Min Keys", "Min keys for right density")]
        public BindableInt RMinKeys { get; set; } = new BindableInt(1)
        {
            MinValue = 1,
            MaxValue = 9,
        };

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            var maniaBeatmap = (ManiaBeatmap)beatmap;
            int originalKeys = maniaBeatmap.TotalColumns;
            var rng = new Random();

            var newObjects = new List<ManiaHitObject>();

            foreach (var hitObject in maniaBeatmap.HitObjects)
            {
                int originalColumn = hitObject.Column;

                // 左侧处理
                if (!LRemove.Value)
                {
                    int leftCol = LMirror.Value ? (originalKeys - 1 - originalColumn) : originalColumn;

                    if (EnableModifyKeys.Value)
                        leftCol = rng.Next(ModifyKeys.Value);

                    var leftObject = KrrConversionHelper.CloneWithColumn(hitObject, leftCol);
                    newObjects.Add(leftObject);
                }

                // 右侧处理
                if (!RRemove.Value)
                {
                    int rightCol = RMirror.Value ? (originalKeys - 1 - originalColumn) : originalColumn;

                    if (EnableModifyKeys.Value)
                        rightCol = ModifyKeys.Value + rng.Next(ModifyKeys.Value);
                    else
                        rightCol += originalKeys;

                    var rightObject = KrrConversionHelper.CloneWithColumn(hitObject, rightCol);
                    newObjects.Add(rightObject);
                }
            }

            // 如果开启密度控制，调用 N2NC 转换器来处理左右半区
            if (LDensity.Value || RDensity.Value)
            {
                var options = new KrrOptions
                {
                    TargetKeys = originalKeys * 2,
                    MaxKeys = LDensity.Value ? LMaxKeys.Value : RMaxKeys.Value,
                    MinKeys = LDensity.Value ? LMinKeys.Value : RMinKeys.Value,
                    Seed = rng.Next()
                };

                KrrN2NcConverter.Transform(maniaBeatmap, options);
            }
            else
            {
                maniaBeatmap.HitObjects.Clear();
                maniaBeatmap.HitObjects.AddRange(newObjects.OrderBy(h => h.StartTime).ThenBy(h => h.Column));
            }

            // 最后更新总列数
            int finalKeys = originalKeys * 2;
            maniaBeatmap.Stages.Clear();
            maniaBeatmap.Stages.Add(new StageDefinition(finalKeys));
            maniaBeatmap.Difficulty.CircleSize = finalKeys;
        }
    }
}
