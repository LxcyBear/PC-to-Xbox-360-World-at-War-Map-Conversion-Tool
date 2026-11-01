using System.Buffers.Binary;

namespace WaW360.Core.FastFiles;

/// <summary>
/// Rebases source-owned static draw XModel pointers to the FINAL Stage4D
/// layout for the current FF emission. Never reuse pointers from a prior FF.
/// Unverified external references remain NULL and block hardware approval.
/// </summary>
public static class T4XenonStaticDrawLayoutRebaser
{
    public sealed record Draw(int Ordinal,int NativeRootIndex,
        bool SourceOwnedModelBodyPresent);
    public sealed record Result(byte[] Candidate,int SourceOwnedRelocated,
        int UnverifiedExternalDraws,int DistinctRootBodies);

    public static Result Rebase(byte[] source,int stride,int pointerOffset,
        IReadOnlyList<Draw> draws,IReadOnlyDictionary<int,uint> finalRootPointers)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(finalRootPointers);
        if(stride < 4 || pointerOffset < 0 || pointerOffset + 4 > stride ||
           (pointerOffset & 3)!=0 || draws.Count == 0 ||
           (long)stride*draws.Count != source.LongLength)
            throw new InvalidDataException("Invalid final-layout draw count or pointer offset.");

        var result=(byte[])source.Clone();
        var real=0;
        var unresolved=0;
        var roots=new HashSet<int>();
        for(var i=0;i<draws.Count;i++)
        {
            var item=draws[i];
            if(item.Ordinal!=i || item.NativeRootIndex<0)
                throw new InvalidDataException($"Draw #{i} has an invalid ordinal/root.");
            var at=checked(i*stride+pointerOffset);
            if(item.SourceOwnedModelBodyPresent)
            {
                if(!finalRootPointers.TryGetValue(item.NativeRootIndex,out var value) ||
                   value==0)
                    throw new InvalidDataException($"Source-owned model root #{item.NativeRootIndex} has no final pointer.");
                var pointer=T4ZonePointer.Decode(value);
                if(pointer.Kind!=T4ZonePointerKind.Offset ||
                   pointer.Block!=T4ZoneBlock.Virtual)
                    throw new InvalidDataException($"Model root #{item.NativeRootIndex} is not a VIRTUAL pointer.");
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at,4),value);
                roots.Add(item.NativeRootIndex);
                real++;
            }
            else
            {
                // Source pointer must stay empty; unresolved hardware dependency.
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at,4),0);
                unresolved++;
            }
        }
        for(var i=0;i<source.Length;i++)
            if(i%stride < pointerOffset || i%stride >= pointerOffset+4)
                if(source[i]!=result[i])
                    throw new InvalidDataException("Non-pointer scalar/geometry bytes changed.");
        return new Result(result,real,unresolved,roots.Count);
    }

    public static void SelfTest()
    {
        const int stride=48,offset=24;
        var source=new byte[stride*3];
        source[5]=0xAA;source[35]=0x80;
        var prior=T4ZonePointer.Encode(T4ZoneBlock.Virtual,0x1234);
        BinaryPrimitives.WriteUInt32BigEndian(source.AsSpan(offset,4),prior);
        var fresh=T4ZonePointer.Encode(T4ZoneBlock.Virtual,0x2234);
        var map=new Dictionary<int,uint>{{7,fresh},{8,T4ZonePointer.Encode(T4ZoneBlock.Virtual,0x3344)}};
        var result=Rebase(source,stride,offset,
            new[]{new Draw(0,7,true),new Draw(1,8,true),new Draw(2,9,false)},map);
        if(result.SourceOwnedRelocated!=2 || result.UnverifiedExternalDraws!=1 ||
           result.DistinctRootBodies!=2 ||
           BinaryPrimitives.ReadUInt32BigEndian(result.Candidate.AsSpan(offset,4))!=fresh ||
           result.Candidate[5]!=0xAA || result.Candidate[35]!=0x80 ||
           BinaryPrimitives.ReadUInt32BigEndian(result.Candidate.AsSpan(stride*2+offset,4))!=0)
            throw new InvalidDataException("Generic final-layout rebase self-test failed.");
    }
}
