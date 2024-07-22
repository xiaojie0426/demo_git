using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using SimpleCore;
using SimpleCore.Library;
using SimpleCore.PropType;
using TrafficControlCore.DispatchSystem.Graph;
using static TrafficControlCore.DispatchSystem.Graph.Node;
using static TrafficControlCore.DispatchSystem.DispatchMisson;
using System.Linq;
using TrafficControlCore.Utils;

namespace TrafficControlCore.DispatchSystem
{
    //最小堆实现优先队列
    /*public class MinHeap
    {
        private List<(int id, int fScore)> heap;
        public MinHeap()
        {
            heap = new List<(int id, int fScore)>();
        }
        //嵌入
        public void Insert(int id, int fScore)
        {
            heap.Add((id, fScore));
            HeapifyUp(heap.Count - 1);
        }
        //提取最小id
        public (int id, int fScore) ExtractMin()
        {
            if (heap.Count == 0) throw new InvalidOperationException("Heap is empty.");

            (int id, int fScore) min = heap[0];
            heap[0] = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
            HeapifyDown(0);
            return min;
        }
        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                if (heap[parentIndex].fScore <= heap[index].fScore)
                    break;

                (heap[parentIndex], heap[index]) = (heap[index], heap[parentIndex]);
                index = parentIndex;
            }
        }
        private void HeapifyDown(int index)
        {
            int size = heap.Count;
            int leftChildIndex = 2 * index + 1;
            int rightChildIndex = 2 * index + 2;
            int smallestIndex = index;

            if (leftChildIndex < size && heap[leftChildIndex].fScore < heap[smallestIndex].fScore)
                smallestIndex = leftChildIndex;

            if (rightChildIndex < size && heap[rightChildIndex].fScore < heap[smallestIndex].fScore)
                smallestIndex = rightChildIndex;

            if (smallestIndex != index)
            {
                (heap[index], heap[smallestIndex]) = (heap[smallestIndex], heap[index]);
                HeapifyDown(smallestIndex);
            }
        }
    }
    */
    public static class SectorAstar
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn List<int> FindPath(SectorGraph Graph, int startNodeId, int goalNodeId, List<int> lockList, Agent agent)
        ///
        /// @brief  根据 reservation and lock 规划路径
        /// @param  startNodeId      起点
        /// @param  goalNodeId       终点
        /// @param  lockList         不可途径点
        /// @param  agent            规划agent
        /// 
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public static List<int> FindPath(SectorGraph Graph, int startNodeId, int goalNodeId, List<int> lockList, Agent agent, bool isAllowPlanDisabled, int isAvoidFinder_level = 0)
        {
            // 地图：包含点和边（目前包含对应的id）
            // 需要给节点添加属性：G值，H值，父节点
            // 包含openlist和closelist

            List<int> path = new List<int>();      //添加起始点和目标点
            Dictionary<int, int> G = new Dictionary<int, int>();
            Dictionary<int, int> F = new Dictionary<int, int>();
            Dictionary<int, int> H = new Dictionary<int, int>();
            Dictionary<int, int> parent = new Dictionary<int, int>();
            Dictionary<int, int> Depth = new Dictionary<int, int>();
            // int busyAlpha = 20000;

            if (isAvoidFinder_level == 0 && 
                (Graph.nodeDict.ContainsKey(startNodeId) == false || Graph.nodeDict.ContainsKey(goalNodeId) == false))
            {
                Diagnosis.Post("startNodeId is invalid, agentID: " + agent.Id + " startNodeId: " + startNodeId + " goalNodeId: " + goalNodeId);
                return null;
                // throw new Exception("startNodeId is invalid, agentID: " + agent.Id + " startNodeId: " + startNodeId + " goalNodeId: " + goalNodeId);

            }

            Node startNode = Graph.nodeDict[startNodeId];
            Node goalNode = null;
            if (Graph.nodeDict.ContainsKey(goalNodeId))
                goalNode = Graph.nodeDict[goalNodeId];

            List<int> openList = new List<int>();  //定义开放列表，存放待探索的点
            List<int> closeList = new List<int>();  //定义封闭列表，存放已确定的路线点
            int INF = 2147483647;
            G.Add(startNodeId, 0);
            H.Add(startNodeId, CalculateManhattanDistance(Graph, startNode, goalNode));
            F.Add(startNodeId, G[startNodeId] + H[startNodeId]);
            Depth.Add(startNodeId, 0);
            parent.Add(startNodeId, -1);

            openList.Add(startNodeId);           //将起始点添加至开放列表
            MinHeap openListHeap = new MinHeap();//最小堆的初始化
            openListHeap.Insert(startNodeId, F[startNodeId]);//openlist的节点嵌入最小堆
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
                    var str2 = "Find Path Fail. No Path Found, IDa: " + startNodeId + " IDb: " + goalNodeId + " pos( " + startNode.x + ", " + startNode.y + ", " + startNode.layout + ") -> ( " + goalNode.x + ", " + goalNode.y + ", " + goalNode.layout;
                    str2 += ")";
                    Diagnosis.Post(str2);
                    Console.WriteLine($"Find Path Fail. No Path Found.");
                    return null;
                }

