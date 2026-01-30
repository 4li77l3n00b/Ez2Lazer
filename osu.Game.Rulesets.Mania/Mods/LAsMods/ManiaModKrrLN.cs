// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Mods.KrrConversion;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Mania.Mods.LAsMods
{
    public class ManiaModKrrLN : Mod, IApplicableAfterBeatmapConversion
    {
        public override string Name => "Krr LN";
        public override string Acronym => "LN";
        public override LocalisableString Description => "[KrrTool] Long-note transformer (移植版)";
        public override double ScoreMultiplier => 1;
        public override ModType Type => ModType.Conversion;
        public override bool Ranked => false;
        public override bool ValidForMultiplayer => true;

        // ---------------- 设置项 ----------------

        [SettingSource("Level", "Transform level")]
        public BindableInt Level { get; } = new BindableInt(3)
        {
            MinValue = -3,
            MaxValue = 10,
        };

        [SettingSource("Seed", "Random seed (optional)")]
        public Bindable<int?> Seed { get; } = new Bindable<int?>(114514);

        [SettingSource("Process Original LN", "Skip original LN notes")]
        public BindableBool ProcessOriginalIsChecked { get; } = new BindableBool(false);

        [SettingSource("Length Threshold", "Border key threshold")]
        public BindableInt LengthThreshold { get; } = new BindableInt(4)
        {
            MinValue = 1,
            MaxValue = 32,
        };

        [SettingSource("Long LN Percentage", "Percentage of long LN conversion")]
        public BindableDouble LongPercentage { get; } = new BindableDouble(100)
        {
            MinValue = 0,
            MaxValue = 100,
        };

        [SettingSource("Short LN Percentage", "Percentage of short LN conversion")]
        public BindableDouble ShortPercentage { get; } = new BindableDouble(100)
        {
            MinValue = 0,
            MaxValue = 100,
        };

        [SettingSource("Long LN Limit", "Max long LN per row")]
        public BindableInt LongLimit { get; } = new BindableInt(4)
        {
            MinValue = 0,
            MaxValue = 32,
        };

        [SettingSource("Short LN Limit", "Max short LN per row")]
        public BindableInt ShortLimit { get; } = new BindableInt(4)
        {
            MinValue = 0,
            MaxValue = 32,
        };

        [SettingSource("Long Random", "Randomization factor for long LN")]
        public BindableInt LongRandom { get; } = new BindableInt(50)
        {
            MinValue = 0,
            MaxValue = 100,
        };

        [SettingSource("Short Random", "Randomization factor for short LN")]
        public BindableInt ShortRandom { get; } = new BindableInt(50)
        {
            MinValue = 0,
            MaxValue = 100,
        };

        [SettingSource("Alignment", "Enable LN length alignment (1/8..1/1)")]
        public BindableInt Alignment { get; } = new BindableInt(0)
        {
            MinValue = 0,
            MaxValue = 8,
        };

        [SettingSource("OD Override", "Override OverallDifficulty")]
        public Bindable<float?> ODValue { get; } = new Bindable<float?>(null);

        // ---------------- 应用逻辑 ----------------

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            var maniaBeatmap = (ManiaBeatmap)beatmap;

            var options = new KrrLNOptions
            {
                Level = Level.Value,
                Seed = Seed.Value,
                ProcessOriginalIsChecked = ProcessOriginalIsChecked.Value,
                LengthThreshold = LengthThreshold.Value,
                LongPercentage = LongPercentage.Value,
                ShortPercentage = ShortPercentage.Value,
                LongLimit = LongLimit.Value,
                ShortLimit = ShortLimit.Value,
                LongRandom = LongRandom.Value,
                ShortRandom = ShortRandom.Value,
                Alignment = Alignment.Value > 0 ? Alignment.Value : null,
                ODValue = ODValue.Value
            };

            KrrLNConverter.Transform(maniaBeatmap, options);
        }
    }

    // ---------------- Options 类 ----------------

    public class KrrLNOptions
    {
        public int Level { get; set; }
        public int? Seed { get; set; }
        public bool ProcessOriginalIsChecked { get; set; }
        public int LengthThreshold { get; set; } = 4;
        public double LongPercentage { get; set; } = 100;
        public double ShortPercentage { get; set; } = 100;
        public int LongLimit { get; set; } = 4;
        public int ShortLimit { get; set; } = 4;
        public int LongRandom { get; set; } = 50;
        public int ShortRandom { get; set; } = 50;
        public int? Alignment { get; set; } // null means no alignment
        public float? ODValue { get; set; }
    }
}
