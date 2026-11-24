using GachaEngine.Data;

namespace GachaEngine.Core
{
    /// <summary>
    /// Random number generator abstraction used by <see cref="Engine.GachaRoller"/> for rarity rolls and weighted item selection.
    /// </summary>
    public interface IGachaRng
    {
        /// <summary>
        /// Returns a random floating-point value in the half-open interval [0.0, 1.0).
        /// </summary>
        double NextDouble();

        /// <summary>
        /// Returns a random integer in the range [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).
        /// </summary>
        /// <param name="minInclusive">Inclusive lower bound.</param>
        /// <param name="maxExclusive">Exclusive upper bound.</param>
        int NextInt(int minInclusive, int maxExclusive);
    }

    /// <summary>
    /// Persistence contract for reading and saving <see cref="GachaPityState"/> keyed by banner <c>pityGroupId</c>.
    /// </summary>
    public interface IPityStore
    {
        /// <summary>
        /// Returns the existing pity state for <paramref name="pityGroupId"/>, creating and storing a fresh state if none exists.
        /// </summary>
        /// <param name="pityGroupId">Shared pity group identifier from a <see cref="GachaBannerAsset"/>.</param>
        GachaPityState GetOrCreate(string pityGroupId);

        /// <summary>
        /// Persists updated pity state for the specified <paramref name="pityGroupId"/>.
        /// </summary>
        /// <param name="pityGroupId">Shared pity group identifier.</param>
        /// <param name="state">Updated pity state snapshot to store.</param>
        void Save(string pityGroupId, GachaPityState state);

        /// <summary>
        /// Clears all stored pity states across every pity group.
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// Converts repeated pulls of an already-owned item into duplicate shard or max-rank overflow rewards.
    /// </summary>
    public interface IDuplicateResolver
    {
        /// <summary>
        /// Resolves ownership count and currency rewards when <paramref name="item"/> is pulled.
        /// </summary>
        /// <param name="item">The pulled item entry.</param>
        /// <param name="ownedCountBeforePull">Number of copies owned prior to this pull.</param>
        /// <returns>Resolution struct indicating duplicate status, overflow state, new owned count, and reward payout.</returns>
        DuplicateResolution Resolve(GachaItemEntry item, int ownedCountBeforePull);
    }

    /// <summary>
    /// Currency balance contract used by <see cref="Engine.GachaRoller"/> to charge pull costs and credit duplicate rewards.
    /// </summary>
    public interface IGachaCurrencyWallet
    {
        /// <summary>
        /// Returns the current balance for <paramref name="currencyId"/>, or <c>0</c> if unknown.
        /// </summary>
        /// <param name="currencyId">Currency identifier string.</param>
        int GetBalance(string currencyId);

        /// <summary>
        /// Attempts to deduct <paramref name="amount"/> of <paramref name="currencyId"/> if sufficient balance is available.
        /// </summary>
        /// <param name="currencyId">Currency identifier to debit.</param>
        /// <param name="amount">Non-negative amount to spend.</param>
        /// <returns><c>true</c> if the balance covered the cost (or <paramref name="amount"/> is non-positive); otherwise <c>false</c>.</returns>
        bool TrySpend(string currencyId, int amount);

        /// <summary>
        /// Credits <paramref name="amount"/> of <paramref name="currencyId"/> to the wallet.
        /// </summary>
        /// <param name="currencyId">Currency identifier to credit.</param>
        /// <param name="amount">Positive amount to add.</param>
        void Add(string currencyId, int amount);
    }

    /// <summary>
    /// Optional hook for custom banner rules such as event rate boosts, starter discounts, or streak bonuses.
    /// </summary>
    public interface IGachaPoolModifier
    {
        /// <summary>
        /// Unique identifier for this modifier instance or rule type.
        /// </summary>
        string ModifierId { get; }

        /// <summary>
        /// Adjusts the effective drop probability for a single rarity tier before the tier roll is evaluated.
        /// </summary>
        /// <param name="banner">Active banner being rolled.</param>
        /// <param name="tier">Rarity tier currently under evaluation.</param>
        /// <param name="effectiveProbability">Probability in [0, 1] after soft pity; modified in place and clamped by the roller.</param>
        /// <param name="pityState">Current pity state for the banner's pity group.</param>
        void ModifyTierRate(
            GachaBannerAsset banner,
            RarityTierConfig tier,
            ref double effectiveProbability,
            GachaPityState pityState);
    }
}
