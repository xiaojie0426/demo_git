using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrafficControlCore.DispatchSystem.Graph;
using TrafficControlCore.DispatchSystem;
using SimpleCore.Library;
using static TrafficControlCore.Utils.Geometry;
using SimpleCore;

namespace TrafficControlCore.TaskSystem
{
    public static class allocationHelper
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

        private static int TIMEOUT_THRESHOLD1 = 1800;
        private static int TIMEOUT_THRESHOLD2 = 3600;
        private static int TIMEOUT_THRESHOLD3 = 5400;

        public static Dictionary<int, int> PortLimit;
        public static int PORT_LIMIT_NUM = 1;

        // 是否需要动态选车
        private static bool needDynamicChooseTask(Agent agent)
        {
            if (agent.State != carState.PICKUP) return false; // 已经不是装货状态，返回false 
            var taskId = agent.TaskId;
            var task = TaskManager.getTask(taskId);
            if (task.taskStatus != TaskStatus.Ready) return true; // 原先的任务已经被占据，需要重新匹配

            if (haveValidTarget(taskId) == false) return true;
            if (haveValidPath(taskId, agent) == false) return true;
            // 下面讨论 agent.State == PICKUP and task.status == Ready 的情况，且此时一定是AfterFetch

            var targetPoint = Graph.nodeDict[task.getCargoSite()];
            var nowPoint = Graph.nodeDict[agent.SiteID];
            if (Manhattan(Graph, targetPoint, nowPoint) < TaskManager.DynamicDistanceThreshold) return false;


            lock (agent.GetCar.obj)
            {
                foreach (var site in agent.holdingLocks())
                {
                    var checkNode = Graph.nodeDict[site];
                    if (Graph.sectorDict.ContainsKey(checkNode.sectorId) == false) return false;
                    var sector = Graph.sectorDict[checkNode.sectorId];
                    if (sector.type == Sector.SECTOR_TYPE.DELIVERY) return false;
                }
            }
            return true;
        }

        public static bool haveValidTarget(String taskId)
        {
            if (TaskManager.TaskList.ContainsKey(taskId) == false) return false;
            var task = TaskManager.getTask(taskId);
            var carGoNode = Graph.nodeDict[task.getCargoSite()];

            if (carGoNode.taskIds.Count <= 0) return false;

            if (task.taskType == TaskType.OutFlow || task.taskType == TaskType.Shift)
            {
                var sector = Graph.sectorDict[carGoNode.sectorId];
                if (sector.getCargoIndexList().Count == 0) return false;
                var validPointList = sector.generateValidTarget(carState.PICKUP);
                return validPointList.Count > 0;
            }

            // 检修区相关，如果任务位于检修区sector，则不存在ValidTarget
            var shelfNode = Graph.nodeDict[task.getShelf()];
            var shelfSector = Graph.sectorDict[shelfNode.sectorId];
            if (shelfSector.isDisabled == true) return false;
            if (task.taskType == TaskType.Shift)
            {
                shelfNode = Graph.nodeDict[task.getShelf(0)];
                shelfSector = Graph.sectorDict[shelfNode.sectorId];
                if (shelfSector.isDisabled == true) return false;
            }

            return true;
        }
        private static bool haveValidPath(String taskId, Agent agent = null)
        {
            var task = TaskManager.getTask(taskId);
            var cargoSite = task.getCargoSite();
            var desitination = task.getDestination();

            var cargoNode = Graph.nodeDict[cargoSite];
            var desitinationNode = Graph.nodeDict[desitination];
            List<int> lockList = new List<int> { };

            if (agent == null)
            {
                if (cargoNode.isDisabled == true || desitinationNode.isDisabled == true) return false;
                // var firstPath = SectorAstar.haveValidPath(Graph, agent.getLastHoldingLockId(), cargoSite, lockList, false);
                var secondPath = true; //= SectorAstar.haveValidPath(Graph, cargoSite, desitination, lockList, true);
                return secondPath;
            }
            else
            {
                var agentNode = Graph.nodeDict[agent.SiteID];
                if (cargoNode.isDisabled == true || desitinationNode.isDisabled == true || agentNode.isDisabled == true) return false;

                var firstPath = SectorAstar.haveValidPath(Graph, cargoSite, agent.getLastHoldingLockId(), lockList, false);
                var secondPath = true;//  SectorAstar.haveValidPath(Graph, cargoSite, desitination, lockList, true);
                return firstPath && secondPath;
            }
        }