                int curNodeId = openList[0];   //选择当前探索的节点，openList列表的首位
                //优先队列选择openlist中F值最小的节点作为当前节点                       
                (curNodeId, _) = openListHeap.ExtractMin();

                //在openList中移除当前节点，并添加到closeList中
                openList.Remove(curNodeId);
                Node curNode = Graph.nodeDict[curNodeId];
                if (!closeList.Contains(curNodeId))
                {
                    closeList.Add(curNodeId);
                }
                if ((isAvoidFinder_level == 0 && agent.isArriveGoal(curNodeId, true, agent.isCurrentGoalKey))
                    || isAvoidFinder_level != 0 && isArriveAvoidNode(Graph, curNodeId, agent, isAvoidFinder_level))
                {
                    // 回溯路径
                    while (curNodeId != startNodeId)
                    {
                        path.Add(curNodeId);
                        curNodeId = parent[curNodeId];
                    }
                    path.Add(curNodeId);
                    path.Reverse();
                    return path;
                }
                //从一条边的两个点来判断当前节点的相邻扩展节点
                foreach (var neighborId in curNode.neighBor)
                {
                    var neighbor = Graph.nodeDict[neighborId];
                    if (isAvoidFinder_level == 0 && checkConnection(Graph, curNodeId, neighborId, goalNodeId, agent, isAllowPlanDisabled) == false) continue;
                    if (isAvoidFinder_level >= 1 && checkConnectionForAvoid(Graph, curNodeId, neighborId, agent, isAvoidFinder_level) == false) continue;
                    if (agent.have_goods 
                        && SimpleLib.GetSite(neighbor.id).tags.Contains("Goods")
                        && neighbor.isPortAndType == 0) continue;
                    // 从已有的地图site中来读取对应的node信息
                    if (closeList.Contains(neighborId))
                    {
                        continue;
                    }
                    var neighborNode = Graph.nodeDict[neighborId];
                    //计算每个节点的代价
                    if (G.ContainsKey(neighborId) == false)
                    {
                        G.Add(neighborId, INF);
                        H.Add(neighborId, INF);
                        F.Add(neighborId, INF);
                        parent.Add(neighborId, -1);
                        Depth.Add(neighborId, -1);
                    }
                    if (G[neighborId] > G[curNodeId] + CalculateManhattanDistance(Graph, curNode, neighbor)/* + busyAlpha * neighborNode.busyCost*/)
                    {
                        Depth[neighborId] = Depth[curNodeId] + 1;
                        parent[neighborId] = curNodeId;
                        G[neighborId] = G[curNodeId] + CalculateManhattanDistance(Graph, curNode, neighbor)/* + busyAlpha * neighborNode.busyCost*/;
                        H[neighborId] = CalculateManhattanDistance(Graph, neighbor, goalNode);
                        F[neighborId] = G[neighborId] + H[neighborId];
                        if (!openList.Contains(neighborId) && !closeList.Contains(neighborId) && !lockList.Contains(neighborId))
                        {
                            openList.Add(neighborId);
                            openListHeap.Insert(neighborId, F[neighborId]);//openlist每次add的节点嵌入最小堆
                        }
                    }
                }
            }
            if (isAvoidFinder_level == 0)
            {
                var str = "Find Path Fail. No Path Found, pos( " + startNode.x + ", " + startNode.y + ") -> ( " + goalNode.x + ", " + goalNode.y;
                str += ")";
                Diagnosis.Post(str);
                Console.WriteLine($"Find Path Fail. No Path Found.");
            }
            else
            {
                var str = "Find Avoid Node Fail. pos( " + startNode.x + ", " + startNode.y + ")";
                Diagnosis.Post(str);
                Console.WriteLine($"Find Path Fail. No Path Found.");

            }
            return null;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn bool checkConnection(SectorGraph Graph, int curId, int nexId, int goalId, Agent agent)
        ///
        /// @brief  判断curId到nexId是否联通
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// 
        private static bool checkConnection(SectorGraph Graph, int curId, int nexId, int goalId, Agent agent, bool isAllowPlanDisabled)
        {
            var trackId = Graph.findTrackId(curId, nexId);
            Graph.Track track = Graph.trackDict[trackId];

            if (trackId == -1) return false;
            if (track.direction == 3) return false;
            if (track.direction == 1 && curId > nexId) return false;
            if (track.direction == 2 && curId < nexId) return false;
            Node curNode = Graph.nodeDict[curId];
            Node nexNode = Graph.nodeDict[nexId];

            if (isAllowPlanDisabled == false && nexNode.isDisabled == true) return false;

            if (agent.isAvoid == true && Graph.CommandLockSites.Contains(nexId))
            {
                var block_agent_id = Graph.nodeDict[nexId].commandLockAgent;
                if (agent.avoidFor == block_agent_id) return false;
                // 绕开所有pending_lock点
            }

            // 对于最右侧的部分节点而言，没有Sector，无需考虑预约制
            if (Graph.nodeId2SectorId.ContainsKey(nexId) == false) return true;
            if (Graph.nodeId2SectorId.ContainsKey(curId) == false) return true;
            var nexSectorId = Graph.nodeId2SectorId[nexId];
            Sector nexSector = Graph.sectorDict[nexSectorId];
            var curSectorId = Graph.nodeId2SectorId[curId];
            Sector curSector = Graph.sectorDict[curSectorId];

            // 下面在pathplan开始考虑预约制度，如果非双向路则不需要考虑预约制度  （这条边不与twoway_SHELF进行交互）
            if (nexSector.type != Sector.SECTOR_TYPE.TwoWay_SHELF && nexSector.type != Sector.SECTOR_TYPE.TwoWay_SHELF) return true;

            var InOutType = Graph.isInOutOrStaySector(curId, nexId);
            if (InOutType == 1)  // 如果是进入货架
            {
                //  如果下一个Sector被锁住，则除非终点在该sector内部，否则在pathplan层面需要进行绕路
                if (nexSector.isLocked == 1 && nexSector.beLockedCar != -1 && nexSector.beLockedCar != agent.Id)
                {
                    if (Graph.nodeId2SectorId.ContainsKey(goalId))
                    {
                        if (Graph.nodeId2SectorId[goalId] != nexSectorId) return false; // 如果终点不在该sector内部，则不能经过
                        // 否则，终点在sector内部，后续判断预约制度
                    }
                    else return false; // 终点没有sector编号，一定不在sector内部
                }

                if (agent.State == carState.WORK) // 携带货物 且 终点在sector内部 则不需要遵守
                {
                    if (Graph.nodeId2SectorId.ContainsKey(goalId))
                    {
                        if (Graph.nodeId2SectorId[goalId] == nexSectorId) return true; // 如果终点在该sector内部
                    }
                }

                // 需要遵守预约制度
                if (nexSector.reservationCar.Count == 0 || nexSector.reservationPoint == -1) return true;
                if (nexSector.reservationPoint == curId) return true;
                return false;
            }

            // 否则，需要遵守预约制度
            if (curSector.reservationCar.Count == 0 || curSector.reservationPoint == -1) return true;
            if (Graph.nodeDict[curSector.reservationPoint].y < Graph.nodeDict[curId].y && Graph.nodeDict[curId].y < Graph.nodeDict[nexId].y) return true;
            if (Graph.nodeDict[curSector.reservationPoint].y > Graph.nodeDict[curId].y && Graph.nodeDict[curId].y > Graph.nodeDict[nexId].y) return true;
            return false;
        }

        public static bool needPathPlan(SectorGraph Graph, Agent agent)
        {
            if (agent.iscurGoalChange == true)
                return true;

            if (agent.haveGoals() == false
                && (agent.Path == null || (agent.Path != null && agent.Path.Count <= 1)))
                return false;

            if (agent.pathStatus == 2)
                return false;

            var curPoint = agent.getLastHoldingLockId();

            if (agent.Path.Contains(curPoint))
                agent.initCurPath(curPoint);
            else return true;

            var finalNodeId = agent.Path[agent.Path.Count - 1];
            if (agent.isArriveGoal(finalNodeId, true, agent.isCurrentGoalKey) == false) 
                return true;

            foreach (var node in agent.Path)
            {
                if (curPoint == node) continue;
                if (checkConnection(Graph, curPoint, node, agent.CurrentGoal, agent, true) == false)
                    return true;
                curPoint = node;
            }
            return false;
        }

        private static int CalculateManhattanDistance(SectorGraph Graph, Node nodeA, Node nodeB = null)
        {
            if (nodeB == null) return 0;
            if (nodeA.layout == nodeB.layout)
            {
                if((nodeA.type== NODE_TYPE.NORMAL&&nodeA.x< Graph.DELIVER_LINE_X))//若AB节点是灰色点且不包含最右侧三列,且越靠近左侧代价越小
                {
                    return (int)(0.5*(Math.Abs(nodeA.x - nodeB.x) + Math.Abs(nodeA.y - nodeB.y)) * Math.Abs(nodeA.x-Graph.MinX) / Math.Abs( Graph.MaxX-Graph.MinX));
                }
                else if ((nodeB.type == NODE_TYPE.NORMAL && nodeB.x < Graph.DELIVER_LINE_X))
                {
                    return (int)(0.5 * (Math.Abs(nodeA.x - nodeB.x) + Math.Abs(nodeA.y - nodeB.y)) * Math.Abs(nodeB.x - Graph.MinX) / Math.Abs(Graph.MaxX - Graph.MinX));
                }
                else
                return (int)((Math.Abs(nodeA.x - nodeB.x) + Math.Abs(nodeA.y - nodeB.y)) * Math.Abs(nodeA.x - Graph.MinX) / Math.Abs(Graph.MaxX - Graph.MinX));
            }
            return Math.Abs(Graph.MaxX - nodeA.x);
        }

        public static bool haveValidPath(SectorGraph Graph, int startNodeId, int goalNodeId, List<int> lockList, bool haveGoods)
        {
            Dictionary<int, int> G = new Dictionary<int, int>();
            Dictionary<int, int> F = new Dictionary<int, int>();
            Dictionary<int, int> H = new Dictionary<int, int>();
            Dictionary<int, int> parent = new Dictionary<int, int>();

            Node startNode = Graph.nodeDict[startNodeId];
            Node goalNode = Graph.nodeDict[goalNodeId];

            if (startNode.layout != goalNode.layout) return true;   // 不考虑跨楼层，TODO

            List<int> openList = new List<int>();  //定义开放列表，存放待探索的点
            List<int> closeList = new List<int>();  //定义封闭列表，存放已确定的路线点
            int INF = 2147483647;
            G.Add(startNodeId, 0);
            H.Add(startNodeId, CalculateManhattanDistance(Graph, startNode, goalNode));
            F.Add(startNodeId, G[startNodeId] + H[startNodeId]);
            parent.Add(startNodeId, -1);

            openList.Add(startNodeId);           //将起始点添加至开放列表

            MinHeap openListHeap = new MinHeap();//最小堆的初始化
            openListHeap.Insert(startNodeId, F[startNodeId]);//openlist的节点嵌入最小堆

            //开始循环搜索路径
            while (openList.Count > 0)
            {
                int curNodeId = openList[0];   //选择当前探索的节点，openList列表的首位
                                               //对openList中的节点进行遍历，选择F值最小的节点作为当前节点
                //优先队列选择openlist中F值最小的节点作为当前节点                       
                (curNodeId, _) = openListHeap.ExtractMin();

                //在openList中移除当前节点，并添加到closeList中
                openList.Remove(curNodeId);
                Node curNode = Graph.nodeDict[curNodeId];
                if (!closeList.Contains(curNodeId))
                {
                    closeList.Add(curNodeId);
                }
                if (curNodeId == goalNodeId)    // 到达目标点
                {
                    return true;
                }
                //从一条边的两个点来判断当前节点的相邻扩展节点
                foreach (var neighborId in curNode.neighBor)
                {
                    var neighbor = Graph.nodeDict[neighborId];
                    if (checkConnectionWithoutLock(Graph, curNodeId, neighborId) == false) continue;
                    if (haveGoods 
                        && SimpleLib.GetSite(neighbor.id).tags.Contains("Goods")
                        && neighbor.isPortAndType == 0) continue;
                    // 从已有的地图site中来读取对应的node信息
                    if (closeList.Contains(neighborId))
                    {
                        continue;
                    }
                    //计算每个节点的代价
                    if (G.ContainsKey(neighborId) == false)
                    {
                        G.Add(neighborId, INF);
                        H.Add(neighborId, INF);
                        F.Add(neighborId, INF);
                        parent.Add(neighborId, -1);
                    }
                    if (G[neighborId] > G[curNodeId] + CalculateManhattanDistance(Graph, curNode, neighbor))
                    {
                        parent[neighborId] = curNodeId;
                        G[neighborId] = G[curNodeId] + CalculateManhattanDistance(Graph, curNode, neighbor);
                        H[neighborId] = CalculateManhattanDistance(Graph, neighbor, goalNode);
                        F[neighborId] = G[neighborId] + H[neighborId];
                        if (!openList.Contains(neighborId) && !closeList.Contains(neighborId) && !lockList.Contains(neighborId))
                        {
                            openList.Add(neighborId);
                            openListHeap.Insert(neighborId, F[neighborId]);//openlist每次add的节点嵌入最小堆
                        }
                    }
                }
            }
            return false;
        }

        private static bool checkConnectionWithoutLock(SectorGraph Graph, int curId, int nexId)
        {
            var trackId = Graph.findTrackId(curId, nexId);
            Graph.Track track = Graph.trackDict[trackId];

            if (trackId == -1) return false;
            if (track.direction == 3) return false;
            if (track.direction == 1 && curId > nexId) return false;
            if (track.direction == 2 && curId < nexId) return false;
            Node curNode = Graph.nodeDict[curId];
            Node nexNode = Graph.nodeDict[nexId];

            if (nexNode.isDisabled == true) return false;

            // 对于最右侧的部分节点而言，没有Sector，无需考虑预约制
            if (Graph.nodeId2SectorId.ContainsKey(nexId) == false) return true;
            if (Graph.nodeId2SectorId.ContainsKey(curId) == false) return true;
            var nexSectorId = Graph.nodeId2SectorId[nexId];
            Sector nexSector = Graph.sectorDict[nexSectorId];
            var curSectorId = Graph.nodeId2SectorId[curId];
            Sector curSector = Graph.sectorDict[curSectorId];

            // 下面在pathplan开始考虑预约制度，如果非双向路则不需要考虑预约制度  （这条边不与twoway_SHELF进行交互）
            if (nexSector.type != Sector.SECTOR_TYPE.TwoWay_SHELF && nexSector.type != Sector.SECTOR_TYPE.TwoWay_SHELF) return true;

            var InOutType = Graph.isInOutOrStaySector(curId, nexId);
            if (InOutType == 1)  // 如果是进入货架
            {
                // 需要遵守预约制度
                if (nexSector.reservationCar.Count == 0 || nexSector.reservationPoint == -1) return true;
                if (nexSector.reservationPoint == curId) return true;
                return false;
            }

            // 否则，需要遵守预约制度
            if (curSector.reservationCar.Count == 0 || curSector.reservationPoint == -1) return true;
            if (Graph.nodeDict[curSector.reservationPoint].y < Graph.nodeDict[curId].y && Graph.nodeDict[curId].y < Graph.nodeDict[nexId].y) return true;
            if (Graph.nodeDict[curSector.reservationPoint].y > Graph.nodeDict[curId].y && Graph.nodeDict[curId].y > Graph.nodeDict[nexId].y) return true;
            return false;
        }


        private static bool isArriveAvoidNode(SectorGraph Graph, int curId, Agent agent, int isAvoidFinder_level)
        {

            if (AvoidManager.hideNodeTable.Keys.Contains(curId) == true
                && AvoidManager.hideNodeTable[curId] != agent.Id)
            {
                return false;
            }

            if (Graph.nodeId2SectorId.ContainsKey(curId))
            {
                var sector = Graph.sectorDict[Graph.nodeId2SectorId[curId]];
                if (sector.executingTaskNum > 0) return false;
                if (sector.ActiveAgentId.Count > 0) return false;

            }
            if (isAvoidFinder_level == 1)
            {
                if (Graph.PendingLockSites.Contains(curId))
                {
                    var block_agent_id = Graph.nodeDict[curId].pendingLockAgent;
                    if (agent.avoidFor == block_agent_id) return false;
                    // 绕开所有pending_lock点
                }
                if (Graph.CommandLockSites.Contains(curId))
                {
                    var block_agent_id = Graph.nodeDict[curId].commandLockAgent;
                    if (agent.avoidFor == block_agent_id) return false;
                    // 绕开所有commandLock点
                }
            }
            else
            {
                if (Graph.CommandLockSites.Contains(curId))
                {
                    var block_agent_id = Graph.nodeDict[curId].commandLockAgent;
                    if (agent.avoidFor == block_agent_id) return false;
                    // 绕开所有commandLock点
                }

            }

            if (Graph.nodeDict[curId].type == Node.NODE_TYPE.SHELF)
            {
                if (!agent.have_goods || (agent.have_goods && !SimpleLib.GetSite(curId).tags.Contains("Goods")))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool checkConnectionForAvoid(SectorGraph Graph, int curId, int nexId, Agent agent, int isAvoidFinder_level)
        {
            var trackId = Graph.findTrackId(curId, nexId);
            Graph.Track track = Graph.trackDict[trackId];

            if (trackId == -1) return false;
            if (track.direction == 3) return false;
            if (track.direction == 1 && curId > nexId) return false;
            if (track.direction == 2 && curId < nexId) return false;
            Node curNode = Graph.nodeDict[curId];
            Node nexNode = Graph.nodeDict[nexId];

            if (nexNode.isDisabled == true) return false;
            if (isAvoidFinder_level == 1)
            {
                if (Graph.PendingLockSites.Contains(nexId))
                {
                    var block_agent_id = Graph.nodeDict[nexId].pendingLockAgent;
                    if (agent.avoidFor == block_agent_id) return false;
                    // 绕开所有pending_lock点
                }
                if (Graph.CommandLockSites.Contains(nexId))
                {
                    var block_agent_id = Graph.nodeDict[nexId].commandLockAgent;
                    if (agent.avoidFor == block_agent_id) return false;
                    // 绕开所有commandLock点
                }
            }
            else
            {
                if (Graph.CommandLockSites.Contains(nexId))
                {
                    var block_agent_id = Graph.nodeDict[nexId].commandLockAgent;
                    if (agent.avoidFor == block_agent_id) return false;
                    // 绕开所有commandLock点
                }

            }

            // 对于最右侧的部分节点而言，没有Sector，无需考虑预约制
            if (Graph.nodeId2SectorId.ContainsKey(nexId) == false) return true;
            if (Graph.nodeId2SectorId.ContainsKey(curId) == false) return true;
            var nexSectorId = Graph.nodeId2SectorId[nexId];
            Sector nexSector = Graph.sectorDict[nexSectorId];
            var curSectorId = Graph.nodeId2SectorId[curId];
            Sector curSector = Graph.sectorDict[curSectorId];

            // 下面在pathplan开始考虑预约制度，如果非双向路则不需要考虑预约制度  （这条边不与twoway_SHELF进行交互）
            if (nexSector.type != Sector.SECTOR_TYPE.TwoWay_SHELF && nexSector.type != Sector.SECTOR_TYPE.TwoWay_SHELF) return true;

            var InOutType = Graph.isInOutOrStaySector(curId, nexId);
            if (InOutType == 1)  // 如果是进入货架
            {
                //  如果下一个Sector被锁住，则除非终点在该sector内部，否则在pathplan层面需要进行绕路
                if (nexSector.isLocked == 1 && nexSector.beLockedCar != -1 && nexSector.beLockedCar != agent.Id)
                    return false;
            }
            return true;
        }
    }
}