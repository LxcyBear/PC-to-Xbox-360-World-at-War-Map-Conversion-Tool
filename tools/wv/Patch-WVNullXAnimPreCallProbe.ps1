param(
    [string]$WVRoot = "C:\Users\jorda\OneDrive\Desktop\My Xbox\X360DebuggerWV-master\X360DebuggerWV"
)

$ErrorActionPreference = "Stop"

$debugger = Join-Path $WVRoot "Debugger.cs"
if (-not (Test-Path -LiteralPath $debugger -PathType Leaf)) {
    throw "Debugger.cs not found: " + $debugger
}

$source = Get-Content -LiteralPath $debugger -Raw
$handlerMarker = "HandleNullXAnimBreak(EventInfo)"
$armMarker = "ArmNullXAnimProbe(EventInfo);"
$helperMarker = "private const uint NullXAnimPre = 0x8216858C;"

if (-not $source.Contains($armMarker)) {
    $needle = "            switch (EventCode)"
    if (-not $source.Contains($needle)) {
        throw "Could not find switch(EventCode) in Debugger.cs"
    }
    $insert = @'
            // Safe diagnostic: arm only after CODXE activates a usermap.
            ArmNullXAnimProbe(EventInfo);
'@
    $source = $source.Replace($needle, $insert + $needle)
}

if (-not $source.Contains($handlerMarker)) {
    $needle = "case XboxDebugEventType.ExecutionBreak:"
    $first = $source.IndexOf($needle)
    if ($first -lt 0 -or $source.IndexOf($needle, $first + 1) -ge 0) {
        throw "Expected exactly one ExecutionBreak case."
    }
    $insert = @'
case XboxDebugEventType.ExecutionBreak:
                    if (HandleNullXAnimBreak(EventInfo))
                        break;
'@
    $source = $source.Replace($needle, $insert)
}

$playMarker = @'
        public static void Play()
'@

