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
        private IEnumerable<Process> _processes;

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
        public void Initialize()
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
                throw new SimulatorConnectionException("Simulator integration only available on Windows");
            }

            _logger.LogDebug("Connected to simulator instance");
        }

        /// <summary>
        /// Shuts down the connection with the automation server
        /// </summary>
        public virtual void Shutdown()
        {
            PreShutdown();
            Server = null;
            if (_processes != null && _processes.Any())
            {
                // This is not ideal but in some cases, activating a simulator instance creates
                // a background process that is not terminated when the instance is removed.
                // To avoid locking simulator licenses, we have to kill the process
                foreach (var prc in _processes)
                {
                    prc.Kill();
                }
                _processes = null;
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

            var prcs = new List<int>();
            if (_config.CanTerminateProcess())
            {
                prcs = Process.GetProcessesByName(_config.ProcessId)
                    .Select(p => p.Id)
                    .ToList();
            }
            dynamic server = Activator.CreateInstance(serverType);
            if (server == null)
            {
                _logger.LogError("Could not activate automation server instance");
                throw new SimulatorConnectionException("Cannot connect to simulator");
            }
            if (_config.CanTerminateProcess())
            {
                _processes = Process.GetProcessesByName(_config.ProcessId)
                    .Where(p => !prcs.Contains(p.Id))
                    .ToList();
            }
            return server;
        }
    }
    public class SimulatorConnectionException : Exception
    {
        public SimulatorConnectionException()
        {
        }

        public SimulatorConnectionException(string message) : base(message)
        {
        }

        public SimulatorConnectionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public class AutomationConfig
    {
        public string ProgramId { get; set; }
        public string ProcessId { get; set; }
        public bool TerminateProcess { get; set; }

        internal bool CanTerminateProcess()
        {
            return TerminateProcess && !string.IsNullOrEmpty(ProgramId);
        }
    }
}
