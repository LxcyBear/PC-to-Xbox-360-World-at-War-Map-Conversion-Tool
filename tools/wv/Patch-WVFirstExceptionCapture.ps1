param(
    [string]$WVRoot = "C:\Users\jorda\OneDrive\Desktop\My Xbox\X360DebuggerWV-master\X360DebuggerWV"
)

$ErrorActionPreference = "Stop"

$debugger = Join-Path $WVRoot "Debugger.cs"
if (-not (Test-Path -LiteralPath $debugger -PathType Leaf)) {
    throw "Debugger.cs not found: $debugger"
}

$source = Get-Content -LiteralPath $debugger -Raw

$oldCase = @'
                case XboxDebugEventType.Exception:
                    jtag.DebugTarget.FreeEventInfo(EventInfo.Info);
                    if (EventInfo.Info.Message != null)
                        Log.WriteLine("Exception @0x" + EventInfo.Info.Address.ToString("X8") + " \"" + EventInfo.Info.Message + "\"");
                    else
                        Log.WriteLine("Exception @0x" + EventInfo.Info.Address.ToString("X8"));
                    breakThreadId = EventInfo.Info.Thread.ThreadId;
                    refreshCPU = true;
                    isRunning = false;
                    break;
'@

$newCase = @'
                case XboxDebugEventType.Exception:
                    // Capture the event fields BEFORE FreeEventInfo. The original WV
                    // freed the COM event structure first and then read Address/Thread.
                    uint exceptionAddress = EventInfo.Info.Address;
                    uint exceptionThreadId = 0;
                    string exceptionMessage = null;
                    try { exceptionThreadId = EventInfo.Info.Thread.ThreadId; } catch { }
                    try { exceptionMessage = EventInfo.Info.Message; } catch { }

                    if (exceptionMessage != null)
                        Log.WriteLine("Exception @0x" + exceptionAddress.ToString("X8") + " TID=0x" + exceptionThreadId.ToString("X8") + " \"" + exceptionMessage + "\"");
                    else
                        Log.WriteLine("Exception @0x" + exceptionAddress.ToString("X8") + " TID=0x" + exceptionThreadId.ToString("X8"));

                    CaptureFirstExceptionContext(exceptionAddress, exceptionThreadId);

                    try { jtag.DebugTarget.FreeEventInfo(EventInfo.Info); } catch { }
                    breakThreadId = exceptionThreadId;
                    refreshCPU = true;
                    isRunning = false;
                    break;
'@

if (-not $source.Contains($oldCase)) {
    throw "Expected Exception case was not found. Debugger.cs may already be patched or differ from the upstream WV source."
}

$source = $source.Replace($oldCase, $newCase)

$playMarker = @'
        public static void Play()
'@

$helpers = @'
        private static readonly object exceptionCaptureLock = new object();
        private static bool firstExceptionCaptured = false;

        private static void CaptureFirstExceptionContext(uint exceptionAddress, uint threadId)
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
                int n = GetThreadIndexById(threadId);
                if (n < 0)
                {
                    sb.AppendLine("ERROR: faulting thread was not present in DebugTarget.Threads.");
                }
                else
                {
                    int[] r32 = GetThreadRegisters32(n);
                    long[] r64 = GetThreadRegisters64(n);

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
                        XBOX_MODULE_INFO info = mod.ModuleInfo;
                        sb.AppendLine(
                            "0x" + info.BaseAddress.ToString("X8") +
                            "  " + info.FullName);
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

            // Also mirror the context into the persistent Watson log.
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

if (-not $source.Contains($playMarker)) {
    throw "Play() insertion marker not found."
}
$source = $source.Replace($playMarker, $helpers + $playMarker)

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
