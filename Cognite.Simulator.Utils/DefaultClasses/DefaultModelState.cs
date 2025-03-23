using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{

    public class DefaultModelFileStatePoco : ModelStateBasePoco
    {
        [StateStoreProperty("info-extracted")]
        public bool InformationExtracted { get; internal set; }
    }

    public class DefaultModelFilestate : ModelStateBase
    {
        public DefaultModelFilestate() : base()
        {
        }

        public bool Processed { get; set; } = true;

        /// <summary>
        /// Indicates if information has been extracted from the model file
        /// </summary>
        public override bool IsExtracted => Processed;
    }
}