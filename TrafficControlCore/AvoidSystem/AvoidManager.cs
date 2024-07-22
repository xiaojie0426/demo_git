/************************************************************************/
/* File:	  AvoidManager.cs							                */
/* Func:	  管理避让                                                  */
/* Author:	  Han Xingyao                                               */
/************************************************************************/
using SimpleComposer.RCS;
using SimpleCore.Library;
using SimpleCore.PropType;
using SimpleCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TrafficControlCore.DispatchSystem.Graph;
using static TrafficControlCore.DispatchSystem.DispatchMisson;
using static TrafficControlCore.TaskSystem.AgvTask;
using TrafficControlCore.DispatchSystem;
using System.Reflection.Emit;
using System.Xml.Linq;
using SimpleComposer.UI;
using System.Collections;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using static System.Net.WebRequestMethods;
using TrafficControlCore.DispatchSystem.Pathplan;

namespace TrafficControlCore.AvoidSystem
{
    public class AgvAvoidManager
    {
        private int curAvoidId;
        public Dictionary<int, Avoid> avoidTable;
        public Dictionary<int, int> expelNodeTable; // NodeId 到 AgentId 的映射
        public Dictionary<int, int> hideNodeTable; // NodeId 到 AgentId 的映射

        public SectorGraph Graph
        {
            get { return DispatchMisson.Graph; }
        }
        public AgvAgentManager AgentManager
        {
            get { return DispatchMisson.AgentManager; }
        }
        public AgvAvoidManager()
        {
            curAvoidId = 0;
            avoidTable = new Dictionary<int, Avoid>();
            expelNodeTable = new Dictionary<int, int>();
            hideNodeTable = new Dictionary<int, int>();
        }

