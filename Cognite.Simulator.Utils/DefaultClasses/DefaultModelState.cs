using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{

    /// <summary>
    /// Default implementation of a model state
    /// </summary>
    public class DefaultModelFileStatePoco : ModelStateBasePoco
    {
        /// <summary>
        /// Gets a value indicating whether the information has been extracted.
        /// </summary>
        [StateStoreProperty("info-extracted")]
        public bool InformationExtracted { get; internal set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultModelFilestate"/> class.
    /// </summary>
    public class DefaultModelFilestate : ModelStateBase
    {
        /// <inheritdoc/>
        public DefaultModelFilestate() : base()
        {
        }

        /// <summary>
        /// Gets or sets a value indicating whether the model file has been processed.
        /// </summary>
        public bool Processed { get; set; } = true;

        /// <summary>
        /// Indicates if information has been extracted from the model file
        /// </summary>
        public override bool IsExtracted => Processed;
    }
}