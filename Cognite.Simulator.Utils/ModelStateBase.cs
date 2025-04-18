using System;

using Cognite.Extractor.StateStorage;
using Cognite.Simulator.Extensions;


namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// This base class represents the state of a model file
    /// TODO: See if we can remove FileState completely and move all the variables into this class
    /// Jira: https://cognitedata.atlassian.net/browse/POFSP-558
    /// </summary>
    public abstract class ModelStateBase : FileState
    {

        /// <summary>
        /// Information about model parsing
        /// </summary>
        public ModelParsingInfo ParsingInfo { get; set; }

        /// <summary>
        /// Indicates if information has been extracted from the model file
        /// </summary>
        public abstract bool IsExtracted { get; }

        /// <summary>
        /// Indicates if the simulator can read the model file and
        /// its data
        /// </summary>
        public bool CanRead { get; set; } = true;

        /// <summary>
        /// Creates a new model file state instance
        /// </summary>
        public ModelStateBase() : base()
        {
        }

        /// <summary>
        /// If true, the local file will be opened and parsed by the connector. By default, this happens only once per model revision.
        /// Can be overridden, when re-parsing on every download is preferred.
        /// </summary>
        public virtual bool ShouldProcess()
        {
            var isParsedBefore = ParsingInfo?.Parsed ?? false;
            return !string.IsNullOrEmpty(FilePath) && CanRead && !isParsedBefore;
        }

        /// <summary>
        /// Model data associated with this state
        /// </summary>
        public SimulatorModelInfo Model => new SimulatorModelInfo()
        {
            ExternalId = ModelExternalId,
            Simulator = Source
        };

        /// <summary>
        /// Initialize this model state using a data object from the state store
        /// </summary>
        /// <param name="poco">Data object</param>
        public override void Init(FileStatePoco poco)
        {
            base.Init(poco);
        }

        /// <summary>
        /// Copies matching properties from the source object to the target object.
        /// Only properties with the same name and type are copied.
        /// </summary>
        public static TTarget SyncProperties<TSource, TTarget>(TSource source, TTarget target) where TSource : class where TTarget : class
        {
            foreach (var sourceProperty in typeof(TSource).GetProperties())
            {
                if (sourceProperty.CanWrite)
                {
                    try
                    {
                        var targetProperty = typeof(TTarget).GetProperty(sourceProperty.Name);
                        if (targetProperty != null && targetProperty.CanWrite && targetProperty.PropertyType == sourceProperty.PropertyType)
                        {
                            targetProperty.SetValue(target, sourceProperty.GetValue(source));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error setting property {sourceProperty.Name}: {ex.Message}");
                    }
                }
            }
            return target;
        }
    }

    /// <summary>
    /// Data object that contains the model state properties to be persisted
    /// by the state store. These properties are restored to the state on initialization
    /// </summary>
    public class ModelStateBasePoco : FileStatePoco
    {

    }
}
