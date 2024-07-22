/************************************************************************/
/* File:	  Agent.cs										            */
/* Func:	  AgentManager，小车状态管理                                 */
/* Author:	  Tan Yuhong                                                */
/************************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleComposer;
using SimpleComposer.RCS;
using SimpleComposer.UI;
using SimpleCore;
using SimpleCore.Library;
using SimpleCore.PropType;

using TrafficControlCore.DispatchSystem;
using TrafficControlCore.DispatchSystem.Graph;
using TrafficControlCore.TaskSystem;
using static System.Collections.Specialized.BitVector32;
using static TrafficControlCore.DispatchSystem.DispatchMisson;
using static TrafficControlCore.AgentSystem.ActionExecutor;
using TrafficControlCore.AgentSystem;
using static TrafficControlCore.Utils.Geometry;
using System.Net;
using TrafficControlCore.Cars;

namespace TrafficControlCore.DispatchSystem
{
    public enum carState
    {
        FREE = 0,
        PICKUP = 1,
        WORK = 2,
        CHARGE = 3,
        OTHER = 4
    }

    public class Agent
    {
        public int deadClock;
        public Agent(int id)
        {
            Id = id;
            state = carState.FREE;
            path = new List<int>();
            deadClock = 0;
            delayTime = 0; // 下次接受任务的冷却时间
            DeadInducementAgentId = -1;
            isAvoid = false;
            isManual = false;  // 不是手动被指定任务状态
            avoidFor = -1;
            isBeenAvoid = false;
            beenAvoidFor = -1;
            iscurGoalChange = false;
            expelInfo = new Dictionary<int, int>();
            pathStatus = 0;
            setAvoidTime = DateTime.Now;
            finishAvoidTime = DateTime.Now;
        }

        public DateTime setAvoidTime;
        public DateTime finishAvoidTime;
        public int delayTime;
        public readonly int Id = -1;
        public bool isManual;   // 手动下发点到点任务，如果不存在则不影响整体系统运行。
        public FinalAction finalAction;  // 手动下发点到点任务，最后的action
        public int pathStatus;  // pathPlanning得到的路径质量，0表示存在路径且不需要经过检修区，1表示存在需要经过检修区的路径，2表示不存在路径

        public bool iscurGoalChange;

        private string taskId = "-1";
        private string later_taskId = "-1";

        public string TaskId
        {
            get { return taskId; }
            set { taskId = value; }
        }
        public string Later_TaskId
        {
            get { return later_taskId; }
            set { later_taskId = value; }
        }

        public float X
        {
            get { return SimpleLib.GetCar(Id).x; }
        }

        public float Y
        {
            get { return SimpleLib.GetCar(Id).y; }
        }

        public float Th
        {
            get { return SimpleLib.GetCar(Id).th; }
        }

        public int SiteID
        {
            get
            {
                var siteID = -1;
                if (SimpleLib.GetCar(Id).status.holdingLocks.Count > 0)
                {
                    siteID = SimpleLib.GetCar(Id).status.holdingLocks[0];
                }
                return siteID;
            }
        }

        public bool IsOnline
        {
            get
            {
                var car = GetCar;
                if (car.lstatus == "上线" && car.tags.Contains("fault") == false) return true;
                return false;
            }
        }

        public bool IsFault
        {
            get { return GetCar.tags.Contains("fault");  }
        }

        public void offline()
        {
            GetCar.lstatus = "下线";

            if (isManual == false)    // 从存在任务转化为手动
            {
                if (State == carState.CHARGE)
                    ChargeManager.gotoFree(this);
                if (State == carState.PICKUP)
                {
                    TaskManager.Release(TaskId, Id);
                    FREE();
                }
                if (State == carState.FREE)
                {
                    FREE();
                }
                initGoalList();
                if (have_goods) State = carState.WORK;
                else State = carState.FREE;
            }
            isManual = true;

            return;
        }

        public void online()
        {
            GetCar.lstatus = "上线";
            if (isManual == true)
            {
                initGoalList();
                if (taskId != "-1" && have_goods)
                {
                    State = carState.WORK;
                    var task = TaskManager.getTask(taskId);
                    addGoal(task.sites[1].siteID, true);
                }
            }
            isManual = false;
            return;
        }
        private carState state = carState.FREE;

        public SectorGraph Graph
        {
            get { return DispatchMisson.Graph; }
        }

        public carState State
        {
            get { return state; }
            set { state = value; }
        }

        private List<int> path = new List<int>();
        private List<int> sectorPath = new List<int>();

        public List<int> Path
        {
            get { return path; }
            set { path = value; }
        }
        public List<int> SectorPath
        {
            get { return sectorPath; }
            set { sectorPath = value; }
        }

        private Queue<(int, bool)> goals = new Queue<(int, bool)>();
        
        public Queue<(int, bool)> Goals
        {
            get { return goals; }
        }
        
        public int CurrentGoal
        {
            get {
                if (goals == null || goals.Count == 0) return -1;
                return goals.Peek().Item1; 
            }
        }

        public bool isCurrentGoalKey
        {
            get {
                if (goals == null || goals.Count == 0) 
                    return false;
                return goals.Peek().Item2; }
        }

        public int CurrentKeyGoal
        {
            get { 
                foreach(var goal in goals)
                {
                    if (goal.Item2 == true) return goal.Item1;
                }
                return -1;
            }
        }

        public int pathLastPointId
        {
            get
            {
                return path[path.Count - 1];
            }
        }

        public DummyCar GetCar
        {
            get { return SimpleLib.GetCar(Id) as DummyCar; }
        }

        public bool haveGoals()
        {
            return goals != null && goals.Count > 0;
        }
        public int getLastHoldingLockId()
        {
            DummyCar car = GetCar;
            if (car.status.holdingLocks.Count == 0) return SiteID;
            return car.status.holdingLocks[car.status.holdingLocks.Count - 1];
        }

        public List<int> holdingLocks()
        {
            return GetCar.status.holdingLocks;
        }
        public int HoldingLocksCount
        {
            get { return GetCar.status.holdingLocks.Count; }
        }
        public void AddHoldingSite(int point)
        {
            // if (getBattery() <= 1) return;  // 模拟零电量
            GetCar.AddHoldingSite(point);
        }
        public bool have_goods
        {
            get { return GetCar.tags.ContainsKey("status"); }
        }
        public int GetLayout()
        {
            DummyCar car = GetCar;
            var id = car.status.holdingLocks[0];
            return SimpleLib.GetSite(id).layerName[0] - '0';
        }
        public void SetLayout(int num)
        {
            string str = $"{num}g";
            DummyCar car = GetCar;
            car.layerName = str;
            var curNode = Graph.nodeDict[SiteID];
            foreach (var node in Graph.nodeDict.Values)
            {
                if (curNode.x == node.x && curNode.y == node.y && node.layout == num)
                {
                    GetCar.status.holdingLocks[0] = node.id;
                    break;
                }
            }
        }
        public void initGoalList()
        {
            (int, bool) curGoal = (CurrentGoal, isCurrentGoalKey);
            foreach (var tmpgoal in goals)
            {
                if (Graph.nodeDict.ContainsKey(tmpgoal.Item1) == false)
                    continue;
                var node = Graph.nodeDict[tmpgoal.Item1];
                if (node.hiddenCar == Id)
                {
                    node.hiddenCar = -1;
                }
            }
            goals = new Queue<(int, bool)>();
            if (curGoal.Item1 != -1 && curGoal.Item2 == false)
                goals.Enqueue(curGoal);
            iscurGoalChange = true;
        }

        public void addGoal(int goal, bool isKey = true)
        {
            goals.Enqueue((goal, isKey));
            iscurGoalChange = true;
        }
        public void removeCurrentGoal()
        {
            goals.Dequeue();
            iscurGoalChange = true;
        }
        
        public void addGoalinFront(int goal, bool isKey = false)
        {
            Queue<(int, bool)> tmpGoals = new Queue<(int, bool)>();
            tmpGoals.Enqueue((goal, isKey));
            while (goals.Count > 0)
            {
                tmpGoals.Enqueue(goals.Peek());
                goals.Dequeue();
            }
            goals = tmpGoals;
            iscurGoalChange = true;
        }

        public double getBattery()
        {
            return GetCar.getBattery();
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void FREE()
        ///
        /// @brief  小车回归FREE状态
        ///
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void FREE()
        {
            initGoalList();
            // goals = new Queue<(int, bool)>();
            state = carState.FREE;
            taskId = "-1";
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void PICKUP()
        ///
        /// @brief  小车尝试执行具体任务，FREE状态切换为PICKUP状态。
        ///         可能由于任务类型冲突不予执行。
        /// @param  taskAllocation        给定执行任务信息
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void WORK(String TaskId)
        {
            var task = TaskManager.getTask(taskId);
            state = carState.WORK;
            this.TaskId = TaskId;
            addGoal(TaskManager.getSites(TaskId)[1], true);
            // Goals.Enqueue(TaskManager.getSites(TaskId)[1]);
        }
        public void PICKUP(String TaskId)
        {
            initGoalList();
            state = carState.PICKUP;
            this.TaskId = TaskId;
            addGoal(TaskManager.getSites(TaskId)[0], true);
            // Goals.Enqueue(TaskManager.getSites(TaskId)[0]);
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void UpdateState()
        ///
        /// @brief  小车状态PICKUP -> WORK -> FREE，同时判断任务是否FINISH
        ///
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        private void ManualUpdateState()
        {
            if (haveGoals() && isCurrentGoalKey == false && isArriveGoal(SiteID, false, false))
            {
                if (isAvoid == false) removeCurrentGoal();
                // removeCurrentGoal();
            }

            if (isCurrentGoalKey == false) return;

            if (haveGoals() && isCurrentGoalKey == true && isArriveGoal(SiteID, false, true))
            {
                removeCurrentGoal();
                if (finalAction == FinalAction.Fetch)
                {
                    GetCar.tags.Add("status", "Goods");
                    SimpleLib.GetSite(SiteID).tags.Remove("Goods");
                    state = carState.WORK;
                }
                else if (finalAction == FinalAction.Put)
                {
                    if (TaskManager.TaskList.ContainsKey(TaskId))
                        TaskManager.Finish(TaskId, SiteID);
                    GetCar.tags.Remove("status");
                    SimpleLib.GetSite(SiteID).tags.Add("Goods");
                    FREE();
                }
            }
            /*
            if (taskId == "-1")
            {
                Diagnosis.Post("Agent " + Id + " is fetching, waiting for the next command!!");
                return;
            }

            var task = TaskManager.getTask(taskId);
            if (state == carState.FREE && SiteID == CurrentGoal)
            {
                // isManual = false;
                TaskManager.Finish(TaskId);
                FREE();
            }
            if (state == carState.PICKUP && SiteID == CurrentGoal)
            {
                if (TaskManager.TaskList.ContainsKey(taskId) == true)
                {
                    TaskManager.Finish(TaskId);
                    GetCar.tags.Add("status", "Goods");
                    SimpleLib.GetSite(SiteID).tags.Remove("Goods");
                    state = carState.WORK;
                    taskId = "-1";
                }
            }
            if(state == carState.WORK && SiteID == CurrentGoal)
            {
                if (TaskManager.TaskList.ContainsKey(taskId) == true)
                {
                    TaskManager.Finish(TaskId, SiteID);
                    GetCar.tags.Remove("status");
                    SimpleLib.GetSite(SiteID).tags.Add("Goods");
                    // isManual = false;
                    FREE();
                }
            }
            */
            /*
            if (isCurrentGoalKey == false) return;

            if (taskId == "-1")
            {
                Diagnosis.Post("Agent " + Id + " is fetching, waiting for the next command!!");
                return;
            }

            var task = TaskManager.getTask(taskId);
            if (state == carState.FREE && SiteID == CurrentGoal)
            {
                // isManual = false;
                TaskManager.Finish(TaskId);
                FREE();
            }
            if (state == carState.PICKUP && SiteID == CurrentGoal)
            {
                if (TaskManager.TaskList.ContainsKey(taskId) == true)
                {
                    TaskManager.Finish(TaskId);
                    GetCar.tags.Add("status", "Goods");
                    SimpleLib.GetSite(SiteID).tags.Remove("Goods");
                    state = carState.WORK;
                    taskId = "-1";
                }
            }
            if(state == carState.WORK && SiteID == CurrentGoal)
            {
                if (TaskManager.TaskList.ContainsKey(taskId) == true)
                {
                    TaskManager.Finish(TaskId, SiteID);
                    GetCar.tags.Remove("status");
                    SimpleLib.GetSite(SiteID).tags.Add("Goods");
                    // isManual = false;
                    FREE();
                }
            }
            */
        }
        public void UpdateState()
        {
            if (GetCar.status.holdingLocks.Count > 1) return;

            if (delayTime > 0) delayTime--;

            if (isManual == true)
            {
                ManualUpdateState();
                return;
            }

            if (haveGoals() && isCurrentGoalKey == false && isArriveGoal(SiteID, false, false))
            {
                if (isAvoid == false) removeCurrentGoal();
                // removeCurrentGoal();
            }

            if (state == carState.FREE) return;
            else if (state == carState.PICKUP)
            {
                if (isCurrentGoalKey && isArriveGoal(SiteID, false, true))
                {
                    removeCurrentGoal();
                    // Goals.Dequeue();

                    var node = Graph.nodeDict[SiteID];
                    var newTaskId = TaskManager.getEarlyTaskId(node);
                    if (newTaskId == "-1" || TaskManager.TaskList.ContainsKey(newTaskId) == false)
                    {
                        TaskManager.Release(TaskId, Id);
                        FREE();
                        return;
                    }
                    if (newTaskId != TaskId)
                    {
                        TaskManager.Release(TaskId, Id);
                        TaskManager.Pickup(newTaskId, Id);
                    }
                    WORK(newTaskId);
                    TaskManager.Execute(newTaskId, Id, node);
                    GetCar.tags.Add("status", "Goods");
                    SimpleLib.GetSite(SiteID).tags.Remove("Goods");
                }
            }
            else if (state == carState.WORK)
            {
                if (isCurrentGoalKey && isArriveGoal(SiteID, false, true))
                {
                    GetCar.tags.Remove("status");
                    if (TaskManager.getTask(TaskId).taskType == TaskType.InFlow 
                        || TaskManager.getTask(TaskId).taskType == TaskType.Shift)
                        SimpleLib.GetSite(SiteID).tags.Add("Goods");
                    TaskManager.Finish(TaskId, SiteID);
                    FREE();
                }
            }
        }
        public void UpdateLayout()
        {
            if (haveGoals() == false) return;
            if (Graph.nodeDict.ContainsKey(CurrentGoal) == false) return;
            var goalNode = Graph.nodeDict[CurrentGoal];

            if (GetLayout() != goalNode.layout)
            {
                var curNode = Graph.nodeDict[SiteID];
                if (curNode.isHoister == true) SetLayout(goalNode.layout);
            }
        }

        public void initCurPath(int curPoint)
        {
            while (path.Count > 0 && curPoint != path[0])
                path.RemoveAt(0);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void isArriveGoal(int curNodeId, int isAstar = 0)
        ///
        /// @brief  判断小车是否已经到达目标点
        ///         对于换层小车，在规划层目标点仅为电梯口
        ///         对于FREE和CHARGE小车，目标为PORT的小车，目标点为Goal
        ///         对于PICKUP小车，目标点为SHELF中最靠外的货物
        ///         对于WORK小车，目标点为SHELF中最靠内的空闲点
        /// @param  curNodeId        当前小车位置
        /// @param  isAstar          当前是否为规划层
        /// 
        /// @TODO   在取货时，可能isArriveGoal==true时，与当前任务点不一致，需要调整当前小车执行的任务
        /// 
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// 
        public bool isArriveGoal(int curNodeId, bool isAstar = false, bool isKey = true)
        {
            if (CurrentGoal == -1) return false;
            var curNode = Graph.nodeDict[curNodeId];
            var goalNode = Graph.nodeDict[CurrentGoal];

            if (isKey == false) return curNodeId == CurrentGoal;

            if (curNode.layout != goalNode.layout && isAstar == true) return curNode.isHoister; // 换层

            if (isManual == true) return curNodeId == CurrentGoal;
            if (state == carState.FREE || state == carState.CHARGE) return curNodeId == CurrentGoal;

            if (Graph.nodeId2SectorId.ContainsKey(CurrentGoal) == false) return curNodeId == CurrentGoal;

            if (curNode.sectorId != goalNode.sectorId) return false;
            var sector = Graph.sectorDict[goalNode.sectorId];

            var validPointList = sector.generateValidTarget(state);
            if (validPointList.Contains(curNodeId) == true) return true;
            return false;
        }


        public void updateDeadClock(bool setFlag)
        {
            if (setFlag == false)
            {
                deadClock++;
            }
            else deadClock = 0;
        }

        public int DeadInducementAgentId;

        public bool isTraversed;

        public Dictionary<int, int> expelInfo;

        public bool isAvoid;
        public int avoidFor;
        public bool isBeenAvoid;
        public int beenAvoidFor;

        public int hideNodeId;

        public void SetExpel(int expel_node_id, int block_robot_id)
        {
            expelInfo[expel_node_id] = block_robot_id;
        }

        public void CancelExpel(int expel_node_id)
        {
            expelInfo.Remove(expel_node_id);
        }

        public void clearTraffic()
        {
            var car = GetCar as SimulatorCarFourWay;
            car.ClearTraffic();
        }

        public void SetAvoid(int hide_node_id, List<int> hide_path = null)
        {
            isAvoid = true;
            setAvoidTime = DateTime.Now;
            hideNodeId = hide_node_id;
            if (hide_path != null)
            {
                if (state == carState.FREE && isManual == false) initGoalList();
                // clearTraffic();
            }
            GetCar.color = "#8FAADC";
            addGoalinFront(hideNodeId);
        }

        public void CancelAvoid()
        {
            isAvoid = false;
            GetCar.color = "orange";
            hideNodeId = -1;
            if (isCurrentGoalKey == false)
                removeCurrentGoal();
            finishAvoidTime = DateTime.Now;
        }

        public void solveExceptionTask()
        {
            if (isManual == true) return;
            if (State != carState.PICKUP && State != carState.WORK) return;

            if (TaskManager.TaskList.ContainsKey(TaskId) == false)
            {
                TaskManager.Release(TaskId, Id);
                FREE();
            }
            else
            {
                var task = TaskManager.getTask(TaskId);
                if (task.isValidTarget() == false)
                {
                    if (State == carState.PICKUP)
                    {
                        TaskManager.Release(TaskId, Id);
                        FREE();
                    }
                    else if (State == carState.WORK)
                    {
                        TaskManager.Delete(TaskId);
                        GetCar.tags.Remove("status");
                        FREE();
                    }
                }
            }
        }
    }

    public class AgvAgentManager
    {
        public Dictionary<int, Agent> agentList;

        public SectorGraph Graph
        {
            get { return DispatchMisson.Graph; }
        }

        public AgvAgentManager()
        {
            agentList = new Dictionary<int, Agent>();
            UpdateAgentList();
        }

        public List<Agent> getAllAgents()
        {
            return agentList.Values.ToList();
        }

        public List<int> getAllAgentsId()
        {
            return agentList.Keys.ToList();
        }

        public Agent getAgent(int agentId)
        {
            if (agentList.ContainsKey(agentId) == false) return null;

            return agentList[agentId];
        }

        public void LogAgentInfo()
        {
            Diagnosis.Post("Agent num: " + getAllAgents().Count);
            foreach (var agent in getAllAgents())
            {
                // if (agent.State == carState.FREE) continue;
                var first = agent.haveGoals() ? agent.CurrentGoal : -1;
                Diagnosis.Post("Agent Id: " + agent.Id + ", State: " + agent.State + ", TaskID: " + agent.TaskId + ", SiteID: " + agent.SiteID + ", Goals[0]: " +
                    first
                    + ", target: " + agent.TaskId + ", Path length:" + agent.Path.Count );
                string result = string.Join(", ", agent.Goals.Select(item => $"({item.Item1}, {item.Item2})"));
                result = "Avoid: " + agent.isAvoid + ", Goals: " + result;
                Diagnosis.Post(result);
            }
        }

        public void LogPathInfo()
        {
            foreach (var agent in getAllAgents())
            {
                if (agent.Path.Count == 0) continue;
                var path = agent.Path;
                string result = string.Join(", ", path);
                Diagnosis.Post("Agent Id: " + agent.Id + ", Path: " + result);
            }
        }

        public void LogKey()
        {
            var pickup = getAllAgents().Where(agent => agent.State == carState.PICKUP).ToList().Count;
            var work = getAllAgents().Where(agent => agent.State == carState.WORK).ToList().Count;
            Diagnosis.Post("Agent - PICKUP: " + pickup + "; WORK: " + work);
        }

        public List<int> getFreeAgent()
        {
            return getAllAgents()
                .Where(agent => (agent.State == carState.FREE && agent.isManual == false))
                .Select(agent => agent.Id)
                .ToList();
        }
        public List<int> getFreeAndPickupAgent()
        {
            return getAllAgents()
                .Where(agent => (agent.State == carState.FREE || agent.State == carState.PICKUP))
                .Select(agent => agent.Id)
                .ToList();
        }
        
        public List<int> getAllocationAgent()
        {
            var freeAgents = getAllAgents()
                .Where(agent => (agent.State == carState.FREE && agent.isManual == false))
                .Select(agent => agent.Id)
                .ToList();
            
            // 预选车补充
            foreach (var agent in getAllAgents())
            {
                if (agent.State != carState.WORK || agent.isManual == true) continue;
                Node siteId = Graph.nodeDict[agent.SiteID];
                Node target = Graph.nodeDict[agent.CurrentKeyGoal];
                if (Manhattan(Graph, siteId, target) < TaskManager.CohesionDistanceThreshold)
                {
                    freeAgents.Add(agent.Id);
                }
            }
            return freeAgents;
        }

        public void updateState()
        {
            foreach (var agent in getAllAgents())
            {
                agent.UpdateState();
                agent.UpdateLayout();
            }

            foreach (var agent in getAllAgents())
            {
                if (TaskManager.TaskList.ContainsKey(agent.TaskId) == false && agent.isManual == false && agent.State != carState.CHARGE)
                    agent.FREE();
            }
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void UpdateAgentList()
        ///
        /// @brief  每一帧实时更新场上机器人信息，并添加到Agent表中
        /// @TODO   不存在撤销agent的功能
        ///
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void UpdateAgentList()
        {
            foreach (var acar in SimpleLib.GetAllCars())
            {
                if (!agentList.ContainsKey(acar.id))
                {
                    Agent agent = new Agent(acar.id);
                    agentList.Add(acar.id, agent);
                }
            }
            foreach (var agent in agentList.Values)
            {
                agent.iscurGoalChange = false;
                if (agent.IsOnline == false && agent.isManual == false)
                {
                    // 下线小车
                    agent.offline();
                }
                if (agent.IsOnline == true && agent.isManual == true)
                {
                    // 需要上线小车，此时如果小车为故障状态，或身上背着货物且不存在放货的Task，则不允许上线
                    if (agent.IsFault == true || (agent.TaskId == "-1" && agent.have_goods))
                        agent.GetCar.lstatus = "下线" ;
                    else agent.online();
                }

                agent.solveExceptionTask();

            }
        }


        public carState GetAgentState(int robotId)
        {
            return agentList[robotId].State;
        }
    }
}
