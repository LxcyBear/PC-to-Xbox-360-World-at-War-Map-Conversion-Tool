using System.Security.Cryptography;
using System.Text.Json;
using WaW360.Core.FastFiles;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: Gate10DrawProbe <light-candidate-summary.json> <root-relocation-summary.json> <verified-Stage4D-layout.json>");
    return 2;
}

try
{
    T4XenonStaticDrawLayoutRebaser.SelfTest();
    T4XenonAztecGate10DrawProbe.SelfTestPowerShellUtf8Bom();
    var probe=await T4XenonAztecGate10DrawProbe.LoadAsync(args[0],args[1]);
    using var lightDoc=JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
    var light=lightDoc.RootElement;
    using var layoutDoc=JsonDocument.Parse(await File.ReadAllTextAsync(args[2]));
    var layout=layoutDoc.RootElement;
    if(layout.GetProperty("NativeRoots").GetInt32()!=8509 ||
       layout.GetProperty("RealXModelRoots").GetInt32()!=28 ||
       !layout.GetProperty("LayoutReady").GetBoolean())
        throw new InvalidDataException("Baseline 8509-root 28-real-XModel Stage4D layout not verified.");

    var roots=new Dictionary<int,uint>();
    foreach (var row in layout.GetProperty("RootInsertLayouts").EnumerateArray())
    {
        var idx=row.GetProperty("RootIndex").GetInt32();
        if(!roots.TryAdd(idx,row.GetProperty("StablePointer").GetUInt32()))
            throw new InvalidDataException("Duplicate Stage4D root layout index.");
    }

    var rebased=probe.RebaseForLayout(roots);
    var sha=Convert.ToHexString(SHA256.HashData(rebased));
    var expected=light.GetProperty("candidateSha256").GetString();
    if(!sha.Equals(expected,StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException(
            $"Current baseline rebase changed valid original-PC scalar/owner candidate: {sha}, expected {expected}");

    Console.WriteLine("[AZTEC-GATE10-REBASE] PASS: generic shifted layout synthetic test.");
    Console.WriteLine("[AZTEC-GATE10-REBASE] PASS: 1902/1902 real source-owned XModel* rebased on the verified 8509-root baseline.");
    Console.WriteLine("[AZTEC-GATE10-REBASE] 1 external stock draw remains NULL and unapproved for Xbox.");
    Console.WriteLine("[AZTEC-GATE10-REBASE] 1903 x 60 byte exact source scalar/geometry SHA: "+sha);
    Console.WriteLine("[AZTEC-GATE10-REBASE] Still 0/1903 FF static draw records physically linked. No FF, no hardware deployment.");
    return 0;
}
catch(Exception e)
{
    Console.Error.WriteLine("[AZTEC-GATE10-REBASE] FAIL: "+e.Message);
    return 1;
}
