using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleComposer;
using SimpleComposer.RCS;
using SimpleComposer.UI;
using SimpleCore;
using SimpleCore.Library;
using SimpleCore.PropType;
using TrafficControlCore.DispatchSystem.Graph;
using TrafficControlCore.TaskSystem;
using TrafficControlCore.AvoidSystem;
using static TrafficControlCore.DispatchSystem.DispatchMisson;
using TrafficControlCore.Utils;

namespace TrafficControlCore.DispatchSystem
{
    public class NodeFinder
    {
        static Dictionary<int, int> G = new Dictionary<int, int>();
        static List<int> openList = new List<int>();  //定义开放列表，存放待探索的点
        static List<int> closeList = new List<int>();  //定义封闭列表，存放已确定的路线点

        public enum FINDMODE
        {
            Hidden = 0,
            Storage = 1,
            Charge = 2// ,
            /*Avoid = 3,
            Dead = 4*/
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn int FindNearestNodeForHidden(SectorGraph Graph, Agent agent, int startNodeId, List<int> lockList)
        ///
        /// @brief  找到一个合法的距离当前位置最近的避让点
        /// 
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public static int FindNearestNode(SectorGraph Graph, Agent agent, int startNodeId, FINDMODE Mode = FINDMODE.Hidden)
        {
            // 寻求从startNodeId出发的最近的包含在goalList内的节点
            G = new Dictionary<int, int>();
            openList = new List<int>();  //定义开放列表，存放待探索的点
            closeList = new List<int>();  //定义封闭列表，存放已确定的路线点
            int INF = 2147483647;
            G.Add(startNodeId, INF);
            openList.Add(startNodeId);           //将起始点添加至开放列表
            MinHeap openListHeap = new MinHeap();//最小堆的初始化
            openListHeap.Insert(startNodeId, G[startNodeId]);//openlist的节点嵌入最小堆

            int searchDepth = 0;
            //开始循环搜索路径
            while (openList.Count > 0)
            {
                // 搜索深度
                searchDepth++;
                if (searchDepth < -1)
                {
                    // 达到最大搜索深度，退出循环
                    Console.WriteLine("Find Path Fail. Reached Maximum Search Depth.");
                    return -1;
                }
                int curNodeId = openList[0];   //选择当前探索的节点，openList列表的首位
                /*
                //对openList中的节点进行遍历，选择F值最小的节点作为当前节点
                for (int i = 1; i < openList.Count; i++)
                {
                    if (G[openList[i]] < G[curNodeId])
                        curNodeId = openList[i];
                }
                */
                //优先队列选择openlist中F值最小的节点作为当前节点                       
                (curNodeId, _) = openListHeap.ExtractMin();

                //在openList中移除当前节点，并添加到closeList中
                openList.Remove(curNodeId);
                Node curNode = Graph.nodeDict[curNodeId];
                if (!closeList.Contains(curNodeId))
                    closeList.Add(curNodeId);

                if (Mode == FINDMODE.Hidden)
                {
                    if (isHiddenNode(Graph, agent, startNodeId, curNodeId) == true) return curNodeId;
                }
                else if (Mode == FINDMODE.Storage)
                {
                    if (isStorageNode(Graph, agent, startNodeId, curNodeId) == true) return curNodeId;
                }
                else if (Mode == FINDMODE.Charge)
                {
                    if (isChargeNode(Graph, agent, startNodeId, curNodeId) == true) return curNodeId;
                }/*
                else if (Mode == FINDMODE.Dead)
                {
                    if (isDeadNode(Graph, agent, startNodeId, curNodeId) == true) return curNodeId;
                    // return curNodeId;
                }
                else if (Mode == FINDMODE.Avoid)
                {
                    if (isAvoidNode(Graph, agent, startNodeId, curNodeId) == true) return curNodeId;
                }*/
                // 隐藏点必须没有小车隐藏，且不是某任务的终点，且内部不存在活跃的小车，且block没有被锁住

                //从一条边的两个点来判断当前节点的相邻扩展节点
                foreach (var neighborId in curNode.neighBor)
                {
                    var neighbor = Graph.nodeDict[neighborId];
                    if (checkConnection(Graph, curNodeId, neighborId, agent, Mode) == false) continue;
                    if (agent.have_goods 
                        && SimpleLib.GetSite(neighborId).tags.Contains("Goods")
                        && neighbor.isPortAndType == 0) continue;
                    if (closeList.Contains(neighborId)) continue;
                    //计算每个节点的代价
                    if (G.ContainsKey(neighborId) == false)
                    {
                        G.Add(neighborId, INF);
                    }

                    if (G[neighborId] > G[curNodeId] + CalculateManhattanDistance(curNode, neighbor))
                    {
                        G[neighborId] = G[curNodeId] + CalculateManhattanDistance(curNode, neighbor);
                        if (!openList.Contains(neighborId) && !closeList.Contains(neighborId))
                        {
                            openList.Add(neighborId);
                            openListHeap.Insert(neighborId, G[neighborId]);//openlist每次add的节点嵌入最小堆
                        }
                    }
                }
            }
            return -1;
        }
        /*
        public static int FindAvoidNode(SectorGraph Graph, int nodeId, List<int> stopNodeIdList, List<int> inVaildNodeIdList, bool haveGoods = true)
        {
            string parameters = $"Start nodeId: {nodeId}, stopNodeIdList: {string.Join(", ", stopNodeIdList)}, inVaildNodeIdList: {string.Join(", ", inVaildNodeIdList)}, haveGoods: {haveGoods}";
            Diagnosis.Post(parameters);

            int maxSearchDepth = 20;
            List<int> avoidNodeIdList = new List<int>();
            HashSet<int> visited = new HashSet<int>();
            Queue<(int nodeId, int depth)> queue = new Queue<(int, int)>();
            queue.Enqueue((nodeId, 0));

            while (queue.Count > 0)
            {
                var (currentNodeId, depth) = queue.Dequeue();
                visited.Add(currentNodeId);
                var currentNode = Graph.nodeDict[currentNodeId];

                // Graph.sectorDict[currentNode.sectorId].executingTaskNum <= 0
                if (currentNode.type == Node.NODE_TYPE.SHELF && !inVaildNodeIdList.Contains(currentNodeId))
                {
                    if (!haveGoods || (haveGoods && !SimpleLib.GetSite(currentNodeId).tags.Contains("Goods")))
                    {
                        avoidNodeIdList.Add(currentNodeId);
                    }
                }

                if (depth < maxSearchDepth)
                {
                    foreach (int neighborId in currentNode.neighBor)
                    {
                        if (Graph.CommandLockSites.Contains(neighborId))
                            continue;

                        if (stopNodeIdList.Contains(neighborId))
                        {
                            continue;
                        }
                        if (!visited.Contains(neighborId))
                        {
                            queue.Enqueue((neighborId, depth + 1));
                        }
                    }
                }
            }

            List<int> finalAvoidNodeIdList = new List<int>();

            // 预订货架的区域不能设置为避让点
            // List<int> reservedGoodsSectorIdList = new List<int>();
            // foreach (var node in _nodeIdToNode.Values)
            // {
            //     if (node.ReservedGoods)
            //     {
            //         reservedGoodsSectorIdList.Add(node.SectorId);
            //     }
            // }

            foreach (int id in avoidNodeIdList)
            {
                // if (!reservedGoodsSectorIdList.Contains(Node(id).SectorId) && Node(id).HideRobotId < 0)
                // {
                //     finalAvoidNodeIdList.Add(id);
                // }
                finalAvoidNodeIdList.Add(id);
            }

            if (finalAvoidNodeIdList.Count == 0)
            {
                return -1;
            }
            return finalAvoidNodeIdList[0];
        }
        */
        private static int CalculateManhattanDistance(Node nodeA, Node nodeB)
        {
            return (int)(Math.Abs(nodeA.x - nodeB.x) + Math.Abs(nodeA.y - nodeB.y));
        }
        public static int CalculateManhattanDistance(SectorGraph Graph, int nodeAId, int nodeBId)
        {
            Node nodeA = Graph.nodeDict[nodeAId];
            Node nodeB = Graph.nodeDict[nodeBId];
            return (int)(Math.Abs(nodeA.x - nodeB.x) + Math.Abs(nodeA.y - nodeB.y));
        }
        private static bool checkConnection(SectorGraph Graph, int curId, int nexId, Agent agent, FINDMODE Mode = FINDMODE.Hidden)
        {
            var nexNode = Graph.nodeDict[nexId];
            if (nexNode.isDisabled == true) return false;

            var trackId = Graph.findTrackId(curId, nexId);
            if (trackId == -1) return false;
            if (Graph.trackDict[trackId].direction == 3) return false;
            if (Graph.trackDict[trackId].direction == 1 && curId > nexId) return false;
            if (Graph.trackDict[trackId].direction == 2 && curId < nexId) return false;
            /*
            if (Mode == FINDMODE.Dead)
            {
                if (Graph.CommandLockSites.Contains(nexId))
                {
                    var block_agent_Id = Graph.nodeDict[nexId].commandLockAgent;
                    if (block_agent_Id != agent.Id) return false;
                }
            }
            */
            return true;    //  否则一定满足
        }

