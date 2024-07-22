/************************************************************************/
/* File:	  LockAndReservation.cs										*/
/* Func:	  管理SectorGraph中的Lock机制和Reservation机制                */
/* Author:	  Tan Yuhong                                                */
/************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrafficControlCore.AgentSystem;
using static TrafficControlCore.DispatchSystem.Graph.Sector;

namespace TrafficControlCore.DispatchSystem.Graph
{
    public class LockAndReservation
    {
        public SectorGraph Graph
        {
            get { return DispatchMisson.Graph; }
        }
        public AgvAgentManager AgentManager
        {
            get { return DispatchMisson.AgentManager; }
        }
        public ActionExecutor ActionManager
        {
            get { return DispatchMisson.ActionManager; }
        }
        public void init()
        {
            // 初始化commandLockSite（必定锁格）和sector的占用情况
            var agentList = AgentManager.agentList.Values;
            foreach (var agent in agentList)
            {
                /*
                var isActive = false;
                if (agent.Goals == null || agent.Goals.Count == 0) isActive = true;
                else
                {
                    var curGoalId = agent.Goals.Peek();
                    if (agent.SiteID != curGoalId || agent.State != carState.FREE) isActive = true;
                }
                */
                var isActive = true;
                lock (agent.GetCar.obj)
                {
                    for (int i = 0; i < agent.holdingLocks().Count; ++i)
                    {
                        if (agent.IsFault && i > 1) break;

                        var curPointId = agent.holdingLocks()[i];
                        registerLockSite(curPointId, agent);
                        if (isActive)
                            ActionManager.setActiveAgent(curPointId, agent);
                    }
                }
                agent.DeadInducementAgentId = -1;
            }
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void registerLockSite(int locksite, Agent agent)
        ///
        /// @brief  使用locksite更新reservation和lock
        ///
        /// @author Tan Yuhong
        /// @date   2024-3-26
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void registerLockSite(int locksite, Agent agent)
        {

            Node lockNode = Graph.nodeDict[locksite];
            if (agent.haveGoals() == true)
            {
                var curGoalId = agent.CurrentGoal;

                // 如果下一个点是高速路点，有与其他sector相连，则不可能会造成由于curGoal导致的拥堵
                if (Graph._highwaySiteId.Contains(locksite) == true && lockNode.highwayConnectSectorIds != null 
                    && lockNode.highwayConnectSectorIds.Count > 0
                    && curGoalId != -1)
                {
                    // 我的target如果是 nexNode相连的sector

                    foreach (var isectorId in lockNode.highwayConnectSectorIds)   // 取出与之相邻的sector
                    {
                        Sector endSector = Graph.sectorDict[isectorId];

                        if (endSector.type == SECTOR_TYPE.DELIVERY) continue; // 排除与DELIVERY相连的情景。

                        if (Graph.nodeDict[curGoalId].sectorId == isectorId || curGoalId == locksite)
                        // 如果需要进入该Dict
                        {
                            /*
                            if (endSector.siteDown == locksite && endSector.siteUp != -1)
                            {
                                Graph.nodeDict[endSector.siteUp].commandLockAgent = agent.Id;
                                Graph.CommandLockSites.Add(endSector.siteUp);
                            }
                            if (endSector.siteUp == locksite && endSector.siteDown != -1)
                            {
                                Graph.nodeDict[endSector.siteDown].commandLockAgent = agent.Id;
                                Graph.CommandLockSites.Add(endSector.siteDown);
                            }
                            */
                            setLock(Graph.sectorDict[isectorId], agent.Id);

                        }
                    }
                }
            }
            Graph.CommandLockSites.Add(locksite);
            Graph.nodeDict[locksite].commandLockAgent = agent.Id;
            Graph.PendingLockSites.Add(locksite);
            Graph.nodeDict[locksite].pendingLockAgent = agent.Id;

            if (lockNode.inv_gateId != -1)
            {
                if (Graph.inv_portId2AgentId.ContainsKey(lockNode.inv_gateId) == false)
                    Graph.inv_portId2AgentId.Add(lockNode.inv_gateId, agent.Id);
                /*
                if (Graph.CommandLockSites.Contains(lockNode.inv_gateId) == false)
                {
                    Graph.CommandLockSites.Add(lockNode.inv_gateId);
                    Graph.nodeDict[lockNode.inv_gateId].commandLockAgent = agent.Id;
                }
                */
            }

            var curSectorId = Graph.nodeDict[locksite].sectorId;
            Sector curSector = Graph.sectorDict[curSectorId];

            if (!agent.haveGoals())
            {
                setLock(curSector, agent.Id); return;
            }

            var curGoal = AgentManager.getAgent(agent.Id).CurrentGoal;

            if (agent.State == carState.FREE)
            {
                if (curSector.type == SECTOR_TYPE.DeadEnd_SHELF)
                {
                    setLock(curSector, agent.Id);
                    return;
                }
                else if (curSector.type == SECTOR_TYPE.TwoWay_SHELF) return;
            }

            if (curSector.type == SECTOR_TYPE.DeadEnd_SHELF)
            {
                setLock(curSector, agent.Id);
                return;
            }
            else if (curSector.type == SECTOR_TYPE.DELIVERY)
            {
                return;
            }
            else
            {
                if (curSector.isShelf != true) return;
                else
                {
                    if (agent.State == carState.PICKUP)
                    {
                        if (Graph.nodeDict.ContainsKey(curGoal) == true)
                        {
                            var goalSectorId = Graph.nodeDict[curGoal].sectorId;
                            var goalBelongThisSector = (goalSectorId == curSectorId);
                            if (goalBelongThisSector == false) return;
                            else
                            {
                                setLock(curSector, agent.Id); return;
                            }
                        }
                    }
                    else if (agent.State == carState.WORK)
                    {
                        setLock(curSector, agent.Id); return;
                    }
                }
            }
        }
        private void setLock(Sector curSector, int carId)
        {
            var agent = AgentManager.getAgent(carId);
            if (agent.isManual == true) return;
            if (agent.State == carState.CHARGE) return;

            curSector.isLocked = 1;
            curSector.beLockedCar = carId;

            if (curSector.type == SECTOR_TYPE.TwoWay_SHELF && curSector.isShelf == true)
            {
                // SHELF的两头道路需要取消预约制度
                Graph.sectorDict[curSector.id].reservationCar.Clear();
                Graph.sectorDict[curSector.id].reservationPoint = -1;
            }
        }
        public void setReservation(Agent agent, int curPointId, int nexPointId)
        {
            if (Graph.nodeId2SectorId.ContainsKey(nexPointId) == true && Graph.nodeId2SectorId.ContainsKey(curPointId) == true) // 排除最后几个出货口
            {
                var InOutType = Graph.isInOutOrStaySector(curPointId, nexPointId);
                var nexSectorId = Graph.nodeId2SectorId[nexPointId];
                var curSectorId = Graph.nodeId2SectorId[curPointId];

                if (Graph.sectorDict[nexSectorId].type != SECTOR_TYPE.TwoWay_SHELF && Graph.sectorDict[curSectorId].type != SECTOR_TYPE.TwoWay_SHELF)
                    return;

                if (InOutType == 1)  // 如果当前是进入SHELF，则锁定预约; 记录car.id和进入的点位
                {
                    Graph.sectorDict[nexSectorId].reservationCar.Add(agent.Id);  // 从normal进入shelf，锁定预约
                    Graph.sectorDict[nexSectorId].reservationPoint = curPointId;
                }
                else if (InOutType == 2) // 离开shelf，cur是shelf
                {
                    if (Graph.sectorDict[curSectorId].reservationCar.Contains(agent.Id))
                        Graph.sectorDict[curSectorId].reservationCar.Remove(agent.Id);// 从normal进入shelf，取消预约
                    if (Graph.sectorDict[curSectorId].reservationCar.Count == 0)
                        Graph.sectorDict[curSectorId].reservationPoint = -1;
                }
                else if (InOutType == 0)
                {
                    Sector curSector = Graph.sectorDict[curSectorId];
                    if (Graph.sectorDict[curSectorId].reservationCar.Contains(agent.Id) == false) // 该Sector中没有该car，需要新增
                    {
                        Graph.sectorDict[curSectorId].reservationCar.Add(agent.Id);
                        var curPoint = Graph.nodeDict[curPointId];
                        var nexPoint = Graph.nodeDict[nexPointId];
                        if (nexPoint.y > curPoint.y)
                            // if (nexPointId > curPointId) // 从下往上走  下方的y更小
                            Graph.sectorDict[curSectorId].reservationPoint = curSector.siteDown;
                        else // 从上往下走
                            Graph.sectorDict[curSectorId].reservationPoint = curSector.siteUp;
                    }
                }
            }
        }
    }
}
