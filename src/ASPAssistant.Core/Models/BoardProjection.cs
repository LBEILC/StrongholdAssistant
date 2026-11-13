// 改编自 bingyunhe/asp-assistant-arknights（Apache-2.0），2026-11-13：仅保留棋盘投影，去掉部署模型。
namespace ASPAssistant.Core.Models;

public enum BoardCamera { Collapsed, Expanded }

public sealed class BoardProjection
{
    private readonly double[] _h; // 按行存储的 3×3 矩阵

    public BoardProjection(IReadOnlyList<((double Col, double Row) Grid, (double X, double Y) Screen)> corners)
    {
        if (corners.Count != 4) throw new ArgumentException("需要 4 组对应点", nameof(corners));
        var a = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            var ((c, r), (x, y)) = corners[i];
            double[] r1 = [c, r, 1, 0, 0, 0, -x * c, -x * r, x];
            double[] r2 = [0, 0, 0, c, r, 1, -y * c, -y * r, y];
            for (int k = 0; k < 9; k++) { a[2 * i, k] = r1[k]; a[2 * i + 1, k] = r2[k]; }
        }
        for (int i = 0; i < 8; i++)
        {
            int p = i;
            for (int k = i + 1; k < 8; k++) if (Math.Abs(a[k, i]) > Math.Abs(a[p, i])) p = k;
            for (int k = 0; k < 9; k++) (a[i, k], a[p, k]) = (a[p, k], a[i, k]);
            for (int k = 0; k < 8; k++)
            {
                if (k == i) continue;
                double f = a[k, i] / a[i, i];
                for (int j = i; j < 9; j++) a[k, j] -= f * a[i, j];
            }
        }
        _h = new double[9];
        for (int i = 0; i < 8; i++) _h[i] = a[i, 8] / a[i, i];
        _h[8] = 1;
    }

    public (double X, double Y) Project(double col, double row) => Apply(_h, col, row);

    private static (double, double) Apply(double[] m, double u, double v)
    {
        double w = m[6] * u + m[7] * v + m[8];
        return ((m[0] * u + m[1] * v + m[2]) / w, (m[3] * u + m[4] * v + m[5]) / w);
    }

}

