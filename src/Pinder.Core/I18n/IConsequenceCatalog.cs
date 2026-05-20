namespace Pinder.Core.I18n
{
    /// <summary>
    /// Engine-side consequence catalogue that resolves
    /// <c>consequence.&lt;kind&gt;.&lt;outcome&gt;[.&lt;detail&gt;]</c> keys
    /// to plain-language strings with slot substitution already applied.
    /// Implemented in <c>Pinder.LlmAdapters</c> over <c>data/i18n/en/consequences.yaml</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sprint: #976 — engine-side population of Consequence on
    /// RollCheckResult / ShadowCheckResult / HorninessCheckResult.
    /// </para>
    /// <para>
    /// Key shape: <c>consequence.roll.miss.tropetrap</c>,
    /// <c>consequence.shadow.miss.dread</c>, etc. See
    /// <c>data/i18n/en/consequences.yaml</c> for the full key map.
    /// Each value is a template with optional <c>{stat}</c>
    /// / <c>{trap_name}</c> slots.
    /// </para>
    /// <para>
    /// Return null when a key is not in the catalogue — engines
    /// leave <c>Consequence</c> null in that case (documented behaviour:
    /// file a follow-up ticket for the missing key).
    /// </para>
    /// </remarks>
    public interface IConsequenceCatalog
    {
        /// <summary>
        /// Look up the consequence template for <paramref name="key"/>.
        /// Returns null when the key is not registered in the catalogue.
        /// </summary>
        string? Lookup(string key);
    }
}