        private static bool isHiddenNode(SectorGraph Graph, Agent agent, int startNodeId, int curNodeId)
        {
            var curNode = Graph.nodeDict[curNodeId];
            if (Graph.nodeId2SectorId.ContainsKey(curNodeId))
            {
                var curSector = Graph.sectorDict[curNode.sectorId];
                if (curNode.canHidden == 1 && 
                    (curNode.hiddenCar == agent.Id || curNode.hiddenCar == -1) 
                    && curNode.isCharged == false
                    )
                {
                    if (curSector.id == Graph.nodeDict[startNodeId].sectorId)
                    // 如果当前sectorId和起点sectorId保持一致，说明在同一个sector内部
                    {
                        if (curSector.checkOtherActiveAgent(agent.Id) == false)
                        {
                            if ((curSector.isLocked == 0 || (curSector.beLockedCar == agent.Id)))
                            {
                                if (curSector.executingTaskNum == 0) // 没有任务会进入这个sector
                                    return true;
                                if (Graph.checkBlockTheHighway(curNodeId, agent.Id) == true) // 如果挡住了 出不去，则只能在这个sector内部
                                    return true;
                            }

                        }
                        else
                        {
                            // 存在其他的activeAgent，需要判断此agent到目标点是否会被阻挡
                            if (curSector.checkConflictAgent2Point(agent.Id, curNode.id) == false)
                            {
                                return true;
                            }
                        }
                    }
                    else
                    // 当前sectorId和起点sectorId不保持一直，说明一定要经过跨sector调度
                    {
                        if (curSector.executingTaskNum == 0
                            && curSector.checkOtherActiveAgent(agent.Id) == false
                            && (curSector.isLocked == 0 || (curSector.beLockedCar == agent.Id)))
                        {
                            // 判断：没有任务终点属于该sector，不存在其他活跃sector，没有锁
                            return true;
                        }
                    }
                }
                // return curNodeId;
            }
            // 隐藏点必须没有小车隐藏，且不是某任务的终点，且内部不存在活跃的小车，且block没有被锁住
            return false;
        }
        private static bool isStorageNode(SectorGraph Graph, Agent agent, int startNodeId, int curNodeId)
        {
            if (Graph.nodeId2SectorId.ContainsKey(curNodeId))
            {
                var sectorId = Graph.nodeId2SectorId[curNodeId];
                if (tmpStorageHelper.storageRes.ContainsKey(sectorId) == true)
                {
                    if (tmpStorageHelper.storageRes[sectorId] > 0) return true;
                }
            }
            return false;
        }
        private static bool isChargeNode(SectorGraph Graph, Agent agent, int startNodeId, int curNodeId)
        {
            if (ChargeManager.chargePileList.ContainsKey(curNodeId) == false) return false;
            if (ChargeManager.chargePileList[curNodeId].occupiedAgent != -1) return false;
            return true;
        }

        /*
        private static bool isAvoidNode(SectorGraph Graph, Agent agent, int startNodeId, int curNodeId)
        {
            if (AvoidManager.hideNodeTable.Keys.Contains(curNodeId) == true
                && AvoidManager.hideNodeTable[curNodeId] != agent.Id)
            {
                
                return false;
            }
            if (Graph.nodeDict[curNodeId].type == Node.NODE_TYPE.SHELF)
            {
                if (!agent.have_goods || (agent.have_goods && !SimpleLib.GetSite(curNodeId).tags.Contains("Goods")))
                {
                    return true;
                }
                if (ChargeManager.chargePileList[curNodeId].occupiedAgent != -1) return false;
                return true;
            }
            return false;
        }
        private static bool isDeadNode(SectorGraph Graph, Agent agent, int startNodeId, int curNodeId)
        {
            if (Graph.keyPointNode.Contains(curNodeId)) return false;
            if (Graph.CommandLockSites.Contains(curNodeId)) return false;
            return true;
        }
        */
    }
}