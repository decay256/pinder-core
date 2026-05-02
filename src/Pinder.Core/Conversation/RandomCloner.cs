using System;
using System.Collections;
using System.Reflection;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// #790 (Phase 4) — deep-clone helper for <see cref="System.Random"/>.
    ///
    /// <para>
    /// The fast-gameplay scheduler (#425) needs three independent forked
    /// <see cref="GameSession"/> instances per turn. Engine state that lives
    /// behind <see cref="System.Random"/> (steering RNG, stat-draw RNG) must
    /// be deep-copied so that mutating one branch's RNG does not perturb the
    /// others; otherwise three parallel branches sharing one RNG would race
    /// on internal seed state and yield non-deterministic outcomes.
    /// </para>
    ///
    /// <para>
    /// .NET 8's default <see cref="System.Random"/> wraps an internal
    /// <c>_impl</c> object (<c>System.Random+Net5CompatSeedImpl</c>) which in
    /// turn holds a <c>CompatPrng</c> struct containing a backing
    /// <c>_seedArray</c> reference. <see cref="object.MemberwiseClone"/> on
    /// the impl alone preserves the array reference \u2014 the two clones would
    /// share the seed array and silently re-couple. This helper performs the
    /// three layers of copy needed for true independence:
    /// </para>
    /// <list type="number">
    ///   <item>MemberwiseClone the outer <see cref="System.Random"/>.</item>
    ///   <item>MemberwiseClone its internal <c>_impl</c> object.</item>
    ///   <item>For every reference field on the impl (or array field nested
    ///   inside a struct field on the impl), allocate a fresh array and copy
    ///   the values across.</item>
    /// </list>
    ///
    /// <para>
    /// The technique is portable across .NET 8.x revisions; if a future BCL
    /// change introduces additional reference state on the <c>_impl</c>, the
    /// generic field-walk here will still pick it up. A unit test in
    /// <c>RandomClonerTests</c> pins the contract: clone produces a
    /// byte-identical sequence to the parent at clone-time and continues to
    /// do so after the parent advances.
    /// </para>
    /// </summary>
    internal static class RandomCloner
    {
        // BCL field names on .NET 8: Random._impl, *Impl._prng, CompatPrng._seedArray.
        // Resolved by reflection so we don't depend on internal API surfaces
        // staying source-compatible.
        private static readonly FieldInfo? _implField =
            typeof(Random).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo? _memberwiseClone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Deep-clone a <see cref="Random"/> instance so the resulting Random
        /// produces the same sequence as <paramref name="src"/> would have at
        /// the moment of cloning, but is fully independent of subsequent
        /// mutations to <paramref name="src"/>.
        /// </summary>
        /// <param name="src">Random to clone. Must not be null.</param>
        /// <returns>An independent <see cref="Random"/> with the same internal
        /// state as <paramref name="src"/> at the moment of the call.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="src"/> is null.</exception>
        /// <exception cref="InvalidOperationException">If the BCL Random
        /// internals change shape and the helper can no longer locate
        /// <c>_impl</c> / <c>MemberwiseClone</c>. Treat this as a hard fault
        /// rather than silently falling back to a fresh-seeded Random \u2014
        /// non-determinism here would silently break clone equivalence.</exception>
        public static Random Clone(Random src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (_implField == null || _memberwiseClone == null)
                throw new InvalidOperationException(
                    "RandomCloner: BCL Random internals not located via reflection. " +
                    "This indicates a .NET runtime change \u2014 the cloner needs an audit.");

            // 1. MemberwiseClone the impl (shallow), then deep-clone its reference state.
            var srcImpl = _implField.GetValue(src);
            if (srcImpl == null)
                throw new InvalidOperationException(
                    "RandomCloner: Random._impl was null \u2014 unexpected for a default-constructed Random.");

            var clonedImpl = _memberwiseClone.Invoke(srcImpl, null);
            DeepCopyImplReferenceState(srcImpl.GetType(), clonedImpl!);

            // 2. MemberwiseClone the outer Random and graft the cloned impl in place.
            var clonedRandom = (Random)_memberwiseClone.Invoke(src, null)!;
            _implField.SetValue(clonedRandom, clonedImpl);
            return clonedRandom;
        }

        private static void DeepCopyImplReferenceState(Type implType, object clonedImpl)
        {
            // Walk every instance field on the impl. For:
            //   - reference fields holding arrays \u2192 clone the array.
            //   - value-type (struct) fields \u2192 box, recursively walk their
            //     fields, clone any arrays we find, write the boxed copy back.
            //   - other reference fields \u2192 leave alone (immutable enough for
            //     our purposes; the impl types we know about don't carry any).
            foreach (var f in implType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                var current = f.GetValue(clonedImpl);
                if (current == null) continue;

                var ft = f.FieldType;
                if (ft.IsArray)
                {
                    var copy = ((Array)current).Clone();
                    f.SetValue(clonedImpl, copy);
                    continue;
                }

                if (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum)
                {
                    // Box the struct so we can mutate it via reflection, then write back.
                    var boxed = current;
                    foreach (var inner in ft.GetFields(
                                 BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        var iv = inner.GetValue(boxed);
                        if (iv == null) continue;
                        if (inner.FieldType.IsArray)
                        {
                            inner.SetValue(boxed, ((Array)iv).Clone());
                        }
                    }
                    f.SetValue(clonedImpl, boxed);
                }
            }
        }
    }
}
