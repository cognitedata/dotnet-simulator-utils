using System.Linq;
using System;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Cognite.Simulator.Utils.Automation;
using Cognite.Simulator.Utils;
using System.Threading;
using System.Globalization;
using System.Management;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
     public static class ProcessUtils {

        /// <summary>
        /// Get the owner of a process using WMI
        /// </summary>
        /// <param name="processId">The process ID</param>
        /// <returns>The owner of the process</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown when the method is called on a non-Windows platform</exception>
        /// <exception cref="Exception">Thrown when the process is not found or access is denied</exception>
        public static string GetProcessOwnerWmi(int processId)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                throw new PlatformNotSupportedException("This method is only supported on Windows");
            }
            string query = $"SELECT * FROM Win32_Process WHERE ProcessId = {processId}";
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject process in results)
                {
                    try
                    {
                        object[] args = new object[] { "", "" };
                        if (Convert.ToInt32(process.InvokeMethod("GetOwner", args)) == 0)
                        {
                            string user = (string)args[0];
                            string domain = (string)args[1];
                            return $"{domain}\\{user}";
                        }
                    }
                    catch
                    {
                        throw new Exception("Access Denied or Process Exited");
                    }
                }
            }
            throw new Exception("Process not found");
        }

        /// <summary>
        /// Kill a process by ID. Can throw exceptions if it is unable to kill the process or if it cannot find the process owner.
        /// </summary>
        /// <param name="processId"></param>
        /// <param name="_logger"></param>
        public static void KillProcess(string processId, ILogger _logger) {
            try {
                Process[] processes = Process.GetProcessesByName(processId);
                _logger.LogDebug("Searching for process : " + processId);
                _logger.LogDebug("Found {Count} matching processes", processes.Length);
                bool found = false;
                foreach (Process process in processes) {
                    string owner = GetProcessOwnerWmi(process.Id);
                    _logger.LogDebug($"Found process . Process owner is : {owner.ToLower()} . Current user is : {GetCurrentUsername().ToLower()}");
                    if (owner.ToLower() == GetCurrentUsername().ToLower()) {
                        _logger.LogInformation("Killing process with PID {PID}", process.Id);
                        process.Kill();
                        process.WaitForExit();
                        found = true;
                    } else {
                        _logger.LogWarning("Process with PID {PID} is owned by a different user ({Owner}). Skipping.", process.Id, owner);
                    }
                }
                if (!found) {
                    throw new Exception("No processes found to kill for the current user");
                }
            } catch (Exception e) {
                _logger.LogError("Failed to kill process: {Message}", e.Message);
                throw;
            }
        }

        public static string GetCurrentUsername()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                throw new PlatformNotSupportedException("This method is only supported on Windows");
            }
            return WindowsIdentity.GetCurrent().Name;
        }
    }
}
