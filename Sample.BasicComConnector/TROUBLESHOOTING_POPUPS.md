# Troubleshooting PlaceiT Pop-up Dialogs

If you're still seeing pop-up dialogs after implementing all the `DisplayAlerts = false` settings, the pop-ups are **NOT** coming from Excel, but from:

## 1. PlaceiT VBA Code Using MsgBox

The PlaceiT VBA macro might be showing error dialogs directly using `MsgBox`.

### To Fix:
Open the PlaceiT Excel file and check the `PlaceiTCOMEntryPoint` VBA function:

```vba
Function PlaceiTCOMEntryPoint(...) As Variant
    ' ADD THIS AT THE TOP:
    On Error GoTo ErrorHandler
    
    ' ... existing simulation code ...
    
    ' On success:
    PlaceiTCOMEntryPoint = Array(0, resultArray)
    Exit Function
    
ErrorHandler:
    ' REPLACE ANY MsgBox CALLS WITH ERROR CODE RETURN
    ' Instead of: MsgBox "Gridding error occurred"
    ' Do this:
    PlaceiTCOMEntryPoint = Array(1, Null)  ' Return error code 1
End Function
```

### Check for These Patterns in VBA:
- `MsgBox "..."`  ← Replace with error code return
- `InputBox "..."` ← Remove, return error instead  
- `Application.DisplayAlerts = True` ← Remove or set to False
- Missing `On Error GoTo ErrorHandler` ← Add it!

## 2. PlaceiT DLLs Showing Native Windows Dialogs

The PlaceiT DLLs (`Ekc3220.dll`, `Ekc6420.dll`) or executable (`Ekag20nt.exe`) might be showing native Windows MessageBox dialogs.

### To Fix:
You need to:

**Option A: Contact PlaceiT vendor**
- Ask for a "silent mode" or "batch mode" configuration
- Request a version that returns error codes instead of showing dialogs
- Check if there's a configuration file (`.sif`, `.ini`, etc.) to disable GUI mode

**Option B: Modify PlaceiT Configuration**
- Check the `Prg2.sif` file in the PlaceiT package
- Look for settings like:
  ```
  SILENT_MODE=TRUE
  GUI_MODE=FALSE
  SHOW_ERRORS=FALSE
  ```

**Option C: Advanced - Windows Message Hook (NOT RECOMMENDED)**
- Install a Windows hook to intercept and auto-dismiss MessageBox calls
- This is complex and fragile, only as a last resort

## 3. How to Diagnose Which Source

### Test 1: Check VBA Code
1. Extract the PlaceiT .zip file manually
2. Open `PlaceiT v7.3.3.xlsm` in Excel
3. Press `Alt+F11` to open VBA editor
4. Find the `PlaceiTCOMEntryPoint` function
5. Search for `MsgBox` in the code (Ctrl+F)

### Test 2: Run Manually
1. Open the PlaceiT Excel file
2. Open VBA editor (`Alt+F11`)
3. In Immediate window (Ctrl+G), run:
   ```vba
   Application.DisplayAlerts = False
   result = PlaceiTCOMEntryPoint(0.1, 4000, 50, 0, Array(0.05,1000), 0, 20000, Array(1,1,1,0,0,0,0,0,0,0,0,0), Array(8,24,130,0,0,0,0,0,0,0,0,0), Array(0.1,4,0.08,0,0,0,0,0,0,0,0,0), 90, 5, 2, 100, Array(4,8))
   ```
4. If you still see the pop-up, it's from VBA or DLLs

### Test 3: Check for DLL Dialogs
If the pop-up has a Windows system icon (⚠️ warning symbol) and standard Windows buttons, it's from the PlaceiT DLLs, not Excel.

## Current Implementation

We've implemented **three layers** of dialog suppression:

### Layer 1: Server-level (in `NewSimClient.ConfigureExcelVisibility`)
```csharp
Server.DisplayAlerts = false;
Server.Interactive = false;
Server.EnableEvents = false;
```

### Layer 2: Workbook-level (in `NewSimClient.OpenBook`)
```csharp
app.DisplayAlerts = false;
app.AlertBeforeOverwriting = false;
app.Interactive = false;
app.AskToUpdateLinks = false;
```

### Layer 3: Pre-call level (in `NewSimRoutine.CallPlaceiTCOMEntryPoint`)
```csharp
application.DisplayAlerts = false;
application.Interactive = false;
application.EnableEvents = false;
application.ScreenUpdating = false;
application.ErrorCheckingOptions.BackgroundChecking = false;
```

**If pop-ups still appear after all this**, they are **definitely** from PlaceiT VBA/DLLs, not Excel.

## Recommended Solution

The **best fix** is to modify the PlaceiT VBA code to:
1. Add `On Error GoTo ErrorHandler` at the top of all functions
2. Remove all `MsgBox` calls
3. Return error codes instead of showing dialogs
4. Ensure all sub-procedures also have error handling

This way:
- ✅ No pop-ups block execution
- ✅ Error codes are returned to C#
- ✅ C# logs the errors with full context
- ✅ Subsequent simulations can proceed

## Need More Help?

If you can share the PlaceiT VBA code (the `PlaceiTCOMEntryPoint` function), I can help you modify it to properly handle errors without showing dialogs.

