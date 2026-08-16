namespace Gbar.Core.Maths;

/// <summary>
/// 定点数换算工具：坐标/速度以 long 存储，缩放 1:<see cref="Scale"/>（两位小数）。
/// 加减与比较直接用 long 运算；乘除经此类保持缩放正确；浮点只在边界转换。
/// </summary>
public static class Fix
{
    public const long Scale = 100;

    public static long Mul(long a, long b) => (long)((Int128)a * b / Scale);

    public static long Div(long a, long b) => (long)((Int128)a * Scale / b);

    public static long FromDouble(double v) => (long)Math.Round(v * Scale);

    public static long FromInt(long v) => v * Scale;

    public static double ToDouble(long v) => v / (double)Scale;
}
