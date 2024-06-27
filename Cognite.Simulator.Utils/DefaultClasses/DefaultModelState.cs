using Cognite.Extractor.StateStorage;

namespace Cognite.Simulator.Utils
{

    /// <summary>
    /// The default Model File state Plain Old C Object
    /// </summary>
   public class DefaultModelFileStatePoco : ModelStateBasePoco
    {
        /// <summary>
        /// 
        /// </summary>
        [StateStoreProperty("info-extracted")]
        public bool InformationExtracted { get; internal set; }
    }

    /// <summary>
    /// The default Model File state Object
    /// </summary>
    public class DefaultModelFilestate : ModelStateBase
    {
        /// <summary>
        /// The default Model File state Object
        /// </summary>
        /// <param name="id"></param>
        public DefaultModelFilestate(string id) : base(id)
        {
        }

        /// <summary>
        /// If the model is an archive file, this variable would indicate
        /// if the model has been extracted
        /// </summary>
        public override bool IsExtracted => false;
    }
}