/************************************************************************/
/* File:	  ExceptionManager.cs										*/
/* Func:	  异常处理机制，包含规划层面与执行层面                         */
/* Author:	  TODO                                                      */
/************************************************************************/
using SimpleCore;
using SimpleCore.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TrafficControlCore.Cars;
using TrafficControlCore.DispatchSystem.Graph;
using TrafficControlCore.TaskSystem;
using static System.Windows.Forms.AxHost;
using static TrafficControlCore.DispatchSystem.DispatchMisson;
using static TrafficControlCore.DispatchSystem.Graph.Node;
using static TrafficControlCore.DispatchSystem.Graph.Track;
namespace TrafficControlCore.DispatchSystem
{
    public class AgvExceptionManager
    {
        public SectorGraph Graph
        {
            get { return DispatchMisson.Graph; }
        }
        public void solveBlockAgent()
        {
            var agentList = AgentManager.agentList.Values;

            HashSet<int> visited = new HashSet<int>();
            HashSet<int> onStack = new HashSet<int>();
            HashSet<List<int>> cycles = new HashSet<List<int>>();

            foreach (var agent in agentList)
            {
                if (!visited.Contains(agent.Id))
                {
                    onStack.Clear();
                    FindCyclesUtil(agent, visited, onStack, cycles);
                }
            }
            foreach (var cycle in cycles)
            {
                if (cycle.Count > 1)
                {
                    var str = "Attention! DeadBlock: ";
                    foreach (var i in cycle)
                    {
                        str += i + " ";
                    }
                    Diagnosis.Post(str);
                }
            }
        }

        private static void FindCyclesUtil(Agent agent, HashSet<int> visited, HashSet<int> onStack, HashSet<List<int>> cycles)
        {
            visited.Add(agent.Id);
            onStack.Add(agent.Id);

            var nextAgent = AgentManager.getAgent(agent.DeadInducementAgentId);
            if (nextAgent != null)
            {
                if (!visited.Contains(nextAgent.Id))
                {
                    FindCyclesUtil(nextAgent, visited, onStack, cycles);
                }
                else if (onStack.Contains(nextAgent.Id))
                {
                    List<int> cycle = new List<int>();
                    int currentAgentId = nextAgent.Id;
                    do
                    {
                        cycle.Add(currentAgentId);
                        onStack.Remove(currentAgentId);
                        var currentAgent = AgentManager.getAgent(currentAgentId);
                        currentAgentId = currentAgent.DeadInducementAgentId;
                    } while (currentAgentId != nextAgent.Id);
                    // cycle.Add(nextAgent.Id);
                    cycles.Add(cycle);
                }
            }
            if (onStack.Contains(agent.Id)) onStack.Remove(agent.Id);
        }
        /*
        private bool releaseDeadAgentSuccess(int agentId)
        {
            var agent = AgentManager.getAgent(agentId);
            var node = NodeFinder.FindNearestNode(Graph, agent, agent.getLastHoldingLockId(), NodeFinder.FINDMODE.Dead);
            if (node == -1) return false;
            agent.addGoalinFront(node);
            return true;
        }
        */
        public void makeSureSafety()
        {
            Dictionary<int, int> node2agent = new Dictionary<int, int>();
            foreach (var agent in AgentManager.getAllAgents())
                node2agent.Add(agent.SiteID, agent.Id);

            foreach (var agent in AgentManager.getAllAgents())
            {
                if (isMakecollision(agent, node2agent))
                {
                    var car = agent.GetCar as SimulatorCarFourWay;
                    Diagnosis.Post(" agent.id: " + agent.Id + " clearTraffic. ");
                    car.ClearTraffic();
                }
            }
        }
        public bool isMakecollision(Agent agent, Dictionary<int, int> node2agent)
        {
            for (int i = 0; i < agent.HoldingLocksCount; ++i)
            {
                var nexPoint = agent.GetCar.status.holdingLocks[i];
                if (node2agent.ContainsKey(nexPoint) && node2agent[nexPoint] != agent.Id) return true;
                if (agent.have_goods && SimpleLib.GetSite(nexPoint).tags.Contains("Goods"))
                    return true;
                if (Graph.nodeDict[nexPoint].isDisabled == true)
                    return true;
            }
            return false;
        }
        private List<Node> ConsecutiveFastNodes = new List<Node>(); // 存储拥堵的连续高速路节点 
        private List<Node> CongestedStartNodes = new List<Node>(); // 存储拥堵起始节点的列表
        public int HighwayNodeCount = 5;//滑动窗口大小
        public int CongestCount = 4;//窗口中出现这么多车辆时认为拥堵
        /*public void HighwayOfOneDirection()
        {
            foreach (var node in Graph.nodeDict.Values)
            {
                if (node.type == NODE_TYPE.FAST)
                {
                    foreach (var track in SimpleLib.GetAllTracks())
                    {
                        Track newTrack;
                        if (track.siteA < track.siteB)
                            newTrack = new Track(track.id, track.siteA, track.siteB, track.direction);
                        else
                            newTrack = new Track(track.id, track.siteB, track.siteA, (track.direction >= 1 && track.direction <= 2) ? 3 - track.direction : track.direction);
                    }
                }

            }
        }*/
        public void HighwayOfOneDirection()
        {
            ConsecutiveFastNodes.Clear(); // 在开始之前清空列表  
            //Console.WriteLine("node.id==6318 处是否有小车" + Graph.nodeDict[6318].occupiedThisTime);
            foreach (var node in Graph.nodeDict.Values)
            {
                if (node.type == NODE_TYPE.FAST && !ConsecutiveFastNodes.Contains(node))//&& !consecutiveFastNodes.Contains(node)
                {
                    ConsecutiveFastNodes.Add(node); // 将高速路节点添加到列表中  

                    // 当遇到5个连续的高速路节点时  
                    if (ConsecutiveFastNodes.Count == HighwayNodeCount)
                    {
                        // 拥堵发生，记录第一个拥堵的节点
                        if (IsCongested(ConsecutiveFastNodes))
                        {
                            var startNode = ConsecutiveFastNodes[0];
                            if (!CongestedStartNodes.Contains(startNode))
                            {
                                CongestedStartNodes.Add(startNode);

                            }
                            UpdateTrackDirections(); // 更新路径方向,如果有四个以上小车，则将高速路由双向变为单向
                        }

                        // 使用滑动窗口的方式进行检查  移除列表中的第一个节点
                        ConsecutiveFastNodes.RemoveAt(0);
                    }
                }
                else
                {
                    // 如果节点不是高速路类型，则清空列表  
                    ConsecutiveFastNodes.Clear();
                }
                // 检查拥堵节点是否已解除拥堵，并恢复方向  
               
            }
            RestoreTrackDirections();
        }

