param(
    [string]$WVRoot = "C:\Users\jorda\OneDrive\Desktop\My Xbox\X360DebuggerWV-master\X360DebuggerWV"
)

$ErrorActionPreference = "Stop"

$debugger = Join-Path $WVRoot "Debugger.cs"
if (-not (Test-Path -LiteralPath $debugger -PathType Leaf)) {
    throw "Debugger.cs not found: $debugger"
}

$source = Get-Content -LiteralPath $debugger -Raw

# Be tolerant of local WV edits. Match the entire Exception switch section by
# structure, not by exact upstream text, and stop at the next XboxDebugEventType
# case label so unrelated custom code is preserved.
$newCase = @'
                case XboxDebugEventType.Exception:
                {
                    // Capture the COM event fields BEFORE FreeEventInfo.
                    uint exceptionAddress = EventInfo.Info.Address;
                    uint exceptionThreadId = 0;
                    IXboxThread exceptionThread = null;
                    string exceptionMessage = null;
                    try
                    {
                        exceptionThread = EventInfo.Info.Thread;
                        if (exceptionThread != null)
                            exceptionThreadId = exceptionThread.ThreadId;
                    }
                    catch { }
                    try { exceptionMessage = EventInfo.Info.Message; } catch { }

                    if (exceptionMessage != null)
                        Log.WriteLine("Exception @0x" + exceptionAddress.ToString("X8") + " TID=0x" + exceptionThreadId.ToString("X8") + " \"" + exceptionMessage + "\"");
                    else
                        Log.WriteLine("Exception @0x" + exceptionAddress.ToString("X8") + " TID=0x" + exceptionThreadId.ToString("X8"));

                    CaptureFirstExceptionContext(exceptionAddress, exceptionThreadId, exceptionThread);

                    try { jtag.DebugTarget.FreeEventInfo(EventInfo.Info); } catch { }
                    breakThreadId = exceptionThreadId;
                    refreshCPU = true;
                    isRunning = false;
                    break;
                }
'@

$exceptionPattern = '(?ms)^[ \t]*case\s+XboxDebugEventType\.Exception\s*:\s*.*?(?=^[ \t]*case\s+XboxDebugEventType\.)'
$exceptionMatches = [regex]::Matches($source, $exceptionPattern)

if ($exceptionMatches.Count -eq 0) {
    if ($source.Contains("CaptureFirstExceptionContext(exceptionAddress, exceptionThreadId)")) {
        Write-Host "[WV] Exception handler already patched."
    }
    else {
        throw "Could not locate the XboxDebugEventType.Exception switch section in: $debugger"
    }
}
elseif ($exceptionMatches.Count -ne 1) {
    throw "Expected exactly one XboxDebugEventType.Exception switch section, found $($exceptionMatches.Count)."
}
else {
    $exceptionRegex = [regex]::new($exceptionPattern)
    $source = $exceptionRegex.Replace($source, $newCase, 1)
}


$playMarker = @'
        public static void Play()
'@

