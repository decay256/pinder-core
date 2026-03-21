using System.Collections.Generic;
using Pinder.Core.Characters;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Provides access to anatomy parameter definitions loaded from the anatomy data file.
    /// </summary>
    public interface IAnatomyRepository
    {
        /// <summary>Returns the parameter with the given id, or null if not found.</summary>
        AnatomyParameterDefinition? GetParameter(string parameterId);

        /// <summary>Returns all loaded anatomy parameters.</summary>
        IEnumerable<AnatomyParameterDefinition> GetAll();
    }
}