$helpers = @'
        private const uint NullXAnimPre = 0x8216858C;
        private const uint NullXAnimPost = 0x82168590;
        private static bool nullXAnimArmed = false;
        private static bool nullXAnimPostPending = false;
        private static int nullXAnimCount = 0;
        private static string lastHealthyXAnim = "<none>";

        private static void ArmNullXAnimProbe(IXboxEventInfo e)
        {
            try
            {
                if (e == null || e.Info.Message == null)
                    return;
                if (e.Info.Message.IndexOf("Activated usermap:", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                uint[] old =
                {
                    0x82168570, 0x82168574, 0x82168594, 0x82168598,
                    0x82168900, 0x82169F30, 0x82169F34, NullXAnimPre, NullXAnimPost
                };
                foreach (uint a in old)
                {
                    try { jtag.DebugTarget.RemoveBreakpoint(a); } catch { }
                    try { breakPoints.Remove(a); } catch { }
                }

                nullXAnimCount = 0;
                lastHealthyXAnim = "<none>";
                nullXAnimPostPending = false;
                nullXAnimArmed = true;

                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xanim_name_probe.txt"),
                    "SAFE XANIM PRE-CALL PROBE " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                    Environment.NewLine,
                    Encoding.UTF8);

                AddBreakpoint(NullXAnimPre);
                Log.WriteLine("[WV:XANIM] Armed @0x8216858C; NULL name will stop BEFORE the AV.");
            }
            catch (Exception ex)
            {
                Log.WriteLine("[WV:XANIM] Arm error: " + ex.Message);
            }
        }

        private static bool HandleNullXAnimBreak(IXboxEventInfo e)
        {
            if (!nullXAnimArmed || e == null)
                return false;

            uint address;
            IXboxThread thread;
            try
            {
                address = e.Info.Address;
                thread = e.Info.Thread;
            }
            catch
            {
                return false;
            }

            if (address == NullXAnimPost && nullXAnimPostPending)
            {
                try { RemoveBreakpoint(NullXAnimPost); } catch { }
                if (!breakPoints.Contains(NullXAnimPre))
                    AddBreakpoint(NullXAnimPre);
                nullXAnimPostPending = false;
                Play();
                return true;
            }

            if (address != NullXAnimPre)
                return false;

            uint tid = 0;
            try { tid = thread.ThreadId; } catch { }

            try
            {
                IXboxStackFrame frame = thread.TopOfStack;
                frame.FlushRegisterChanges();

                int[] r32 = new int[5];
                long[] r64 = new long[33];
                for (int i = 0; i < 5; i++)
                    frame.GetRegister32((XboxRegisters32)i, out r32[i]);
                for (int i = 0; i < 33; i++)
                    frame.GetRegister64((XboxRegisters64)i, out r64[i]);

                uint asset = unchecked((uint)r64[4]); // R3
                byte[] pair = asset == 0 ? null : GetMemoryDump(asset, 8);
                uint type = pair != null && pair.Length >= 8 ? NullXAnimBe32(pair, 0) : 0xFFFFFFFFu;
                uint header = pair != null && pair.Length >= 8 ? NullXAnimBe32(pair, 4) : 0;

                if (type == 4)
                {
                    nullXAnimCount++;
                    byte[] h = header == 0 ? null : GetMemoryDump(header, 0x80);
                    uint namePtr = h != null && h.Length >= 4 ? NullXAnimBe32(h, 0) : 0;
                    string name = namePtr == 0 ? "<NULL>" : NullXAnimAscii(namePtr, 160);

                    string line =
                        "XANIM=" + nullXAnimCount.ToString("D4") +
                        " TID=0x" + tid.ToString("X8") +
                        " XAsset=0x" + asset.ToString("X8") +
                        " Header=0x" + header.ToString("X8") +
                        " NamePtr=0x" + namePtr.ToString("X8") +
                        " Name=\"" + name + "\"";

                    File.AppendAllText(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xanim_name_probe.txt"),
                        line + Environment.NewLine,
                        Encoding.UTF8);
                    Log.WriteLine("[WV:XANIM] " + line);

                    if (header == 0 || namePtr == 0)
                    {
                        CaptureNullXAnim(tid, r32, r64, asset, type, header, namePtr);
                        breakThreadId = tid;
                        refreshCPU = true;
                        isRunning = false;
                        Log.WriteLine("[WV:XANIM] CAUGHT NULL XANIM BEFORE 0x82159240. DO NOT CONTINUE.");
                        return true;
                    }

                    lastHealthyXAnim = name;
                }

                try { RemoveBreakpoint(NullXAnimPre); } catch { }
                if (!breakPoints.Contains(NullXAnimPost))
                    AddBreakpoint(NullXAnimPost);
                nullXAnimPostPending = true;
                Play();
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine("[WV:XANIM] Probe error: " + ex);
                breakThreadId = tid;
                refreshCPU = true;
                isRunning = false;
                return true;
            }
        }

        private static void CaptureNullXAnim(
            uint tid, int[] r32, long[] r64,
            uint asset, uint type, uint header, uint namePtr)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("============================================================");
            sb.AppendLine("NULL XANIM PRE-CALL PROBE");
            sb.AppendLine("Time      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.AppendLine("Stopped   : 0x8216858C BEFORE asset-name helper");
            sb.AppendLine("Thread ID : 0x" + tid.ToString("X8"));
            sb.AppendLine("XAnim #   : " + nullXAnimCount);
            sb.AppendLine("Previous healthy XAnim: " + lastHealthyXAnim);
            sb.AppendLine("XAsset    : 0x" + asset.ToString("X8"));
            sb.AppendLine("Type      : 0x" + type.ToString("X8") + " (XANIMPARTS)");
            sb.AppendLine("Header    : 0x" + header.ToString("X8"));
            sb.AppendLine("Name ptr  : 0x" + namePtr.ToString("X8"));

            sb.AppendLine();
            sb.AppendLine("REGISTERS");
            sb.AppendLine("MSR =0x" + unchecked((uint)r32[0]).ToString("X8"));
            sb.AppendLine("IAR =0x" + unchecked((uint)r32[1]).ToString("X8"));
            sb.AppendLine("LR  =0x" + unchecked((uint)r32[2]).ToString("X8"));
            sb.AppendLine("CR  =0x" + unchecked((uint)r32[3]).ToString("X8"));
            sb.AppendLine("XER =0x" + unchecked((uint)r32[4]).ToString("X8"));
            sb.AppendLine("CTR =0x" + unchecked((ulong)r64[0]).ToString("X16"));
            for (int i = 0; i < 32; i++)
                sb.AppendLine("R" + i.ToString("D2") + " =0x" +
                    unchecked((ulong)r64[i + 1]).ToString("X16"));

            NullXAnimMem(sb, "XASSET", asset, 0x80);
            NullXAnimMem(sb, "XANIM HEADER / TEMP", header, 0x200);

            uint sp = unchecked((uint)r64[2]);
            uint r6 = unchecked((uint)r64[7]);
            uint r26 = unchecked((uint)r64[27]);
            uint r31 = unchecked((uint)r64[32]);
            if (sp != 0) NullXAnimMem(sb, "STACK / R1", sp, 0x400);
            if (r6 != 0) NullXAnimMem(sb, "R6 CONTEXT", r6, 0x200);
            if (r26 != 0) NullXAnimMem(sb, "XFILE BLOCK TABLE / R26", r26, 0x100);
            if (r31 != 0) NullXAnimMem(sb, "R31 CONTEXT", r31, 0x100);
            NullXAnimMem(sb, "XASSET NAME DISPATCH TABLE", 0x824B3B48, 0xA0);
            sb.AppendLine("============================================================");

            string text = sb.ToString();
            File.WriteAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "null_xanim_probe.txt"),
                text,
                Encoding.UTF8);
            Log.Write(text);
        }

        private static uint NullXAnimBe32(byte[] b, int o)
        {
            return ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) |
                   ((uint)b[o + 2] << 8) | b[o + 3];
        }

        private static string NullXAnimAscii(uint address, uint max)
        {
            try
            {
                byte[] b = GetMemoryDump(address, max);
                int n = 0;
                while (n < b.Length && b[n] != 0) n++;
                return Encoding.ASCII.GetString(b, 0, n)
                    .Replace("\r", "\\r").Replace("\n", "\\n");
            }
            catch (Exception ex)
            {
                return "<READ-ERROR:" + ex.Message + ">";
            }
        }

        private static void NullXAnimMem(StringBuilder sb, string title, uint address, uint size)
        {
            sb.AppendLine();
            sb.AppendLine(title + " @0x" + address.ToString("X8") + " size=0x" + size.ToString("X"));
            if (address == 0)
            {
                sb.AppendLine("<NULL>");
                return;
            }

            try
            {
                byte[] b = GetMemoryDump(address, size);
                for (int i = 0; i < b.Length; i += 16)
                {
                    sb.Append("0x" + (address + (uint)i).ToString("X8") + ": ");
                    int row = Math.Min(16, b.Length - i);
                    for (int j = 0; j < row; j++)
                        sb.Append(b[i + j].ToString("X2") + " ");
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("MEMORY READ ERROR: " + ex.Message);
            }
        }

'@

if (-not $source.Contains($helperMarker)) {
    if (-not $source.Contains($playMarker)) {
        throw "Could not find Play() insertion marker."
    }
    $source = $source.Replace($playMarker, $helpers + $playMarker)
}
else {
    Write-Host "[WV:XANIM] Helper block already present."
}

$backup = $debugger + ".before-null-xanim-precall-probe.bak"
if (-not (Test-Path -LiteralPath $backup)) {
    Copy-Item -LiteralPath $debugger -Destination $backup -Force
}

Set-Content -LiteralPath $debugger -Value $source -Encoding UTF8

Write-Host "[WV:XANIM] SAFE PRE-CALL PROBE INSTALLED"
Write-Host "     Arms automatically on CODXE usermap activation."
Write-Host "     Healthy assets auto-continue."
Write-Host "     NULL XAnim remains stopped at 0x8216858C BEFORE the crash."
Write-Host "     Send xanim_name_probe.txt + null_xanim_probe.txt after it stops."
