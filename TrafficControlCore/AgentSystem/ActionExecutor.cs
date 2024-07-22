/************************************************************************/
/* File:	  ActionExecutor.cs										    */
/* Func:	  针对agent.Path 尝试执行Path                                */
/* Author:	  Tan Yuhong                                                */
/************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrafficControlCore.DispatchSystem.Graph;
using TrafficControlCore.DispatchSystem;
using SimpleCore;
using TrafficControlCore.AvoidSystem;
using static TrafficControlCore.DispatchSystem.Graph.Sector;
using SimpleCore.Library;

namespace TrafficControlCore.AgentSystem
{
    public class ActionExecutor
    {
        public SectorGraph Graph
        {
            get { return DispatchMisson.Graph; }
        }
        public LockAndReservation LockAndReservationManager
        {
            get { return DispatchMisson.LockAndReservationManager; }
        }
        public AgvAgentManager AgentManager
        {
            get { return DispatchMisson.AgentManager; }
        }
        public AgvAvoidManager AvoidManager
        {
            get { return DispatchMisson.AvoidManager; }
        }
        public ActionExecutor() { }

        public void FormatOutput(Agent agent)
        {
            var path = agent.Path;
            if (path.Count > 0)
            {
                // path.RemoveAt(0);
                int holdingLength = 7;
                int pendingLength = 15;
                var pendingPath = new List<int>(pendingLength);
                pendingPath.AddRange(path.Take(pendingLength));
                var holdingPath = new List<int>(holdingLength);
                holdingPath.AddRange(path.Take(holdingLength));
                SetSingleCarAction(agent, holdingLength, pendingLength, pendingPath, holdingPath);
            }
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn bool checkCanSetCommand(Agent agent, int nexPointId)
        ///
        /// @brief  判断nexPointId能否set
        ///         主要考虑lock机制，考虑电梯口和放货口的拥堵情况
        /// 
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private bool checkCanSetCommand(Agent agent, int nexPointId)
        {
            Node nextPoint = Graph.nodeDict[nexPointId];//SimpleLib.GetSite(nexPointId);

            var curPointId = agent.getLastHoldingLockId();
            var curPoint = SimpleLib.GetSite(curPointId);

            // 检修区 
            if (nextPoint.isDisabled == true) return false;

            // 特殊情况：小车刚结束放货，本身可以走堵塞该放货点的节点
            if (curPoint.x > Graph.MAIN_LINE_X && curPoint.y == nextPoint.y && nextPoint.x == Graph.MAIN_LINE_X)
                return true; // 可以

            // 带货小车不走货物点
            if (agent.have_goods 
                && SimpleLib.GetSite(nexPointId).tags.Contains("Goods")) 
                return false;
            
            /*
             * 带有电梯换层的逻辑 
            if (nextPoint.x >= SectorGraph.MAIN_LINE_X && Graph.portY2AgentId.ContainsKey(nextPoint.y) == true)
            {
                if (nextPoint.gateId != -1)  // 右侧存在库口
                {
                    var portPoint = Graph.nodeDict[nextPoint.gateId];   // 库口节点
                    var isGotoHoister = (Graph.nodeDict[agent.pathLastPointId].y == nextPoint.y && Graph.nodeDict[agent.pathLastPointId].x > SectorGraph.MAIN_LINE_X);
                    
                    if (portPoint.id == agent.CurrentGoal || isGotoHoister == true)  // 当前agent的目标点是库口节点
                    { 
                        var other = Graph.portY2AgentId[nextPoint.y];
                        if (other != agent.Id)
                        {
                            Diagnosis.Post("agentID: " + agent.Id + " block two, curPointID: " + curPointId + ", nexPointId: " + nextPoint.id + " otherCar: " + other);
                            agent.DeadInducementAgentId = other;
                            return false;
                        }
                    }
                }
            }
            */

            Node nexNode = Graph.nodeDict[nexPointId]; // 下一个点NexNode         
            if (agent.haveGoals() == false) return false;
            var curGoalId = agent.CurrentGoal;

            // 如果下一个点不是高速路点，或没有与其他sector相连，则不可能会造成由于curGoal导致的拥堵
            if (Graph._highwaySiteId.Contains(nexPointId) == false || nexNode.highwayConnectSectorIds == null || nexNode.highwayConnectSectorIds.Count == 0)
                return true;

            // if (nexNode.x >= SectorGraph.DELIVER_LINE_X) return true;

            // 我的target如果是 nexNode相连的sector

            foreach (var isectorId in nexNode.highwayConnectSectorIds)   // 取出与之相邻的sector
            {
                Sector endSector = Graph.sectorDict[isectorId];

                if (endSector.type == SECTOR_TYPE.DELIVERY) continue; // 排除与DELIVERY相连的情景。

                if (Graph.nodeDict[curGoalId].sectorId == isectorId 
                    || (curGoalId == nexPointId && Graph._highwaySiteId.Contains(nexPointId)))
                    // || agent.SectorPath.Contains(isectorId))     // modify  这里有问题
                                                                                                                                                   // 如果需要进入该Dict 或者 我目标点为此标志位
                {
                    if (endSector.insideNode.Contains(curPointId)) continue;

                    // 判断该sector是否被锁住
                    if (endSector.isLocked == 1 && endSector.beLockedCar != agent.Id)
                    {
                        Diagnosis.Post("agentID: " + agent.Id + " block three");
                        agent.DeadInducementAgentId = endSector.beLockedCar;
                        return false;    // 存在小车在其内部，不可进入
                    }
                    if (agent.State == carState.WORK || agent.State == carState.PICKUP)    // 如果需要进入该Dict且我是work状态 -> 放货
                    {
                        if (endSector.isCharged == false && endSector.checkOtherActiveAgent(agent.Id) == true)
                        {
                            Diagnosis.Post("agentID: " + agent.Id + " block four");
                            return false;  // 保证该sector内部不存在active小车
                        }
                    }/*
                    if (endSector.reservationCar.Count > 0 && endSector.reservationPoint != nexPointId)
                    {
                        Diagnosis.Post("agentID: " + agent.Id + " block five, 不满足预约制");
                        return false;
                    }*/
                }
            }
            return true;
        }

        private int isOtherBlockMe(Agent agent, int nexPointId, bool isPending = false)
        {
            if (isPending == false && 
                (Graph.CommandLockSites.Contains(nexPointId)
                && Graph.nodeDict[nexPointId].commandLockAgent != agent.Id))
                return Graph.nodeDict[nexPointId].commandLockAgent;

            if (isPending == true &&
                (Graph.PendingLockSites.Contains(nexPointId)
                && Graph.nodeDict[nexPointId].pendingLockAgent != agent.Id))
                return Graph.nodeDict[nexPointId].pendingLockAgent;

            if (Graph.inv_portId2AgentId.ContainsKey(nexPointId)
                && Graph.inv_portId2AgentId[nexPointId] != agent.Id)
            {

                var portPointId = Graph.nodeDict[nexPointId].gateId;
                var portPoint = Graph.nodeDict[portPointId]; 
                var isGotoHoister = (Graph.nodeDict[agent.pathLastPointId].y == portPoint.y 
                    && Graph.nodeDict[agent.pathLastPointId].x > Graph.MAIN_LINE_X);

                Diagnosis.Post(" agentID: " + agent.Id + " nexPointId: " + nexPointId + " portPointId: " + portPointId + " ishositer: " + isGotoHoister);
                if (portPoint.id == agent.CurrentGoal || isGotoHoister == true)
                    return Graph.inv_portId2AgentId[nexPointId];
            }
            return -1;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void SetSingleCarAction(Agent agent, int holdingLength, List<int> holdingPath)
        ///
        /// @brief  给定holdingPath，尝试将holdinglocks补齐到holdingLength长度
        ///         实时更新Lock和Reservation，更新activeAgent
        ///         包含关键点机制
        /// 
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// 
        private void SetSingleCarAction(Agent agent, int holdingLength, int pendingLength, List<int> pendingPath, List<int> holdingPath)
        {
            var SetFlag = false;
            // var AcrossSectorId = -1;
            // var exceptionFlag = false;

            var curPointId = agent.getLastHoldingLockId();
            var tmpPointIds = new Queue<int>(); // 不可以长期占用keyPoint机制
            tmpPointIds.Clear();
            foreach (var nexPointId in pendingPath)
            {
                if (nexPointId == agent.getLastHoldingLockId()) continue;

                var blockAgentId = isOtherBlockMe(agent, nexPointId, false);
                if (blockAgentId != -1)
                {
                    int currentIndex = pendingPath.IndexOf(nexPointId);         // nexPointId的index
                    var exactCurPointId = agent.getLastHoldingLockId();         // nexPoint的上一个节点 CurPointId
                    if (currentIndex != 0)
                    {
                        exactCurPointId = pendingPath[currentIndex - 1];
                    }
                        
                    agent.DeadInducementAgentId = blockAgentId;

                    if (AvoidManager.NeedGenerateAvoid(agent.Id, blockAgentId, exactCurPointId, nexPointId))
                    {
                        AvoidManager.GenerateAvoid(agent.Id, blockAgentId, nexPointId);
                    }
                    if (Graph.ISLARGEGRAPH == true)
                    {
                        Diagnosis.Post("agentID: " + agent.Id + " block one, because of " + blockAgentId);
                    }
                    break;
                }

                var pending_blockAgentId = isOtherBlockMe(agent, nexPointId, true);
                if (pending_blockAgentId != -1 
                    && (agent.isAvoid == false || (agent.isAvoid == true && pending_blockAgentId != agent.avoidFor)))
                {
                    int currentIndex = pendingPath.IndexOf(nexPointId);
                    var exactCurPointId = agent.getLastHoldingLockId();
                    if (currentIndex != 0)
                    {
                        exactCurPointId = pendingPath[currentIndex - 1];
                    }

                    agent.DeadInducementAgentId = pending_blockAgentId;

                    if (AvoidManager.NeedGenerateAvoid(agent.Id, pending_blockAgentId, exactCurPointId, nexPointId))
                    {
                        AvoidManager.GenerateAvoid(agent.Id, pending_blockAgentId, nexPointId);
                    }
                    break;
                }

                if (checkCanSetCommand(agent, nexPointId) == false)
                    break;
                // if (agent.HoldingLocksCount >= holdingLength)
                //     break;

                if (Graph.keyPointNode.Contains(nexPointId) && nexPointId != holdingPath.Last())    // keyPoint延迟发布
                {
                    tmpPointIds.Enqueue(nexPointId);
                    continue;
                }
                /*
                if (Graph.keyPointNode.Contains(nexPointId))
                {
                    Diagnosis.Post("agentId: " + agent.Id + " nexPoint: " + nexPointId + " last: " + pendingPath.Last());
                }
                else 
                    Diagnosis.Post("agentId: " + agent.Id + " nexPoint: " + nexPointId);
                */

                if (holdingPath.Contains(nexPointId) && agent.HoldingLocksCount + tmpPointIds.Count < holdingLength)
                {
                    while (tmpPointIds.Count > 0)
                    {
                        var nowPointId = tmpPointIds.Dequeue();
                        if (agent.HoldingLocksCount < holdingLength)
                        {
                            agent.AddHoldingSite(nowPointId);
                            LockAndReservationManager.registerLockSite(nowPointId, agent);
                            LockAndReservationManager.setReservation(agent, curPointId, nowPointId);
                            setActiveAgent(nowPointId, agent);
                        }

                        Graph.PendingLockSites.Add(nowPointId);
                        Graph.nodeDict[nowPointId].pendingLockAgent = agent.Id;
                        curPointId = nowPointId;
                    }
                    if (agent.HoldingLocksCount < holdingLength)
                    {
                        agent.AddHoldingSite(nexPointId);    // 需要前往command
                        LockAndReservationManager.registerLockSite(nexPointId, agent);
                        LockAndReservationManager.setReservation(agent, curPointId, nexPointId);
                        setActiveAgent(nexPointId, agent);
                        SetFlag = true;
                    }
                    Graph.PendingLockSites.Add(nexPointId);
                    Graph.nodeDict[nexPointId].pendingLockAgent = agent.Id;
                    curPointId = nexPointId;
                }
            }
            agent.updateDeadClock(SetFlag);
        }
        public void setActiveAgent(int curPointId, Agent agent)
        {
            var curPoint = Graph.nodeDict[curPointId];
            if (Graph.nodeId2SectorId.ContainsKey(curPointId) == false) return; var curSector = Graph.sectorDict[curPoint.sectorId];
            if (curSector.type == SECTOR_TYPE.DeadEnd_SHELF || (curSector.type == SECTOR_TYPE.TwoWay_SHELF && curSector.isShelf == true))
            {
                if (/*agent.isManual == false && */curSector.ActiveAgentId.Contains(agent.Id) == false)
                {
                    curSector.ActiveAgentId.Add(agent.Id);
                }
            }
        }
    }
}
