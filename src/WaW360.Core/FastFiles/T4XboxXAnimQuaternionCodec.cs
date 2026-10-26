namespace WaW360.Core.FastFiles;

/// <summary>
/// Xbox 360 T4/IW-era quaternion codec used by the 12-bucket console XAnim layout.
///
/// NORMAL_QUAT is one 32-bit word:
///   signed 9/10/10-bit ratios + 2-bit rotation + sign.
///
/// PRECISION_QUAT is one 48-bit word serialized as three 16-bit words:
///   signed 15/15/15-bit ratios + 2-bit rotation + sign.
///
/// The omitted component is the largest-magnitude quaternion component. The
/// stored ratios are the other three components divided by that component.
/// </summary>
public static class T4XboxXAnimQuaternionCodec
{
    public readonly record struct Quat(double X, double Y, double Z, double W);

    public readonly record struct PrecisionWord(ushort Word0, ushort Word1, ushort Word2)
    {
        public ulong Packed48 =>
            ((ulong)Word0 << 32) |
            ((ulong)Word1 << 16) |
            Word2;
    }

    public static uint EncodeNormal(short x, short y, short z, short w)
    {
        var q = NormalizeFromShorts(x, y, z, w);
        var basis = BuildRatioBasis(q);

        var r0 = QuantizeSigned(basis.R0, 255, 9);
        var r1 = QuantizeSigned(basis.R1, 511, 10);
        var r2 = QuantizeSigned(basis.R2, 511, 10);

        uint packed =
            (uint)r0 |
            ((uint)r1 << 9) |
            ((uint)r2 << 19) |
            ((uint)basis.Rotation << 29);
        if (basis.NegativeOmitted)
            packed |= 0x80000000u;
        return packed;
    }

    public static PrecisionWord EncodePrecision(short x, short y, short z, short w)
    {
        var q = NormalizeFromShorts(x, y, z, w);
        var basis = BuildRatioBasis(q);

        ulong r0 = (uint)QuantizeSigned(basis.R0, 16383, 15);
        ulong r1 = (uint)QuantizeSigned(basis.R1, 16383, 15);
        ulong r2 = (uint)QuantizeSigned(basis.R2, 16383, 15);

        ulong packed =
            r0 |
            (r1 << 15) |
            (r2 << 30) |
            ((ulong)basis.Rotation << 45);
        if (basis.NegativeOmitted)
            packed |= 1UL << 47;

        return new PrecisionWord(
            (ushort)(packed >> 32),
            (ushort)(packed >> 16),
            (ushort)packed);
    }

    public static Quat DecodeNormal(uint packed)
    {
        var r0 = SignExtend((int)(packed & 0x1FF), 9) / 255.0;
        var r1 = SignExtend((int)((packed >> 9) & 0x3FF), 10) / 511.0;
        var r2 = SignExtend((int)((packed >> 19) & 0x3FF), 10) / 511.0;
        var rotation = (int)((packed >> 29) & 3);
        var sign = (packed & 0x80000000u) == 0 ? 1.0 : -1.0;
        return NormalizeAndRotate(r0, r1, r2, rotation, sign);
    }

    public static Quat DecodePrecision(ushort word0, ushort word1, ushort word2)
    {
        ulong packed =
            ((ulong)word0 << 32) |
            ((ulong)word1 << 16) |
            word2;

        var r0 = SignExtend((int)(packed & 0x7FFF), 15) / 16383.0;
        var r1 = SignExtend((int)((packed >> 15) & 0x7FFF), 15) / 16383.0;
        var r2 = SignExtend((int)((packed >> 30) & 0x7FFF), 15) / 16383.0;
        var rotation = (int)((packed >> 45) & 3);
        var sign = (packed & (1UL << 47)) == 0 ? 1.0 : -1.0;
        return NormalizeAndRotate(r0, r1, r2, rotation, sign);
    }

    public static Quat NormalizeFromShorts(short x, short y, short z, short w)
    {
        double dx = x / 32767.0;
        double dy = y / 32767.0;
        double dz = z / 32767.0;
        double dw = w / 32767.0;
        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw);
        if (!(length > 0.0) || !double.IsFinite(length))
            return new Quat(0.0, 0.0, 0.0, 1.0);
        return new Quat(dx / length, dy / length, dz / length, dw / length);
    }

    public static double AngularErrorDeg(Quat a, Quat b)
    {
        var dot = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W);
        dot = Math.Clamp(dot, 0.0, 1.0);
        return 2.0 * Math.Acos(dot) * (180.0 / Math.PI);
    }

    private readonly record struct RatioBasis(
        double R0,
        double R1,
        double R2,
        int Rotation,
        bool NegativeOmitted);

    private static RatioBasis BuildRatioBasis(Quat q)
    {
        Span<double> values = stackalloc double[4] { q.X, q.Y, q.Z, q.W };

        var omitted = 0;
        var maxAbs = Math.Abs(values[0]);
        for (var i = 1; i < 4; i++)
        {
            var abs = Math.Abs(values[i]);
            if (abs > maxAbs)
            {
                omitted = i;
                maxAbs = abs;
            }
        }

        var denominator = values[omitted];
        if (Math.Abs(denominator) < 1e-20)
            return new RatioBasis(0.0, 0.0, 0.0, 0, false);

        var rotation = (3 - omitted) & 3;

        // Decoder emits:
        //   q = [v[rotation], v[rotation+1], v[rotation+2], v[rotation+3]]
        // where v = [r0*inv, r1*inv, r2*inv, inv].
        // Therefore recover v[0..2] from q by the inverse rotation, then divide
        // by v[3] (the omitted component).
        Span<double> v = stackalloc double[4];
        for (var j = 0; j < 4; j++)
            v[j] = values[(j - rotation) & 3];

        return new RatioBasis(
            Math.Clamp(v[0] / denominator, -1.0, 1.0),
            Math.Clamp(v[1] / denominator, -1.0, 1.0),
            Math.Clamp(v[2] / denominator, -1.0, 1.0),
            rotation,
            denominator < 0.0);
    }

    private static int QuantizeSigned(double value, int scale, int bits)
    {
        var clamped = Math.Clamp(value, -1.0, 1.0);
        var signed = (int)Math.Round(clamped * scale, MidpointRounding.AwayFromZero);
        signed = Math.Clamp(signed, -scale, scale);
        return signed & ((1 << bits) - 1);
    }

    private static Quat NormalizeAndRotate(
        double r0,
        double r1,
        double r2,
        int rotation,
        double sign)
    {
        var inverseLength = sign / Math.Sqrt(r0 * r0 + r1 * r1 + r2 * r2 + 1.0);
        Span<double> values = stackalloc double[4]
        {
            r0 * inverseLength,
            r1 * inverseLength,
            r2 * inverseLength,
            inverseLength
        };

        return new Quat(
            values[rotation],
            values[(rotation + 1) & 3],
            values[(rotation + 2) & 3],
            values[(rotation + 3) & 3]);
    }

    private static int SignExtend(int value, int bits)
    {
        var sign = 1 << (bits - 1);
        return (value ^ sign) - sign;
    }
}