        private static bool checkAgentCanReceiveTask(Agent agent)
        {
            if (agent.State != carState.FREE) return false;
            lock (agent.GetCar.obj)
            {
                foreach (var site in agent.holdingLocks())
                {
                    /*
                    var site = agent.GetCar.status.holdingLocks[i];
                    if (Graph.nodeId2SectorId.ContainsKey(site) == false) 
                        return false; // 不存在对应的site，不可以堵塞 接受新的任务

                    var node = Graph.nodeDict[site];
                    if (node.x >= SectorGraph.MAIN_LINE_X) return false;
                    */
                    if (Graph.nodeId2SectorId.ContainsKey(site) == true)
                    {
                        var sector = Graph.sectorDict[Graph.nodeId2SectorId[site]];
                        if (sector.isShelf == false) continue;
                        if (sector.checkOtherActiveAgent(agent.Id) == true)
                            return false;
                    }
                }
            }

            // if (agent.getBattery() < 20) return false;  // 低电量

            return true;
        }

        private static bool isTheSameLayout(String taskId, Agent agent)
        {
            var task = TaskManager.getTask(taskId);
            var targetNodeId = task.getCargoSite();
            var targetNode = Graph.nodeDict[targetNodeId];
            if (targetNode.layout == agent.GetLayout()) return true;
            return false;
        }

        private static bool checkTaskTypeConflict(String taskId)  // 返回1为产生了冲突，返回0为不产生冲突
        {
            if (taskId == "-1" || TaskManager.TaskList.ContainsKey(taskId) == false) return true;
            var task = TaskManager.getTask(taskId);
            var targetId = task.getShelf();
            var targetPoint = Graph.nodeDict[targetId];
            if (Graph.nodeId2SectorId.ContainsKey(targetId) == false) return false;
            var sector = Graph.sectorDict[targetPoint.sectorId];

            if (sector.isCharged == true)
            {
                if (ChargeManager.canReceiveTask(sector.id) == false)
                    return true;
            }

            if (task.taskType == TaskType.InFlow)
            {
                if (sector.executingTaskType == Sector.ExecutingTask_Type.IN) return false;
                if (sector.executingTaskType == Sector.ExecutingTask_Type.DEFAULT) return false;
                return true;
            }
            else if (task.taskType == TaskType.OutFlow)
            {
                if (sector.executingTaskType == Sector.ExecutingTask_Type.OUT) return false;
                if (sector.executingTaskType == Sector.ExecutingTask_Type.DEFAULT) return false;
                return true;
            }
            else if (task.taskType == TaskType.Shift)
            {
                if (sector.executingTaskType == Sector.ExecutingTask_Type.IN) return false;
                if (sector.executingTaskType == Sector.ExecutingTask_Type.DEFAULT) return false;
                targetPoint = Graph.nodeDict[task.getShelf(0)];
                sector = Graph.sectorDict[targetPoint.sectorId];
                if (sector.executingTaskType == Sector.ExecutingTask_Type.OUT) return false;
                if (sector.executingTaskType == Sector.ExecutingTask_Type.DEFAULT) return false;
                return true;
            }
            return true;
        }

        private static bool canPublishOutflowTask(String taskId)
        {
            var task = TaskManager.getTask(taskId);
            if (task.taskType != TaskType.OutFlow) return true;
            var cargoSite = Graph.nodeDict[task.getCargoSite()];
            var portSite = Graph.nodeDict[task.getPort()];
            if (cargoSite.x >= Graph.STORAGE_LINE_X)
            {
                if (PortLimit.ContainsKey(portSite.id) == false)
                    PortLimit.Add(portSite.id, 0);
                if (PortLimit[portSite.id] >= PORT_LIMIT_NUM
                    && SimpleLib.GetSite(portSite.id).tags.Contains("Goods")) return false;
            }
            return true;
        }

