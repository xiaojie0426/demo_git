using SimpleComposer.RCS;
using SimpleCore.Library;
using SimpleCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleCore.PropType;

namespace TrafficControlCore.TaskSystem
{
    public class Utils
    {

        public class ColumnTaskInfo
        {
            public enum ColumnType
            {
                NONE = -1,
                INFLOW = 0,
                OUTFLOW = 1,
            }
            public int id;
            public ColumnType curTaskType = ColumnType.NONE;
            public List<Site> siteList = new List<Site>(); // 按照点深度由浅到深保存
            public int availableFetch = 0;
            public int availablePut = 0;

            public void Init()
            {
                foreach (var site in siteList)
                {
                    if (site.tags.Contains("Goods"))
                        availableFetch++;
                    else
                        availablePut++;
                }
                if (availableFetch == siteList.Count)
                    curTaskType = ColumnType.OUTFLOW;
                if (availablePut == siteList.Count)
                    curTaskType = ColumnType.INFLOW;
            }

            public bool RequestSite(ColumnType type, out Site site)
            {
                site = null;
                //if (curTaskType != ColumnType.NONE && curTaskType != type)
                //    return false;

                if (type == ColumnType.INFLOW)
                {
                    if (CheckColumnFinished())
                    {
                        if (availablePut == 0)
                            curTaskType = ColumnType.OUTFLOW;
                        else
                            curTaskType = ColumnType.INFLOW;
                    }
                    if (availablePut == 0)
                        return false;

                    if (curTaskType != ColumnType.NONE && curTaskType != type)
                        return false;

                    availablePut -= 1;
                    availableFetch += 1;
                    site = siteList[availablePut];
                    return true;
                }

                else if (type == ColumnType.OUTFLOW)
                {
                    if (CheckColumnFinished())
                    {
                        if (availableFetch == 0)
                            curTaskType = ColumnType.INFLOW;
                        else
                            curTaskType = ColumnType.OUTFLOW;
                    }
                    if (availableFetch == 0)
                        return false;

                    if (curTaskType != ColumnType.NONE && curTaskType != type)
                        return false;

                    site = siteList[siteList.Count - availableFetch];
                    availableFetch -= 1;
                    availablePut += 1;

                    //if (availableFetch == 0)
                    //    curTaskType = ColumnType.INFLOW;
                    return true;
                }
                else
                {
                    return false;
                }

            }

            private bool CheckColumnFinished()
            {
                int realFetch = 0, realPut = 0;
                foreach (var site in siteList)
                {
                    if (site.tags.Contains("Goods"))
                        realFetch++;
                    else
                        realPut++;
                }
                if (realFetch == availableFetch && realPut == availablePut)
                    return true;
                else
                    return false;
            }


        }

    }
}