        public void LogDetail()
        {
            Diagnosis.Post("*************************************************************************************");
            Diagnosis.Post("Loading Avoid Table ...");
            foreach (var avoid in avoidTable.Values)
            {
                Diagnosis.Post(avoid.ToString());
            }
            Diagnosis.Post("*************************************************************************************");
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn bool NeedGenerateAvoid(int expelAgentId, int blockAgentId, int curNodeId, int expelNodeId)
        ///
        /// @brief  expelAgentId 当前agent blockAgentId 避让agent，curNodeId是上一个节点编号（被expelAgent占有），expelNodeId是被挡住的节点编号
        /// 
        /// @author Tan Yuhong
        /// @date   2024-7-10
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// 
        public bool NeedGenerateAvoid(int expelAgentId, int blockAgentId, int curNodeId, int expelNodeId)
        {
            var expelAgent = AgentManager.agentList[expelAgentId];
            var blockAgent = AgentManager.agentList[blockAgentId];
            var blockAgentPath = blockAgent.Path;
            if ( Graph.ISLARGEGRAPH == false )
            {
                Diagnosis.Post("AgentID: " + expelAgentId + " Trying Expel ...");
                Diagnosis.Post("Block AgentID: " + blockAgentId + ", Path: " + string.Join(",", blockAgentPath)
                + ", exactCurPointId: " + curNodeId + ", nexPointId: " + expelNodeId
                + ", isAvoid: " + blockAgent.isAvoid + ", avoidFor: " + blockAgent.avoidFor
                + ", isBeenAvoid: " + blockAgent.isBeenAvoid + ", beenAvoidFor: " + blockAgent.beenAvoidFor);
            }
            // 现在已经知道agent的下个点被占据了，如果block_agent的下个点是当前点，那么block_agent需要避让

            if (Graph.nodeDict[blockAgent.SiteID].isPortAndType != 0)
            {
                if (blockAgentPath.Contains(curNodeId) || blockAgent.haveGoals() == false || blockAgent.SiteID == blockAgent.CurrentGoal) ;
                if (blockAgent.Path.Contains(expelAgent.SiteID) == false) return false;
            }
            if (blockAgent.isAvoid == false     // avoid小车不能避让别人了
                && blockAgent.isBeenAvoid == false
                && (blockAgentPath.Contains(curNodeId) || blockAgent.haveGoals() == false || blockAgent.SiteID == blockAgent.CurrentGoal
                || Graph.nodeDict[blockAgent.SiteID].isPortAndType != 0)
                && (DateTime.Now - blockAgent.finishAvoidTime).TotalSeconds > 5) // 发生了抢占点的情况
                /*
                (blockAgentPath.Contains(curNodeId) && blockAgent.isTraversed == false)
                || (blockAgent.haveGoals() == false || blockAgent.SiteID == blockAgent.CurrentGoal)
                || Graph.nodeDict[blockAgent.SiteID].isPortAndType != 0)*/
            { 
                return true;
            }
            return false;
        }

        public void GenerateAvoid(int expelAgentId, int blockAgentId, int expelNodeId)
        {
            Diagnosis.Post("AgentID: " + expelAgentId + " Expel NodeID: " + expelNodeId + " From Block AgentID: " + blockAgentId);
            var expelAgent = AgentManager.agentList[expelAgentId];
            var blockAgent = AgentManager.agentList[blockAgentId];

            // blockAgent 要设置 Avoid, 同步更新 hideNodeTable;
            var hideNodeId = -1;

            // 这里写的不优雅~
            blockAgent.avoidFor = expelAgentId;
            var hidePath = SectorAstar.FindPath(Graph, blockAgent.getLastHoldingLockId(), -1, new List<int>(), blockAgent, false, 1);
            if (hidePath is null || hidePath.Count == 0)
                hidePath = SectorAstar.FindPath(Graph, blockAgent.getLastHoldingLockId(), -1, new List<int>(), blockAgent, false, 2);
            if (hidePath is not null && hidePath.Count != 0)
            {
                // expelAgent 要设置 Expel, 同步更新 expelNodeTable;
                if (expelAgent.expelInfo.ContainsKey(expelNodeId))
                {
                    var lastBlockAgentId = expelAgent.expelInfo[expelNodeId];
                    var lastBlockAgent = AgentManager.agentList[lastBlockAgentId];
                    if (lastBlockAgent.isAvoid == true)
                    {
                        lastBlockAgent.CancelAvoid();
                        lastBlockAgent.avoidFor = -1;
                        expelAgent.isBeenAvoid = false;
                        expelAgent.beenAvoidFor = -1;
                    }
                };
                blockAgent.clearTraffic();
                hidePath = SectorAstar.FindPath(Graph, blockAgent.getLastHoldingLockId(), -1, new List<int>(), blockAgent, false, 1);
                if (hidePath is null || hidePath.Count == 0)
                    hidePath = SectorAstar.FindPath(Graph, blockAgent.getLastHoldingLockId(), -1, new List<int>(), blockAgent, false, 2);
                expelAgent.SetExpel(expelNodeId, blockAgentId);
                expelNodeTable[expelNodeId] = expelAgent.Id;

                hideNodeId = hidePath[hidePath.Count - 1];
                blockAgent.SetAvoid(hideNodeId, hidePath);
                blockAgent.avoidFor = expelAgentId;
                expelAgent.isBeenAvoid = true;
                expelAgent.beenAvoidFor = blockAgentId;

                blockAgent.SectorPath = PathPlanManager.path2SectorPath(hidePath);
                blockAgent.Path = hidePath;
                blockAgent.GetCar.tempDst = hidePath[hidePath.Count - 1];
                hideNodeTable[hideNodeId] = blockAgent.Id;
                Diagnosis.Post("AgentID: " + blockAgentId + " Set Avoid. Hide Node: " + hideNodeId + " for Robot: " + expelAgentId + " at Expel Node: " + expelNodeId);
                // 生成 Avoid 类;
                Avoid avoid = new Avoid(curAvoidId, expelNodeId, expelAgentId, blockAgentId, hideNodeId);
                avoidTable[curAvoidId] = avoid;
                curAvoidId += 1;
            }
            else
            {
                blockAgent.avoidFor = -1;
            }
        }

        public void UpdateAvoidTable()
        {
            List<int> avoidIdList = new List<int>(avoidTable.Keys);

            Diagnosis.Post("*************************************************************************************");
            Diagnosis.Post("Detecting Avoid Cancel ...");
            foreach (int avoidId in avoidIdList)
            {
                Avoid avoid = avoidTable[avoidId];
                var expelNodeId = avoid.expelNodeId;
                var hideNodeId = avoid.hideNodeId;

                var expelAgentId = avoid.expelAgentId;
                var expelAgent = AgentManager.agentList[expelAgentId];
                var blockAgentId = avoid.blockAgentId;
                var blockAgent = AgentManager.agentList[blockAgentId];

                string avoidInfo = string.Join(", ", expelAgent.holdingLocks().Select(item => $"{item}"));
                avoidInfo = "AgentID: " + expelAgent.Id + ", Expel Node ID: " + expelNodeId 
                            + ", from Block AgentID: " + blockAgentId + ", Holding Locks: (" + avoidInfo + ")";
                Diagnosis.Post(avoidInfo);

                if ((DateTime.Now - blockAgent.setAvoidTime).TotalSeconds > 5 &&
                        (expelAgent.holdingLocks().Contains(expelNodeId) 
                    || expelAgent.Path.Contains(expelNodeId) == false 
                    || (blockAgent.isAvoid && blockAgent.isCurrentGoalKey)
                    || (blockAgent.SiteID == blockAgent.CurrentGoal && (DateTime.Now - blockAgent.setAvoidTime).TotalSeconds > 20)
                    || ((blockAgent.Path == null || blockAgent.Path.Count == 0) && (DateTime.Now - blockAgent.setAvoidTime).TotalSeconds > 5)))
                {
                    if (expelAgent.holdingLocks().Contains(expelNodeId)) 
                    {
                        Diagnosis.Post("Cancel Avoid, AgentID: " + expelAgentId + " Expel NodeID: " + expelNodeId + " is still holding lock, Cancel Avoid ...");
                    }
                    if (expelAgent.Path.Contains(expelNodeId) == false)
                    {
                        Diagnosis.Post("Cancel Avoid, AgentID: " + expelAgentId + " Expel NodeID: " + expelNodeId + " is not in path, Cancel Avoid ...");
                    }
                    if (blockAgent.isAvoid && blockAgent.isCurrentGoalKey)
                    {
                        Diagnosis.Post("Cancel Avoid, AgentID: " + blockAgentId + " is Avoiding, Cancel Avoid ...");
                    }
                    if (blockAgent.SiteID == blockAgent.CurrentGoal && (DateTime.Now - blockAgent.setAvoidTime).TotalSeconds > 20)
                    {
                        Diagnosis.Post("Cancel Avoid, AgentID: " + blockAgentId + " waiting too much, Cancel Avoid ...");
                    }
                    if ((blockAgent.Path == null || blockAgent.Path.Count == 0) && (DateTime.Now - blockAgent.setAvoidTime).TotalSeconds > 5)
                    {
                        Diagnosis.Post("Cancel Avoid, AgentID: " + blockAgentId + " do not have path, Cancel Avoid ...");
                    }
                    RemoveAvoid(avoidId);
                }
            }
            Diagnosis.Post("*************************************************************************************");
        }

        private void RemoveAvoid(int avoidId)
        {
            Avoid avoid = avoidTable[avoidId];
            var expelNodeId = avoid.expelNodeId;
            var hideNodeId = avoid.hideNodeId;
            expelNodeTable.Remove(expelNodeId);
            hideNodeTable.Remove(hideNodeId);

            var expelAgentId = avoid.expelAgentId;
            var expelAgent = AgentManager.agentList[expelAgentId];
            var blockAgentId = avoid.blockAgentId;
            var blockAgent = AgentManager.agentList[blockAgentId];

            blockAgent.CancelAvoid();
            blockAgent.avoidFor = -1;
            expelAgent.isBeenAvoid = false;
            expelAgent.beenAvoidFor = -1;

            expelAgent.CancelExpel(expelNodeId);
            avoidTable.Remove(avoidId);
            Diagnosis.Post("AgentID: " + blockAgentId + " Cancel Avoid, Release Expel NodeID: " + expelNodeId);

        }
        /*
        public int FindHideNodeId(Agent blockAgent, int expelNodeId, List<int> avoidStopIdList, List<int> inVaildNodeIdList)
        {
            Diagnosis.Post("AgentID: " + blockAgent.Id + " Searching for Hide Node ... ");

            foreach (var nodeId in hideNodeTable.Keys) 
            {
                avoidStopIdList.Add(nodeId);
            }
            foreach (var sector in Graph.sectorDict.Values)
            {
                if (sector.beLockedCar != -1 && sector.beLockedCar != blockAgent.Id)
                {
                    foreach (var node in sector.NodeList)
                    {
                        avoidStopIdList.Add(node.id);
                    }
                }
            }
            
            var hideNodeId = NodeFinder.FindAvoidNode(Graph, expelNodeId, avoidStopIdList, inVaildNodeIdList, blockAgent.have_goods);
            return hideNodeId;
        }
        */
    }
}
