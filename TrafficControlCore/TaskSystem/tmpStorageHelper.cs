using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrafficControlCore.DispatchSystem.Graph;
using TrafficControlCore.DispatchSystem;
using SimpleCore.Library;
using static TrafficControlCore.Utils.Geometry;
using static System.Windows.Forms.AxHost;
using System.Xml;
using System.IO;
using static System.Collections.Specialized.BitVector32;
using System.Xml.Linq;
using System.Data.OleDb;
using SimpleCore;

namespace TrafficControlCore.TaskSystem
{
    public static class tmpStorageHelper
    {
        public static SectorGraph Graph
        {
            get { return DispatchMisson.Graph; }
        }
        public static LockAndReservation LockAndReservationManager
        {
            get { return DispatchMisson.LockAndReservationManager; }
        }
        public static AgvAgentManager AgentManager
        {
            get { return DispatchMisson.AgentManager; }
        }
        public static AgvTaskManager TaskManager
        {
            get { return DispatchMisson.TaskManager; }
        }
        public static AgvChargeManager ChargeManager
        {
            get { return DispatchMisson.ChargeManager; }
        }

        public static void tmpStorageChecker()
        {
            // var agents = genStorageAgents();
            allocationHelper.PortLimit = new Dictionary<int, int>();
            gotoStorage();
        }

        // public static Dictionary<int, int> PortLimit;
        // public static int PORT_LIMIT_NUM = 3;

        private static void gotoStorage()
        {
            // foreach (var agent in agents)
            foreach (var agent in AgentManager.getAllAgents())
            {
                if (agent.State != carState.WORK) continue;
                if (agent.isManual == true) continue;

                var oldTask = TaskManager.getTask(agent.TaskId);
                if (oldTask.taskType == TaskType.OutFlow)
                {
                    if (allocationHelper.PortLimit.ContainsKey(oldTask.getPort()) == false)
                        allocationHelper.PortLimit.Add(oldTask.getPort(), 0);
                    allocationHelper.PortLimit[oldTask.getPort()]++;
                }
                
                if (isNeedRollBack(agent))
                {
                    RollbackStorage(agent);
                    continue;
                }
                
                if (isNeedStorage(agent) == false) continue;

                var tmpGoal = genStorageNode(agent, oldTask);
                if (tmpGoal == null)
                {
                    Diagnosis.Post("do not have valid storageNode.");
                    return; 
                }

                var shiftTask = genShiftTask(agent, oldTask, tmpGoal);
                //var flowTask = genFlowTask(agent, oldTask, tmpGoal);
                //var flag = spliteTask(agent, oldTask, shiftTask, flowTask);
                var flag = spliteTask(agent, oldTask, shiftTask);
                if (flag == true)
                {
                    Diagnosis.Post("agent: " + agent.Id + ", task is splited, oldTask: " + oldTask.Id + " shiftTask: " + shiftTask.Id);
                    allocationHelper.PortLimit[oldTask.getPort()]--;
                }
                else
                {
                    Diagnosis.Post("agent: " + agent.Id + ", task CANNOT splited, FAIL.");
                }
            }
        }
        /*
        private static List<Agent> genStorageAgents()
        {
            var agents = new List<Agent>();
            foreach (var agent in AgentManager.getAllAgents())
            {
                if (isNeedStorage(agent) == true) agents.Add(agent);
            }
            return agents;
        }
        */
        private static Node genStorageNode (Agent agent, AgvTask oldTask)
        {
            var result = NodeFinder.FindNearestNode(Graph, agent, agent.getLastHoldingLockId(), NodeFinder.FINDMODE.Storage);
            if (result == -1) return null;
            else return Graph.nodeDict[result];
        }
        private static AgvTask genShiftTask(Agent agent, AgvTask oldTask, Node tmpGoal)
        {
            List<(int, bool)> movements = new() {
                                    (oldTask.getCargoSite(), true),
                                    (tmpGoal.id, true),
                                    };
            AgvTask shiftTask = new AgvTask(movements)
            {
                Car = -1,
                sites = movements,
                sendTime = oldTask.sendTime,
                taskType = TaskType.Shift,
            };

            return shiftTask;
        }
        private static AgvTask genFlowTask(Agent agent, AgvTask oldTask, Node tmpGoal)
        {
            List<(int, bool)> movements = new() {
                                    (tmpGoal.id, true),
                                    (oldTask.getDestination(), true),
                                    };
            AgvTask flowTask = new AgvTask(movements)
            {
                Car = -1,
                sites = movements,
                sendTime = oldTask.sendTime,
                taskType = TaskType.OutFlow,
                Priority = 5,
            };
            return flowTask;
        }
        private static bool spliteTask(Agent agent, AgvTask oldTask, AgvTask shiftTask)
        {
            // oldTask.isNeedAllocation = false;
            TaskManager.Stop(oldTask.Id);
            agent.FREE();

            TaskManager.TaskList.Add(shiftTask.Id, shiftTask);

            agent.State = carState.WORK;
            agent.TaskId = shiftTask.Id;
            agent.addGoal(TaskManager.getSites(shiftTask.Id)[1], true);
            // agent.Goals.Enqueue(TaskManager.getSites(shiftTask.Id)[1]);

            shiftTask.taskStatus = TaskStatus.Executing;
            shiftTask.executingStatus = ExecutingStatus.AfterFetch;
            shiftTask.Car = agent.Id;

            var node = Graph.nodeDict[shiftTask.getDestination()];
            var sectorId = node.sectorId;
            var sector = Graph.sectorDict[sectorId];
            storageRes[sectorId]--;

            shiftTask.realTaskId = oldTask.Id;
            sector.addExecutingTask(Sector.ExecutingTask_Type.IN);

            return true;
        }
        private static bool isNeedStorage(Agent agent)
        {
            var oldTask = TaskManager.getTask(agent.TaskId);
            if (oldTask.taskType != TaskType.OutFlow) return false;
            // 只处理带货小车 且 出库任务，此时可能会堵住，因此需要进行储存

            if (agent.pathStatus != 0) return true;
            /*
            for (int i = 0; i < agent.holdingLocks().Count; ++i)
            {
                var node = Graph.nodeDict[agent.holdingLocks()[i]];
                if (node.x >= SectorGraph.STORAGE_LINE_X) return false;
            }
            */
            var portSite = oldTask.getPort();
            if (allocationHelper.PortLimit.ContainsKey(portSite) == false)
                allocationHelper.PortLimit.Add(portSite, 0);
            if (allocationHelper.PortLimit[portSite] > allocationHelper.PORT_LIMIT_NUM
                && SimpleLib.GetSite(portSite).tags.Contains("Goods")
                ) return true;
            // 不存在完整的不需要经过检修区的路径，需要进行任务拆分
            
            return false;
        }

