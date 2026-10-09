using GachaEngine.Data;

namespace GachaEngine.Core
{
    public interface IGachaRng
    {
        double NextDouble();
        int NextInt(int minInclusive, int maxExclusive);
    }

    public interface IPityStore
    {
        GachaPityState GetOrCreate(string pityGroupId);
        void Save(string pityGroupId, GachaPityState state);
        void Clear();
    }

    public interface IDuplicateResolver
    {
        DuplicateResolution Resolve(GachaItemEntry item, int ownedCountBeforePull);
    }

    public interface IGachaCurrencyWallet
    {
        int GetBalance(string currencyId);
        bool TrySpend(string currencyId, int amount);
        void Add(string currencyId, int amount);
    }

    /// <summary>
    /// Optional hook for custom banner rules (event rate boosts, beginner discounts, streak bonuses).
    /// </summary>
    public interface IGachaPoolModifier
    {
        string ModifierId { get; }
        void ModifyTierRate(
            GachaBannerAsset banner,
            RarityTierConfig tier,
            ref double effectiveProbability,
            GachaPityState pityState);
    }
}
