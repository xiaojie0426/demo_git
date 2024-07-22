/************************************************************************/
/* File:	  PathPlan.cs										        */
/* Func:	  根据goal生成Path                                           */
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
using TrafficControlCore.AgentSystem;
using TrafficControlCore.DispatchSystem.Graph;

namespace TrafficControlCore.DispatchSystem.Pathplan
{
    public class AgvPathPlan
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

        private static List<int> lockList = new List<int> { };
        public void PathPlan(Agent agent)
        {
            if (!agent.haveGoals())
            {
                var str = "Agent Id: " + agent.Id + " Do not have a goal! ";
                agent.Path = new List<int>();
                agent.SectorPath = new List<int>();
                agent.GetCar.tempDst = agent.SiteID;
                Diagnosis.Post(str);
                return;
            }
            /*
            if (SectorAstar.needPathPlan(Graph, agent) == false)
            {
                var str = "Agent Id: " + agent.Id + " Do NOT need replan! ";
                Diagnosis.Post(str);
                return;
            }
            else
            {
                var str = "Agent Id: " + agent.Id + " Do need replan! ";
                Diagnosis.Post(str);
            }
            */
            var robotId = agent.Id;
            var curGoal = agent.CurrentGoal;
            var car = SimpleLib.GetCar(robotId) as DummyCar;
            
            var path = SectorAstar.FindPath(Graph, agent.getLastHoldingLockId(), curGoal, lockList, agent, false);
            agent.pathStatus = 0;

            if (path == null || path.Count == 0)
            {
                path = SectorAstar.FindPath(Graph, agent.getLastHoldingLockId(), curGoal, lockList, agent, true);
                agent.pathStatus = 1;
            }

            if (path == null || path.Count == 0)
            {
                agent.Path = new List<int>();
                Diagnosis.Post("DO NOT FIND PATH! AgentId: " + robotId + " and must WAIT ");
                agent.pathStatus = 2;
                return;
            }
            agent.SectorPath = path2SectorPath(path);
            agent.Path = path;
            car.tempDst = path[path.Count - 1];
        }

        public List<int> path2SectorPath(List<int> path)
        {
            var sectorPath = new List<int>();
            foreach (var nodeId in path)
            {
                var node = Graph.nodeDict[nodeId];
                var sectorId = node.sectorId;
                
                if (sectorPath.Count > 0 && sectorId == sectorPath[sectorPath.Count - 1]) continue;
                    
                sectorPath.Add(sectorId);
            }
            return sectorPath;
        }
    }
}
