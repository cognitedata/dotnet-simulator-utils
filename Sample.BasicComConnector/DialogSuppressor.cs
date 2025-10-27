using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// Automatically dismisses Windows MessageBox dialogs that appear during simulation execution.
/// This is a last-resort solution when VBA/DLL code shows dialogs that can't be suppressed via Excel COM.
/// </summary>
public class DialogSuppressor : IDisposable
{
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Thread _monitorThread;
    private bool _isRunning;

    // Windows API imports
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // Windows message constants
    private const uint WM_CLOSE = 0x0010;
    private const uint BM_CLICK = 0x00F5;

    public DialogSuppressor(ILogger logger)
    {
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
        _monitorThread = new Thread(MonitorAndCloseDialogs)
        {
            IsBackground = true,
            Name = "DialogSuppressorThread"
        };
    }

    /// <summary>
    /// Start monitoring for and automatically dismissing dialogs
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _monitorThread.Start();
        _logger.LogDebug("Dialog suppressor started - will auto-dismiss error dialogs");
    }

    /// <summary>
    /// Stop monitoring for dialogs
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cancellationTokenSource.Cancel();
        
        if (_monitorThread.IsAlive)
        {
            _monitorThread.Join(TimeSpan.FromSeconds(2));
        }
        
        _logger.LogDebug("Dialog suppressor stopped");
    }

    private void MonitorAndCloseDialogs()
    {
        var token = _cancellationTokenSource.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Look for common dialog window classes and titles
                CloseDialogIfFound("#32770", null); // Standard Windows dialog class
                CloseDialogIfFound(null, "Microsoft Excel"); // Excel dialogs
                CloseDialogIfFound(null, "PlaceiT"); // PlaceiT-specific dialogs
                
                // Check for common error dialog patterns
                CloseDialogByTitlePattern("Error");
                CloseDialogByTitlePattern("Warning");
                CloseDialogByTitlePattern("gridding");
                CloseDialogByTitlePattern("rounding");
                
                // Sleep briefly to avoid consuming too much CPU
                Thread.Sleep(100); // Check every 100ms
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in dialog suppressor monitoring loop");
            }
        }
    }

    private void CloseDialogIfFound(string? className, string? windowTitle)
    {
        IntPtr hwnd = FindWindow(className, windowTitle);
        
        if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
        {
            // Get the window text for logging
            StringBuilder windowText = new StringBuilder(256);
            GetWindowText(hwnd, windowText, 256);
            
            if (!string.IsNullOrWhiteSpace(windowText.ToString()))
            {
                _logger.LogWarning($"Auto-dismissing dialog: '{windowText}'");
                
                // Try to find and click OK button first (preferred)
                if (ClickButtonInDialog(hwnd, "OK") || 
                    ClickButtonInDialog(hwnd, "&OK") ||
                    ClickButtonInDialog(hwnd, "Yes") ||
                    ClickButtonInDialog(hwnd, "&Yes"))
                {
                    _logger.LogDebug("Clicked OK/Yes button on dialog");
                    return;
                }
                
                // If no button found, send WM_CLOSE
                SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                _logger.LogDebug("Sent WM_CLOSE to dialog");
            }
        }
    }

    private void CloseDialogByTitlePattern(string pattern)
    {
        // This is a simplified approach - in production you'd enumerate all windows
        // For now, we'll just try common combinations
        IntPtr hwnd = IntPtr.Zero;
        
        // Try to find any visible dialog with the pattern in the title
        for (int i = 0; i < 10; i++) // Check up to 10 windows
        {
            StringBuilder sb = new StringBuilder(256);
            hwnd = FindWindow("#32770", null);
            
            if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
            {
                GetWindowText(hwnd, sb, 256);
                string title = sb.ToString();
                
                if (title.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _logger.LogWarning($"Auto-dismissing dialog with pattern '{pattern}': '{title}'");
                    
                    // Try clicking OK button
                    if (ClickButtonInDialog(hwnd, "OK") || 
                        ClickButtonInDialog(hwnd, "&OK"))
                    {
                        return;
                    }
                    
                    // Otherwise close it
                    SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
        }
    }

    private bool ClickButtonInDialog(IntPtr dialogHwnd, string buttonText)
    {
        // Find button by text
        IntPtr buttonHwnd = FindWindowEx(dialogHwnd, IntPtr.Zero, "Button", buttonText);
        
        if (buttonHwnd != IntPtr.Zero && IsWindowVisible(buttonHwnd))
        {
            // Bring dialog to foreground and click button
            SetForegroundWindow(dialogHwnd);
            SendMessage(buttonHwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        
        return false;
    }

    public void Dispose()
    {
        Stop();
        _cancellationTokenSource?.Dispose();
        GC.SuppressFinalize(this);
    }
}