        private static bool allocationIsValid(String taskId, Agent agent, bool isSet = true)
        {
            if (isSet == true)
                return
                    isTheSameLayout(taskId, agent) == true
                    && checkTaskTypeConflict(taskId) == false       // sector 内部 任务类型不冲突
                    && checkAgentCanReceiveTask(agent) == true      // agent 本身位置可以接受该任务
                    && agent.delayTime == 0                         // agent 接受任务冷却时间
                    && haveValidTarget(taskId) == true              // 任务有合法的目标点，存在货位 且 不位于检修区
                    && haveValidPath(taskId, agent) == true               // 任务有合法的路径，不受检修区干扰
                    && canPublishOutflowTask(taskId) == true;
            else
                return
                    isTheSameLayout(taskId, agent) == true
                    && checkTaskTypeConflict(taskId) == false       // sector 内部 任务类型不冲突
                                                                    // && checkAgentCanReceiveTask(agent) == true      // agent 本身位置可以接受该任务
                    && agent.delayTime == 0                         // agent 接受任务冷却时间
                    && haveValidTarget(taskId) == true              // 任务有合法的目标点，存在货位 且 不位于检修区
                    && canPublishOutflowTask(taskId) == true;
        }

        private static void HandleSpecifiedTasks()
        {
            var manualTasks = TaskManager.getReadyTasks(TaskType.Manual);
            var manualTaskIds = new List<string>();
            foreach (var task in manualTasks)
            {
                var agent = AgentManager.getAgent(task.Car);
                if (agent.IsFault == true) continue;
                agent.offline();
                agent.finalAction = task.finalAction;
                agent.initGoalList();
                agent.addGoal(task.sites[0].siteID, true);
                manualTaskIds.Add(task.Id);
            }
            foreach (var taskId in manualTaskIds)
            {
                TaskManager.TaskList.Remove(taskId);
            }
        }

        private static void HandleLaterTasks()
        {
            foreach (var agent in AgentManager.getAllAgents())
            {
                if (agent.State != carState.FREE || agent.isManual == true) continue;
                if (agent.Later_TaskId == "-1" || TaskManager.TaskList.ContainsKey(agent.Later_TaskId) == false) return;
                var TaskId = agent.Later_TaskId;
                var task = TaskManager.getTask(TaskId);
                if(task.taskStatus == TaskStatus.Ready && allocationIsValid(TaskId, agent) == true)
                {
                    agent.PICKUP(TaskId);
                    TaskManager.Pickup(TaskId, agent.Id);
                }
            }
            foreach (var agent in AgentManager.getAllAgents())
                agent.Later_TaskId = "-1";
        }


        public static void TaskAllocation()
        {

            HandleSpecifiedTasks();

            HandleLaterTasks();

            if (DispatchMisson.DurTime % 20 == 0)
            {
                foreach (var agent in AgentManager.getAllAgents())
                {
                    if (agent.isManual == true) continue;

                    if (needDynamicChooseTask(agent))
                    {
                        TaskManager.Release(agent.TaskId, agent.Id);
                        agent.FREE();
                    }
                }
            }
            
            // 出库限制
            if (PortLimit == null) PortLimit = new Dictionary<int, int>();
            PortLimit.Clear();
            foreach (var agent in AgentManager.getAllAgents())
            {
                if (agent.State != carState.WORK) continue;
                if (agent.isManual == true) continue;

                var task = TaskManager.getTask(agent.TaskId);
                if (task.taskType == TaskType.OutFlow)
                {
                    if (PortLimit.ContainsKey(task.getPort()) == false)
                        PortLimit.Add(task.getPort(), 0);
                    PortLimit[task.getPort()]++;
                }
            }

            var allocationAgents = AgentManager.getAllocationAgent();
            // var freeAgents = AgentManager.getFreeAgent();
            Diagnosis.Post("Free agents num: " + allocationAgents.Count);
            var allocationList = allocateTasks(allocationAgents);             // allocationList中不包含Manual类型的Task
            Diagnosis.Post("Allocation num: " + allocationList.Count);
            if (allocationList.Count <= 0) return;
            setAllocationList(allocationList);
        }


