using System;
using System.Collections.Generic;
using System.Linq;

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
        /// Indicates if the model file has been downloaded and the file exists on the disk.
        /// Also checks if all dependency files have their paths assigned.
        /// </summary>
        public bool Downloaded
        {
            get
            {
                var dependenciesDownloaded = DependencyFiles.All(file => file.Downloaded);
                return !string.IsNullOrEmpty(FilePath) && System.IO.File.Exists(FilePath) && dependenciesDownloaded;
            }
        }

        /// <summary>
        /// Sets the file path for the main file or a dependency file.
        /// </summary>
        public void SetFilePath(long fileId, string filePath, string fileExtension = null)
        {
            if (fileId == CdfId)
            {
                FilePath = filePath;
                IsInDirectory = true;
                FileExtension = fileExtension ?? FilesExtensions.GetFileExtension(filePath);
            }
            else
            {
                UpdateDependencyFilePath(fileId, filePath);
            }
        }

        /// <summary>
        /// Gets all file IDs that are yet to be downloaded.
        /// This includes the main file ID and any dependency files.
        /// </summary>
        /// <returns>List of file IDs pending download, and a dictionary of already downloaded files with their paths</returns>
        /// <exception cref="InvalidOperationException">Thrown if CdfId is not set</exception>
        public (List<long>, Dictionary<long, string>) DeduplicateDownloadFileIds(Dictionary<long, string> currentFilesCache)
        {
            if (CdfId == 0)
            {
                throw new InvalidOperationException("Model state must have a valid File ID.");
            }

            if (currentFilesCache == null)
            {
                throw new ArgumentNullException(nameof(currentFilesCache));
            }

            var expectedFileIds = new[] { CdfId }.Concat(DependencyFiles.Select(file => file.Id));
            var fileIdsToDownload = new List<long>();
            var alreadyDownloadedFiles = new Dictionary<long, string>();

            foreach (var expectedFile in expectedFileIds)
            {
                if (currentFilesCache.TryGetValue(expectedFile, out var filePath))
                {
                    alreadyDownloadedFiles[expectedFile] = filePath;
                }
                else
                {
                    fileIdsToDownload.Add(expectedFile);
                }
            }

            return (
                fileIdsToDownload,
                alreadyDownloadedFiles
            );
        }

        /// <summary>
        /// Updates the file path of a dependency file.
        /// Throws an exception if the file reference does not exist in the state.
        /// </summary>
        /// <param name="fileId">The ID of the dependency file</param>
        /// <param name="filePath">The new file path for the dependency file</param>
        /// <exception cref="ArgumentNullException">Thrown if dependencyFile is null</exception>
        public void UpdateDependencyFilePath(long fileId, string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty.");
            }

            var indexOf = DependencyFiles.FindIndex(file => file.Id == fileId);

            if (indexOf >= 0)
            {
                DependencyFiles[indexOf].FilePath = filePath;
                return;
            }

            throw new InvalidOperationException($"Dependency file with ID {fileId} not found in the model state.");
        }

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