$helpers = @'
        private static readonly object exceptionCaptureLock = new object();
        private static bool firstExceptionCaptured = false;

        private static void CaptureFirstExceptionContext(uint exceptionAddress, uint threadId, IXboxThread exceptionThread)
        {
            lock (exceptionCaptureLock)
            {
                if (firstExceptionCaptured)
                    return;
                firstExceptionCaptured = true;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("============================================================");
            sb.AppendLine("FIRST EXCEPTION CONTEXT");
            sb.AppendLine("Time      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.AppendLine("Address   : 0x" + exceptionAddress.ToString("X8"));
            sb.AppendLine("default.xex RVA (base 0x82000000): 0x" +
                (exceptionAddress >= 0x82000000 ? (exceptionAddress - 0x82000000).ToString("X8") : "????????"));
            sb.AppendLine("Thread ID : 0x" + threadId.ToString("X8"));

            try
            {
                IXboxThread thread = exceptionThread;

                // Fall back to enumeration only if the event did not retain a thread object.
                if (thread == null)
                {
                    int n = GetThreadIndexById(threadId);
                    if (n >= 0)
                        thread = jtag.DebugTarget.Threads[n];
                }

                if (thread == null)
                {
                    sb.AppendLine("ERROR: no faulting IXboxThread object was available.");
                }
                else
                {
                    IXboxStackFrame frame = thread.TopOfStack;
                    frame.FlushRegisterChanges();

                    int[] r32 = new int[5];
                    long[] r64 = new long[33];

                    for (int i = 0; i < 5; i++)
                        frame.GetRegister32((XboxRegisters32)i, out r32[i]);
                    for (int i = 0; i < 33; i++)
                        frame.GetRegister64((XboxRegisters64)i, out r64[i]);

                    sb.AppendLine();
                    sb.AppendLine("REGISTERS");
                    sb.AppendLine("MSR =0x" + unchecked((uint)r32[0]).ToString("X8"));
                    sb.AppendLine("IAR =0x" + unchecked((uint)r32[1]).ToString("X8"));
                    sb.AppendLine("LR  =0x" + unchecked((uint)r32[2]).ToString("X8"));
                    sb.AppendLine("CR  =0x" + unchecked((uint)r32[3]).ToString("X8"));
                    sb.AppendLine("XER =0x" + unchecked((uint)r32[4]).ToString("X8"));
                    sb.AppendLine("CTR =0x" + unchecked((ulong)r64[0]).ToString("X16"));

                    for (int i = 0; i < 32; i++)
                    {
                        sb.AppendLine(
                            "R" + i.ToString("D2") +
                            " =0x" + unchecked((ulong)r64[i + 1]).ToString("X16"));
                    }

                    uint iar = unchecked((uint)r32[1]);
                    uint lr = unchecked((uint)r32[2]);
                    uint sp = unchecked((uint)r64[2]); // r64[0]=CTR, [1]=R0, [2]=R1

                    AppendMemoryWindow(sb, "CODE AROUND IAR", iar >= 0x80 ? iar - 0x80 : 0, 0x200);
                    if (lr != 0)
                        AppendMemoryWindow(sb, "CODE AROUND LR", lr >= 0x40 ? lr - 0x40 : 0, 0x100);
                    if (sp != 0)
                        AppendMemoryWindow(sb, "STACK AROUND R1", sp, 0x400);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine();
                sb.AppendLine("CONTEXT CAPTURE ERROR: " + ex);
            }

            try
            {
                sb.AppendLine();
                sb.AppendLine("MODULES");
                foreach (IXboxModule mod in jtag.DebugTarget.Modules)
                {
                    try
                    {
                        sb.AppendLine(
                            "0x" + mod.ModuleInfo.BaseAddress.ToString("X8") +
                            "  " + mod.ModuleInfo.FullName);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("MODULE ENUM ERROR: " + ex.Message);
            }

            sb.AppendLine("============================================================");

            string text = sb.ToString();
            try
            {
                string path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "first_exception_context.txt");
                File.WriteAllText(path, text, Encoding.UTF8);
            }
            catch { }

            Log.Write(text);
        }

        private static void AppendMemoryWindow(StringBuilder sb, string title, uint address, uint size)
        {
            sb.AppendLine();
            sb.AppendLine(title + " @0x" + address.ToString("X8") + " size=0x" + size.ToString("X"));

            try
            {
                byte[] data = GetMemoryDump(address, size);
                for (int i = 0; i < data.Length; i += 16)
                {
                    sb.Append("0x" + (address + (uint)i).ToString("X8") + ": ");
                    int row = Math.Min(16, data.Length - i);
                    for (int j = 0; j < row; j++)
                        sb.Append(data[i + j].ToString("X2") + " ");
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("MEMORY READ ERROR: " + ex.Message);
            }
        }

'@
$helperPattern = '(?ms)^[ \t]*private static readonly object exceptionCaptureLock\s*=.*?(?=^[ \t]*public static void Play\(\))'
$helperMatches = [regex]::Matches($source, $helperPattern)

if ($helperMatches.Count -gt 1) {
    throw "Expected at most one first-exception helper block, found $($helperMatches.Count)."
}
elseif ($helperMatches.Count -eq 1) {
    $helperRegex = [regex]::new($helperPattern)
    $source = $helperRegex.Replace($source, $helpers, 1)
    Write-Host "[WV] Upgraded existing first-exception helper methods."
}
else {
    if (-not $source.Contains($playMarker)) {
        throw "Play() insertion marker not found."
    }
    $source = $source.Replace($playMarker, $helpers + $playMarker)
}

$backup = $debugger + ".before-first-exception-capture.bak"
if (-not (Test-Path -LiteralPath $backup)) {
    Copy-Item -LiteralPath $debugger -Destination $backup -Force
}

Set-Content -LiteralPath $debugger -Value $source -Encoding UTF8

Write-Host "[WV] FIRST-EXCEPTION CONTEXT PATCH INSTALLED"
Write-Host "     Source:  $debugger"
Write-Host "     Backup:  $backup"
Write-Host "     Runtime: <WV exe folder>\first_exception_context.txt"
Write-Host "              <WV exe folder>\watson_log.txt"
