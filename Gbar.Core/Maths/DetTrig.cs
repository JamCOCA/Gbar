namespace Gbar.Core.Maths;

/// <summary>
/// 确定性三角函数：只用 +−×÷ 与 <see cref="Math.Floor"/>（IEEE-754 精确定义的运算），
/// 不使用 <see cref="Math.Sin(double)"/> 等超越函数（.NET 不保证跨平台逐 bit 一致）。
/// 供天体轨道解析等需要服务器/客户端逐 bit 一致的路径使用。
/// 实现：按对称性归约到 [0, π/4] 后 Taylor 展开（Horner 形式），截断误差 ~1e-18。
/// </summary>
public static class DetTrig
{
    public const double Pi = 3.14159265358979323846264338327950288;
    public const double TwoPi = 6.28318530717958647692528676655900576;
    public const double HalfPi = 1.57079632679489661923132169163975144;
    private const double QuarterPi = 0.785398163397448309615660845819875721;

    /// <summary>sin(x)，x 为弧度，结果与平台无关、逐 bit 确定。</summary>
    public static double Sin(double x)
    {
        x -= TwoPi * Math.Floor(x / TwoPi + 0.5);   // 归约到 [−π, π]
        var sign = 1.0;
        if (x < 0) { x = -x; sign = -1.0; }          // sin 奇对称 → [0, π]
        if (x > HalfPi) x = Pi - x;                  // sin(π−x) = sin(x) → [0, π/2]
        var r = x > QuarterPi ? CosPoly(HalfPi - x) : SinPoly(x);
        return sign * r;
    }

    /// <summary>cos(x)，x 为弧度，结果与平台无关、逐 bit 确定。</summary>
    public static double Cos(double x)
    {
        x -= TwoPi * Math.Floor(x / TwoPi + 0.5);   // 归约到 [−π, π]
        if (x < 0) x = -x;                           // cos 偶对称 → [0, π]
        var sign = 1.0;
        if (x > HalfPi) { x = Pi - x; sign = -1.0; } // cos(π−x) = −cos(x) → [0, π/2]
        var r = x > QuarterPi ? SinPoly(HalfPi - x) : CosPoly(x);
        return sign * r;
    }

    /// <summary>把相位（弧度）折回 [0, 2π)。</summary>
    public static double WrapPhase(double x) => x - TwoPi * Math.Floor(x / TwoPi);

    // [0, π/4] 上 sin 的 Taylor 展开（x·P(x²)，Horner），下一项 x¹⁹/19! ≈ 8e-20
    private static double SinPoly(double x)
    {
        var x2 = x * x;
        return x * (1.0 + x2 * (-1.0 / 6.0 + x2 * (1.0 / 120.0 + x2 * (-1.0 / 5040.0
            + x2 * (1.0 / 362880.0 + x2 * (-1.0 / 39916800.0 + x2 * (1.0 / 6227020800.0
            + x2 * (-1.0 / 1307674368000.0 + x2 * (1.0 / 355687428096000.0)))))))));
    }

    // [0, π/4] 上 cos 的 Taylor 展开（P(x²)，Horner），下一项 x¹⁸/18! ≈ 2e-18
    private static double CosPoly(double x)
    {
        var x2 = x * x;
        return 1.0 + x2 * (-1.0 / 2.0 + x2 * (1.0 / 24.0 + x2 * (-1.0 / 720.0
            + x2 * (1.0 / 40320.0 + x2 * (-1.0 / 3628800.0 + x2 * (1.0 / 479001600.0
            + x2 * (-1.0 / 87178291200.0 + x2 * (1.0 / 20922789888000.0))))))));
    }
}