        private static bool isNeedRollBack(Agent agent)
        {
            var task = TaskManager.getTask(agent.TaskId);
            /*
            for (int i = 0; i < agent.holdingLocks().Count; ++i)
            {
                var node = Graph.nodeDict[agent.holdingLocks()[i]];
                if (node.x >= SectorGraph.STORAGE_LINE_X) return false;
            }
            */
            if (TaskManager.TaskList.ContainsKey(task.realTaskId))
            {
                var realTask = TaskManager.getTask(task.realTaskId);
                Diagnosis.Post("reaTaskId: " + realTask.Id + " desitination: " + realTask.getDestination() + " holding: " + agent.getLastHoldingLockId());
                // var path = SectorAstar.FindPath(Graph, agent.getLastHoldingLockId(), realTask.getDestination(), new List<int>(), agent, false);
                var path = SectorAstar.haveValidPath(Graph, agent.getLastHoldingLockId(), realTask.getDestination(), new List<int>(), false);
                // Diagnosis.Post(" pathCount: " + path.Count);
                if (path 
                    && SimpleLib.GetSite(realTask.getPort()).tags.Contains("Goods") == false
                    && agent.have_goods == true
                    /*(
                    allocationHelper.PortLimit.ContainsKey(realTask.getPort()) == false || 
                    allocationHelper.PortLimit[realTask.getPort()] < allocationHelper.PORT_LIMIT_NUM)*/)
                    return true;
            }
            return false;
        }

        private static void RollbackStorage(Agent agent) 
        {
            agent.initGoalList();
            agent.State = carState.WORK;
            var task = TaskManager.getTask(agent.TaskId);
            var realTask = TaskManager.getTask(task.realTaskId);
            agent.State = carState.WORK;
            agent.TaskId = realTask.Id;
            agent.addGoal(TaskManager.getSites(realTask.Id)[1], true);
            realTask.taskStatus = TaskStatus.Executing;
            realTask.Car = agent.Id;

            var node = Graph.nodeDict[task.getDestination()];
            var sectorId = node.sectorId;
            storageRes[sectorId]++;
            TaskManager.Delete(task.Id);
            if (allocationHelper.PortLimit.ContainsKey(realTask.getPort()) == false)
                allocationHelper.PortLimit.Add(realTask.getPort(), 0);
            allocationHelper.PortLimit[realTask.getPort()]++;
        }

        public static List<Sector> storageSector;
        public static Dictionary<int, int> storageRes { get; set; }
        
        public static void init()
        {
            storageRes = new Dictionary<int, int>();
            storageSector = new List<Sector>();
            foreach (var sector in Graph.sectorDict.Values)
            {
                if (sector.isStorage == true)
                {
                    storageSector.Add(sector);
                    storageRes.Add(sector.id, sector.NodeList.Count);
                }
            }
        }
        public static void storageResReset()
        {
            foreach (var sector in storageSector)
            {
                var goodsNum = sector.getCargoIndexList().Count;
                storageRes[sector.id] = sector.NodeList.Count - goodsNum;
            }
            foreach (var agent in AgentManager.getAllAgents())
            {
                if (agent.State != carState.WORK || agent.isManual == true) continue;
                var goal = Graph.nodeDict[agent.CurrentKeyGoal];
                if (Graph.sectorDict.ContainsKey(goal.sectorId) == false) continue;
                var sectorId = goal.sectorId;
                if (storageRes.ContainsKey(sectorId) == true)
                    storageRes[sectorId]--;
            }
        }

    }
}
