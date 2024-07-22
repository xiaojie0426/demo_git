/************************************************************************/
/* File:	  PreMove.cs										        */
/* Func:	  管理非搬运性移动：预调度任务 + 充电任务                      */
/* Author:	  Tan Yuhong                                                */
/************************************************************************/
using SimpleComposer.RCS;
using SimpleCore.Library;
using SimpleCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TreeView;
using TrafficControlCore.DispatchSystem;
using TrafficControlCore.DispatchSystem.Graph;

namespace TrafficControlCore.PreMoveSystem
{
    public class PreMove
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
        public AgvChargeManager ChargeManager
        {
            get { return DispatchMisson.ChargeManager; }
        }

        public void PreDispatchAndCharge()  // 考虑对Free小车，需要找到左侧地图的某一个空闲点位令他前往
        {
            foreach (var agentId in AgentManager.agentList.Keys)
            {
                var agent = AgentManager.agentList[agentId];
                var robotStatus = AgentManager.GetAgentState(agentId);

                if (robotStatus == carState.FREE 
                    && agent.isManual == false)
                {
                    agent.initGoalList();
                    var curPointId = agent.getLastHoldingLockId();
                    int MinId = NodeFinder.FindNearestNode(Graph, agent, curPointId, NodeFinder.FINDMODE.Hidden);
                    if (Graph.nodeDict.ContainsKey(MinId) == false)
                    {
                        Diagnosis.Post("agentID: " + agent.Id + " Do NOT have valid hidden Node. ");
                        continue;
                    }
                    else
                    {
                        var sector = Graph.sectorDict[Graph.nodeDict[MinId].sectorId];
                        if (Graph.ISLARGEGRAPH == false)
                            Diagnosis.Post("agentID: " + agent.Id + " hidden Node: " + MinId + " sector: " + sector.id + " executingNum: " + sector.executingTaskNum);
                    }
                    agent.addGoal(MinId, true);
                    // agent.Goals.Enqueue(MinId);
                    var node = Graph.nodeDict[MinId];
                    node.hiddenCar = agent.Id;
                }
                /*
                if (robotStatus == carState.CHARGE)
                {
                    if (agent.haveGoals() == false || Graph.nodeDict[agent.CurrentKeyGoal].isCharged == false)
                    {
                        agent.initGoalList();
                        agent.addGoal(ChargeManager.chargeAgentList[agent.Id].pileId, true);
                        // agent.Goals.Enqueue(ChargeManager.chargeAgentList[agent.Id].pileId);
                    }
                }
                */
            }

            var totalNum = 0;
            var str = "TaskTerminalSet: ";
            foreach (var pair in Graph.sectorDict)
            {
                if (pair.Value.executingTaskNum > 0)
                    str += " " + pair.Key + " ";
                totalNum += pair.Value.executingTaskNum;
            }
            Diagnosis.Post(str);
            Diagnosis.Post("executingTaskNum: " + totalNum);
        }
    }
}
