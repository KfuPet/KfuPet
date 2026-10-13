namespace KfuPet.Models
{
    /// <summary>
    /// 情绪状态（emotion.json）的数据契约：愉悦 / 精力 / 亲密三个维度（0–100），
    /// 外加两个时间游标——<see cref="UpdatedAtUtc"/> 用于惰性衰减结算，
    /// <see cref="LastInteractionUtc"/> 用于“被冷落”惩罚结算。
    /// 两者语义不同，不可合并：前者随每次结算推进，后者只在主人真的互动时才刷新。
    /// 基线数值与增减规则由 <see cref="KfuPet.Services.EmotionService"/> 维护，本类只承载数据。
    /// </summary>
    public class EmotionState
    {
        /// <summary>愉悦度：被抚摸、被夸时上升；被冷落、被甩出屏幕时下降。</summary>
        public double Joy { get; set; }

        /// <summary>精力：交谈时略升，被反复戳弄时下降。</summary>
        public double Energy { get; set; }

        /// <summary>亲密度：互动越频繁越高，长期不理它则下降。</summary>
        public double Intimacy { get; set; }

        /// <summary>上次结算时刻（UTC），衰减计算的起点。</summary>
        public DateTime UpdatedAtUtc { get; set; }

        /// <summary>上次与主人互动的时刻（UTC），“被冷落”惩罚的计算起点。</summary>
        public DateTime LastInteractionUtc { get; set; }
    }
}
