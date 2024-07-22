using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrafficControlCore.DispatchSystem.Graph;
using TrafficControlCore.DispatchSystem;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TreeView;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;
using SimpleCore.Library;

namespace TrafficControlCore.Utils
{
    public static class Geometry
    {
        public static int Manhattan(SectorGraph Graph, Node nodeA, Node nodeB)
        {
            if (nodeA.layout == nodeB.layout)//两个节点在同一层
            {

                var siteup_or_down_A = nodeA;
                var siteup_or_down_B = nodeB;
                //nodeA&B也可能没有对应的sector
                if ((Graph.nodeId2SectorId.ContainsKey(nodeA.id)==false)||(Graph.nodeId2SectorId.ContainsKey(nodeB.id) == false))
                {
                    return (int)(Math.Abs(nodeA.x - nodeB.x) + Math.Abs(nodeA.y - nodeB.y));

                }
                //获取A的siteup&down处的内容
                var Sector_A = Graph.sectorDict[nodeA.sectorId];
                var Sector_B = Graph.sectorDict[nodeB.sectorId];
                
                if (Sector_A.type == Sector.SECTOR_TYPE.HIGHWAY || Sector_B.type == Sector.SECTOR_TYPE.HIGHWAY)
                    return (int)(Math.Abs(nodeA.x - nodeB.x) + Math.Abs(nodeA.y - nodeB.y));

                if ((Graph.nodeDict.ContainsKey(Sector_A.siteUp) == true))//  && Sector_A.siteUp!=-1)
                {
                    var siteUpdict_A = Graph.nodeDict[Sector_A.siteUp]; //获取siteup处的内容
                    if (Graph.nodeDict.ContainsKey(Sector_A.siteDown) == true)
                    {
                        var siteDowndict_A = Graph.nodeDict[Sector_A.siteDown];//获取sitedown处的内容
                        siteup_or_down_A = (Math.Abs(siteUpdict_A.y - nodeA.y) > Math.Abs(siteDowndict_A.y - nodeA.y)) ? siteUpdict_A : siteDowndict_A;
                    }
                    else
                    {
                        siteup_or_down_A = siteUpdict_A;
                    }
                }
                else
                {
                    if (Graph.nodeDict.ContainsKey(Sector_A.siteDown) == true )
                    {
                        var siteDowndict_A = Graph.nodeDict[Sector_A.siteDown];//获取sitedown处的内容
                        siteup_or_down_A = siteDowndict_A;
                    }
                }

                //获取B的siteup&down处的内容
                if (Graph.nodeDict.ContainsKey(Sector_B.siteUp) == true)
                {
                    var siteUpdict_B = Graph.nodeDict[Sector_B.siteUp]; //获取siteup处的内容
                    if (Graph.nodeDict.ContainsKey(Sector_B.siteDown) == true)
                    {
                        var siteDowndict_B = Graph.nodeDict[Sector_B.siteDown];//获取sitedown处的内容
                        siteup_or_down_B = (Math.Abs(siteUpdict_B.y - nodeB.y) > Math.Abs(siteDowndict_B.y - nodeB.y)) ? siteUpdict_B : siteDowndict_B;
                    }
                    else
                    {
                        siteup_or_down_B = siteUpdict_B;
                    }
                }
                else//B的siteup == -1 
                {
                    //B的sitedown必须 ！=-1
                    if (Graph.nodeDict.ContainsKey(Sector_B.siteDown) == true)
                    {
                        var siteDowndict_B = Graph.nodeDict[Sector_B.siteDown];//获取sitedown处的内容
                        siteup_or_down_B = siteDowndict_B;
                    }
                }

                //判断节点A&B取上还是下高速路点
                //var A = (Math.Abs(siteUpdict_A.y - nodeA.y) > Math.Abs(siteDowndict_A.y - nodeA.y)) ? siteUpdict_A : siteDowndict_A;
                //var B = (Math.Abs(siteUpdict_B.y - nodeA.y) > Math.Abs(siteDowndict_B.y - nodeA.y)) ? siteUpdict_B : siteDowndict_B;
                return (int)(Math.Abs(siteup_or_down_A.x - siteup_or_down_B.x) 
                    + Math.Abs(siteup_or_down_A.y - siteup_or_down_B.y)
                    + Math.Abs(siteup_or_down_A.y - nodeA.y)
                    + Math.Abs(siteup_or_down_B.y - nodeB.y));

            }
            return Math.Abs(Graph.MaxX - nodeA.x) + Math.Abs(Graph.MaxX - nodeB.x) + Math.Abs(nodeA.y - nodeB.y);

        }
    }}
/*
             if (nodeA.layout == nodeB.layout)//两个节点在同一层
            {
               
                return (int)(Math.Abs(nodeA.x - nodeB.x) + Math.Abs(nodeA.y - nodeB.y));

            }
                      
            return Math.Abs(Graph.MaxX - nodeA.x) + Math.Abs(Graph.MaxX - nodeB.x) + Math.Abs(nodeA.y - nodeB.y);
 */