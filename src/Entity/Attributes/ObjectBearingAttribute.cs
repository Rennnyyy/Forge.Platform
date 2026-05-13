namespace Forge.Entity;

/// <summary>
/// Marks an <see cref="EntityAttribute">entity</see> as object-bearing.
/// The source generator emits <c>ObjectKey</c>, <c>ContentType</c>, and
/// <c>ForgeObjectStoreKey</c> members on the partial class.
/// The named store identified by <paramref name="storeKey"/> is stored in the
/// <c>ForgeObjectStoreKey</c> constant so that HTTP infrastructure can resolve
/// the correct <c>IObjectStore</c> at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ObjectBearingAttribute : Attribute
{
    public ObjectBearingAttribute(string storeKey) => StoreKey = storeKey;

    /// <summary>The DI key of the <c>IObjectStore</c> to use for this entity's blob.</summary>
    public string StoreKey { get; }
}