        private static Dictionary<int, String> allocateTasks(List<int> allocationAgent)
        {
            var alllocationList = new Dictionary<int, String>();

            List<String> readyTasks = TaskManager.getTaskListForAllocation();

            Diagnosis.Post("ReadyTasksCount: " + readyTasks.Count);

            if (allocationAgent.Count <= 0 || readyTasks.Count <= 0) goto Label;

            // 2. 算法：KM
            int Dimension = Math.Max(readyTasks.Count(), allocationAgent.Count());  // to get a Square Matrix. 
            int[,] costMatrix = new int[Dimension, Dimension];
            for (int i = 0; i < Dimension; i++)
                for (int j = 0; j < Dimension; j++)
                    costMatrix[i, j] = -100000000;


            for (int j = 0; j < Dimension && j < readyTasks.Count; j++)
            {
                var taskId = readyTasks[j];
                var task = TaskManager.getTask(taskId);

                if (canPublishOutflowTask(taskId) == false) continue;

                for (int i = 0; i < Dimension && i < allocationAgent.Count; i++)  // 构建代价矩阵
                {
                    var agentId = allocationAgent[i];
                    var agent = AgentManager.getAgent(agentId);
                    if (isTheSameLayout(taskId, agent) == true)
                    // if (allocationIsValid(taskId, agent, false))
                    {
                        costMatrix[i, j] = -calAllocationCost(task, agent);
                    }
                }
            }
            KuhnMunkres km = new KuhnMunkres(costMatrix.GetLength(0), costMatrix.GetLength(0));
            km.WeightMatrix = costMatrix;
            int maxWeight = km.KM(); // 获取最大权
            var matchs = km.MatchY.ToList().Where(p => p != -1).ToList(); // 获取匹配

            for (int i = 0; i < matchs.Count; i++)
            {
                if (costMatrix[matchs[i], i] != -100000000)
                {
                    var agentId = allocationAgent[matchs[i]];
                    var taskAllocateInfo = readyTasks[i];

                    alllocationList.Add(agentId, taskAllocateInfo);
                }
            }

        Label:
            return alllocationList;
        }
        private static void setAllocationList(Dictionary<int, String> allocations)
        {
            var activeAgentNum = 0;
            foreach (var agent in AgentManager.getAllAgents())
            {
                if (agent.State == carState.PICKUP || agent.State == carState.WORK) activeAgentNum++;
            }

            var AllocationSetNum = 0;
            foreach (var pair in allocations)
            {
                var agent = AgentManager.getAgent(pair.Key);
                var TaskId = pair.Value;
                /*if(agent.State == carState.WORK)
                {
                    Diagnosis.Post("later AgentID: " + agent.Id + " TaskID: " + TaskId);
                }*/

                if (agent.State == carState.WORK)
                {
                    agent.Later_TaskId = TaskId;
                    if (Graph.ISLARGEGRAPH == false) Diagnosis.Post("later Set AgentID: " + agent.Id + " TaskID: " + TaskId);
                    continue;
                }

                if (allocationIsValid(TaskId, agent) 
                    && (Graph.ISLARGEGRAPH == true || (Graph.ISLARGEGRAPH == false && activeAgentNum + AllocationSetNum <= 15)) )//  && haveValidPath(TaskId, agent) == true)
                {
                    agent.PICKUP(TaskId);
                    TaskManager.Pickup(TaskId, agent.Id);

                    if (Graph.ISLARGEGRAPH == false)
                    {
                        Diagnosis.Post("successful Set AgentID: " + agent.Id + " TaskID: " + TaskId);
                    }
                    AllocationSetNum++;
                }
                else
                {
                    var task = TaskManager.getTask(TaskId);

                    if (Graph.ISLARGEGRAPH == false)
                    {
                        Diagnosis.Post("The Allocation is invalid: " + agent.Id + " TaskID: " + TaskId + " firstDes: " + task.getCargoSite() + " secondDes: " + task.getDestination());
                        Diagnosis.Post("reason: " + checkTaskTypeConflict(TaskId) + " , " + checkAgentCanReceiveTask(agent) + " , " + haveValidTarget(TaskId) + " , " + haveValidPath(TaskId, agent));
                    }
                }
            }
            Diagnosis.Post("The number of successful Allocation: " + AllocationSetNum);
        }

        private static int calAllocationCost(AgvTask task, Agent agent)
        {
            int targetPoint = task.getCargoSite();
            var targetNode = Graph.nodeDict[targetPoint];
            int value = 0;
            if (agent.State == carState.WORK)
            {
                var tmpNode = Graph.nodeDict[agent.CurrentKeyGoal];
                value += Manhattan(Graph, tmpNode, targetNode);
            }
            var curNode = Graph.nodeDict[agent.SiteID];
            value += Manhattan(Graph, curNode, targetNode);

            var priority = task.Priority;
            if (priority > 5) priority = 5;
            if (priority < 0) priority = 0;

            value = (int)(value * 0.2 * (6 - priority));

            var time = (DateTime.Now - task.sendTime).TotalSeconds;
            if (time > TIMEOUT_THRESHOLD1) value = (int)(value * 0.75);
            else if (time > TIMEOUT_THRESHOLD2) value = (int)(value * 0.50);
            else if (time > TIMEOUT_THRESHOLD3) value = (int)(value * 0.1);

            return value;
        }
    }
}
