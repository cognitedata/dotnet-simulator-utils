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

        public static void KillProcess(string processId, ILogger _logger) {
            try {
                Process[] processes = Process.GetProcessesByName(processId);
                _logger.LogDebug("Searching for process : " + processId);
                foreach (Process process in processes) {
                    string owner = GetProcessOwnerWmi(process.Id);
                    _logger.LogDebug($"Found process . Process owner is : {owner.ToLower()} . Current user is : {GetCurrentUsername().ToLower()}");
                    if (owner.ToLower() == GetCurrentUsername().ToLower()) {
                        _logger.LogInformation("Killing process with PID {PID}", process.Id);
                        process.Kill();
                    }
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
