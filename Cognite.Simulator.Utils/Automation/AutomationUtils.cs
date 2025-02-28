using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Cognite.Simulator.Utils.Automation
{
    /// <summary>
    /// Implements a dynamic automation client that connects to a simulator's 
    /// automation server (COM interface). The types of the objects exposed by the server 
    /// are not known at runtime
    /// </summary>
    public abstract class AutomationClient
    {
        /// <summary>
        /// The activated instance of the automation server (simulator)
        /// </summary>
        protected dynamic Server { get; private set; }
        private readonly ILogger _logger;
        private readonly AutomationConfig _config;

        /// <summary>
        /// Creates an instance of the client that instantiates a connection
        /// to the server with the program id in the provided configuration (<paramref name="config"/>)
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="config">Automation configuration</param>
        public AutomationClient(ILogger logger, AutomationConfig config)
        {
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// Initialize a connection to the simulator instance. This instance needs to
        /// be deactivated after use with <see cref="Shutdown"/>, else resources may not be deallocated
        /// </summary>
        /// <exception cref="SimulatorConnectionException">Thrown if the connection cannot be established</exception>
        public virtual void Initialize()
        {
            if (Server != null)
            {
                return;
            }
            _logger.LogDebug("Connecting to automation server");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Server = ActivateAutomationServer();
            }
            else
            {
                var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT").ToLower() == "development";
                if (!isDevelopment) {
                    throw new SimulatorConnectionException("Simulator integration only available on Windows");
                }

            }

            _logger.LogDebug("Connected to simulator instance");
        }

        /// <summary>
        /// Shuts down the connection with the automation server
        /// </summary>
        public virtual void Shutdown()
        {
            try
            {
                PreShutdown();
            }
            finally
            {
                if (Server != null) {
                    Marshal.ReleaseComObject(Server);
                    _logger.LogDebug("Released COM Object");
                    Server = null;
                }
            }
            _logger.LogDebug("Automation server instance removed");
        }

        /// <summary>
        /// This method implements actions that need to occur prior to the automation server
        /// shutdown.
        /// </summary>
        protected abstract void PreShutdown();

        private dynamic ActivateAutomationServer()
        {
            var serverType = Type.GetTypeFromProgID(_config.ProgramId);
            if (serverType == null)
            {
                _logger.LogError("Could not find automation server using the id: {ProgId}", _config.ProgramId);
                throw new SimulatorConnectionException("Cannot connect to simulator");
            }            
            dynamic server = Activator.CreateInstance(serverType);
            if (server == null)
            {
                _logger.LogError("Could not activate automation server instance");
                throw new SimulatorConnectionException("Cannot connect to simulator");
            }
            return server;
        }
    }
    
    /// <summary>
    /// Represents errors related to the connection to simulator instance
    /// </summary>
    public class SimulatorConnectionException : Exception
    {
        /// <summary>
        /// Creates a new simulator connection exception
        /// </summary>
        public SimulatorConnectionException()
        {
        }

        /// <summary>
        /// Creates a new simulator connection exception, with the given message
        /// </summary>
        /// <param name="message">Error message</param>
        public SimulatorConnectionException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a new simulator connection exception, with the given message and inner exception
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="innerException">Inner exception</param>
        public SimulatorConnectionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Represents the automation client configuration. COM automation clients
    /// connect to servers dynamically, using the <see cref="ProgramId"/> to identify
    /// the application to connect to
    /// </summary>
    public class AutomationConfig
    {
        /// <summary>
        /// Identifier of the application/program to connect to
        /// </summary>
        public string ProgramId { get; set; }
        
        /// <summary>
        /// Identifier of the process that should be terminated on shutdown, if any
        /// </summary>
        public string ProcessId { get; set; }
    }
}
