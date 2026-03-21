using System.Collections.Generic;
using Pinder.Core.Characters;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Provides access to item definitions loaded from the item data files.
    /// </summary>
    public interface IItemRepository
    {
        /// <summary>Returns the item with the given id, or null if not found.</summary>
        ItemDefinition? GetItem(string itemId);

        /// <summary>Returns all loaded items.</summary>
        IEnumerable<ItemDefinition> GetAll();
    }
}
