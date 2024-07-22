using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrafficControlCore.DispatchSystem
{
    public struct KMConst
    {
        public const int MaxN = 1000;
        public const int INF = 1000000000 + 7;
        public const int NPOS = -1;
    }

    public class KmMatrix
    {

        public int[,] WeightMatrix = new int[KMConst.MaxN, KMConst.MaxN];
        public int[] Lx = new int[KMConst.MaxN];
        public int[] Ly = new int[KMConst.MaxN];
        public int[] MatchY = new int[KMConst.MaxN];
        public int[] VisX = new int[KMConst.MaxN];
        public int[] VisY = new int[KMConst.MaxN];
        public int[] Slack = new int[KMConst.MaxN];
        public int Nx { get; set; }
        public int Ny { get; set; }

        protected void Memset(int[] arr, int ini)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = ini;
            }
        }

    }

    /// <summary>
    /// 使用km算法返回最带权匹配
    /// </summary>
    public class KuhnMunkres : KmMatrix
    {
        public KuhnMunkres(int x, int y)
        {
            Nx = x;
            Ny = y;
        }

        bool Find(int x)
        {
            VisX[x] = 1;
            for (int y = 0; y < Ny; y++)
            {
                if (VisY[y] == 1)
                    continue;
                int t = Lx[x] + Ly[y] - WeightMatrix[x, y];
                if (t == 0)
                {
                    VisY[y] = 1;
                    if (MatchY[y] == -1 || Find(MatchY[y]))
                    {
                        MatchY[y] = x;
                        return true;//找到增广轨
                    }
                }
                else if (Slack[y] > t)
                    Slack[y] = t;
            }
            return false;  //没有找到增广轨（说明顶点x没有对应的匹配，与完备匹配(相等子图的完备匹配)不符）
        }

        public int KM()  //返回最优匹配的值
        {
            int i, j;
            Memset(MatchY, -1);
            Memset(Ly, 0);
            for (i = 0; i < Nx; i++)
                for (j = 0, Lx[i] = -KMConst.INF; j < Ny; j++)
                {
                    // Console.WriteLine("Lx Length: " + Lx.Count() + ", Index: " + i + ", W Shape: (" + WeightMatrix.GetLength(0) + ", " + WeightMatrix.GetLength(1) + "), Index: (" + i + ", " + j + ").");
                    // Console.WriteLine("Ny");
                    if (WeightMatrix[i, j] > Lx[i])
                        Lx[i] = WeightMatrix[i, j];
                }
            for (int x = 0; x < Nx; x++)
            {
                for (i = 0; i < Ny; i++)
                    Slack[i] = KMConst.INF;
                while (true)
                {
                    Memset(VisX, 0);
                    Memset(VisY, 0);
                    if (Find(x))//找到增广轨，退出
                        break;
                    int d = KMConst.INF;
                    for (i = 0; i < Ny; i++)//没找到，对l做调整(这会增加相等子图的边)，重新找
                    {
                        if (!(VisY[i] != 0) && d > Slack[i])
                            d = Slack[i];
                    }
                    for (i = 0; i < Nx; i++)
                    {
                        if (VisX[i] != 0)
                            Lx[i] -= d;
                    }
                    for (i = 0; i < Ny; i++)
                    {
                        if (VisY[i] != 0)
                            Ly[i] += d;
                        else
                            Slack[i] -= d;
                    }
                }
            }
            int result = 0;
            for (i = 0; i < Ny; i++)
                if (MatchY[i] > -1)
                    result += WeightMatrix[MatchY[i], i];
            return result;
        }
    }

}