        public bool IsCongested(List<Node> consecutiveNodes)
        {
            int occupiedCount = 0;
            //如果隔一段时间还有车再检测
            foreach (var node in consecutiveNodes)
            {
                if (node.occupiedThisTime == 1) // 1是有车  
                {
                    //Thread.Sleep(500);// 延迟500毫秒，即0.5秒
                   // if (node.occupiedThisTime == 1)
                        occupiedCount++;
                }
            }
            // 在这5个连续的高速路节点上至少有4辆小车  
            if (occupiedCount >= CongestCount)
            {
                return true;
            }
            else
                return false;

        }
        public void UpdateTrackDirections()
        {
            if(ConsecutiveFastNodes.Count >1)
            { for (int i = 0; i < ConsecutiveFastNodes.Count -1; i++)
                {
                    Node currentNode = ConsecutiveFastNodes[i];
                    Node nextNode = ConsecutiveFastNodes[i + 1];

                    foreach (var track in SimpleLib.GetAllTracks())
                    {
                        if ((track.siteA == currentNode.id && track.siteB == nextNode.id) || (track.siteB == currentNode.id && track.siteA == nextNode.id))
                        {
                            track.direction = 1; // 设置为从左往右
                            Console.WriteLine("方向已更改" + "当前trackId为：" + track.id + "左侧为："+track.siteA + "右侧为："+ track.siteB);
                            if (Graph.trackDict.ContainsKey(track.id))
                            {
                                Graph.trackDict[track.id].direction = 1;
                            }
                        }
                    }
                }
            }
        }
        public void RestoreTrackDirections()
        {
            if (CongestedStartNodes.Count != 0)
            {
                for (int start = 0; start < CongestedStartNodes.Count;)
                {
                    Console.WriteLine("该点实际有无车:");
                    var StorageCongestNode = new List<Node>();
                    bool allNodesExist = true;
                    for (int i = 0; i < HighwayNodeCount; i++)
                    {
                        /*var node = Graph.nodeDict[CongestedStartNodes[start].id + 2 * i];
                        if (node != null)
                        {
                            StorageCongestNode.Add(node);
                        }*/
                        Node node;
                        if (Graph.nodeDict.TryGetValue(CongestedStartNodes[start].id + 2 * i, out node))
                        {
                            StorageCongestNode.Add(node);
                        }
                        else
                        {
                            allNodesExist = false;
                            break;
                        }
                        Console.WriteLine(node.occupiedThisTime);
                    }
                    Console.WriteLine("拥堵起始节点:" + CongestedStartNodes[start].id + " 暂存区的大小是:" + StorageCongestNode.Count);
                    if (!IsCongested(StorageCongestNode))
                    {
                        for (int i = 0; i < StorageCongestNode.Count - 1; i++)
                        {
                            Node currentNode = StorageCongestNode[i];
                            Node nextNode = StorageCongestNode[i + 1];
                            foreach (var track in SimpleLib.GetAllTracks())
                            {
                                if (track.siteA == currentNode.id && track.siteB == nextNode.id ||
                                    track.siteB == currentNode.id && track.siteA == nextNode.id)
                                {
                                    track.direction = 0; // 还原为双向
                                    Console.WriteLine("方向已还原" + "当前trackId为：" + track.id + "左侧为：" + track.siteA + "右侧为：" + track.siteB);
                                    if (Graph.trackDict.ContainsKey(track.id))
                                    {
                                        Graph.trackDict[track.id].direction = 0;
                                       
                                    }
                                }
                            }
                        }
                        CongestedStartNodes.RemoveAt(start);
                    }
                    else
                    {
                        start++; // 如果仍然拥堵，继续检查下一个起始节点
                    }
                }
            }
        }
        //若过往拥堵点不再拥堵，则恢复双向路
        /* public void RestoreTrackDirections()
         {
             var StorageCongestNode = new List<Node>();//由起始拥堵节点获得整个滑动窗的节点并储存
             if (CongestedStartNodes.Count != 0)
             {
                 //congestedStartNodes[0] = Graph.nodeDict[congestedStartNodes[0].id];

                 for (int start = 0; start < CongestedStartNodes.Count; start++)//每次遍历所有拥堵路段的起始节点
                 {
                     Console.WriteLine("该点实际有无车:");
                     for (int i = 0; i < HighwayNodeCount; i++)
                     {
                         //foreach (var node in SimpleLib.GetAllSites())//如何获取当前地图的所有node节点？

                         foreach (var node in Graph.nodeDict.Values)
                         {
                             if (CongestedStartNodes[start].id + 2 * i == node.id)//不一定是从第一个开始解锁的
                                                                              //congestedStartNodes[0 + 2 * i] = Graph.nodeDict[congestedStartNodes[0 + 2 * i].id];
                             {
                                 StorageCongestNode.Add(Graph.nodeDict[node.id]);
                                 Console.WriteLine(node.occupiedThisTime);
                                 break;
                             }
                             //StorageCongestNode.Add(Graph.nodeDict[congestedStartNodes[0].id + 2 * i]);
                             //为什么它不会实时更新？？？？？？？
                         }
                     }
                     Console.WriteLine("拥堵起始节点:" + CongestedStartNodes[start].id + " 暂存区的大小是:" + StorageCongestNode.Count);
  //                   Console.WriteLine("拥堵起始节点:" + CongestedStartNodes[start].id  + " 暂存区的大小是:" + StorageCongestNode.Count + " 该点分别为" + StorageCongestNode[0].occupiedThisTime + StorageCongestNode[1].occupiedThisTime + StorageCongestNode[2].occupiedThisTime + StorageCongestNode[3].occupiedThisTime + StorageCongestNode[4].occupiedThisTime);
                     if (!IsCongested(StorageCongestNode))
                     {
                         for (int i = 0; i < StorageCongestNode.Count - 1; i++)
                         {
                             Node currentNode = StorageCongestNode[i];
                             Node nextNode = StorageCongestNode[i + 1];
                             Console.WriteLine("暂存区的数据" + StorageCongestNode[i].id);
                             foreach (var track in SimpleLib.GetAllTracks())
                             {
                                 if ((track.siteA == currentNode.id && track.siteB == nextNode.id) || (track.siteB == currentNode.id && track.siteA == nextNode.id))
                                 {
                                     track.direction = 0; // 还原为双向路
                                     Console.WriteLine("方向已还原");
                                 }
                             }

                         }
                         CongestedStartNodes.RemoveAt(start);
                         StorageCongestNode.Clear();//暂存区清空
                     }

                     StorageCongestNode.Clear();//暂存区清空
                 }
             }
         }*/
    }
}
