/************************************************************************/
/* File:	  AgvTask.cs										        */
/* Func:	  任务管理系统                                               */
/* Author:	  Tan Yuhong                                                */
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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using static TrafficControlCore.Utils.Geometry;
using System.Xml;
using System.Security.Policy;

namespace TrafficControlCore.TaskSystem
{
    public enum TaskType
    {
        Shift = 0,
        InFlow = 1,
        OutFlow = 2,
        Default = 3, //  缺省值，用于保证sector内部仅有一种类型的任务。
        Manual = 4   //  手动指定单次移动任务，移动后可能会附带动作 (Fetch, Put, None).
    }
    public enum TaskStatus
    {
        Ready = 0,
        Executing = 1,
        Finished = 2,
        Error = 3
    }
    public enum FinalAction
    {
        Fetch = 1,
        Put = 2,
        None = 3
    }
    public enum ExecutingStatus
    {
        BeforeFetch = 0, 
        AfterFetch = 1
    }

    public class AgvTask
    {
        public const string M_NODE_NAME = "AGV任务";//node节点名称
        public static int Num { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }
        public int Priority { get; set; }
        public string Error { get; set; } = string.Empty;
        public ExecutingStatus executingStatus = ExecutingStatus.BeforeFetch; // 1- 初始状态， 每到一个关键点加1
        public TaskStatus taskStatus = TaskStatus.Ready;
        public TaskType taskType = TaskType.Default;
        public int Car { get; set; } = -1;
        public List<(int siteID, bool isKeySite)> sites = new();
        public FinalAction finalAction { get; set; } = FinalAction.None;
        public string realTaskId = "";  // 对于虚拟任务，需要明确原任务的ID
        public bool isNeedAllocation = true;

        public Car UsingCar;
        // public List<int> carList = new();

        public int stuckedTime = 0;
        public string stuckedReason = "";

        public const int MAX_ALLOCATION_TASK_NUM_PER = 110;

        public DateTime sendTime;
        public DateTime startTime;
        public DateTime finishTime;
        public TimeSpan workTime;
        public TimeSpan waitTime;

        private static object obj = new();
        public AgvTask(List<(int, bool)> lm, string name = "AGV任务")
        {
            lock (obj)
                Id = (Num++).ToString();
            sites = lm;
            Name = name;
            sendTime = DateTime.Now;
        }
        public AgvTask(AgvTask task, string taskNo = " ")
        {
            lock (obj)
                if (taskNo == " ")
                    Id = (Num++).ToString();
                else
                    Id = taskNo;
            sites = task.sites;
            Name = task.Name;
            Car = task.Car;
            Priority = task.Priority;
            // carList = task.carList;
            sendTime = task.sendTime;
        }

        public static List<T> List_Clone<T>(object list)
        {
            using Stream objectStream = new MemoryStream();
            IFormatter formatter = new BinaryFormatter();
            formatter.Serialize(objectStream, list);
            objectStream.Seek(0, SeekOrigin.Begin);
            return formatter.Deserialize(objectStream) as List<T>;
        }

        public int getCargoSite()
        {
            return sites[0].siteID;
        }

        public int getPort()
        {
            if (taskType == TaskType.OutFlow) return getDestination();
            else if (taskType == TaskType.InFlow) return getCargoSite();
            else if (taskType == TaskType.Shift) throw new InvalidOperationException("Shift Task DONOT have Port");
            else throw new InvalidOperationException($"Task Type: {taskType}");
        }
        public int getShelf(int ShelfId = 1)
        {
            if (taskType == TaskType.OutFlow) return getCargoSite();
            else if (taskType == TaskType.InFlow) return getDestination();
            else if (taskType == TaskType.Shift)
            {
                if (ShelfId == 0) return getCargoSite();
                else if (ShelfId == 1) return getDestination();
                else throw new InvalidOperationException($"GetShelf, Wrong ShelfId: {ShelfId}");
            }
            else throw new InvalidOperationException($"Task Type: {taskType}");
        }
        public int getDestination()
        {
            return sites[1].siteID;
        }
        public bool isValidTarget()
        {
            if (taskStatus == TaskStatus.Finished) return true;
            if (taskStatus == TaskStatus.Executing)
            {
                var carGoNode = Graph.nodeDict[getDestination()];
                if (carGoNode.isPortAndType != 0) return true;

                var sector = Graph.sectorDict[carGoNode.sectorId];
                if (sector.isCharged == false && sector.getCargoIndexList().Count >= sector.NodeList.Count) return false;
                if (sector.isCharged == true && sector.getCargoIndexList().Count + 1 >= sector.NodeList.Count) return false;
                return true;
            }
            if (taskStatus == TaskStatus.Ready)
            {
                return allocationHelper.haveValidTarget(Id);
            }
            else return false;
        }
    }

    public class AgvTaskManager
    {
        public Dictionary<string, AgvTask> TaskList { get; set; }  // Ready & Executing

        public List<AgvTask> NewTasks { get; set; }  // avoid TaskList being a shared var in two thread 
        public Dictionary<string, AgvTask> FinishTaskList { get; set; }
        public Dictionary<string, AgvTask> ErrorTaskList { get; set; }
        public int FinishedNum {set; get;}
        public int OUTPORT_CAPACITY = 25;
        public int INPORT_CAPACITY = 25;
        public int MAX_CROSS_CAPACITY = 25;
        public Dictionary<int, int> CrossPortflow { get; set; }  // 关键交叉口的流量
        public Dictionary<int, int> CrossPortCapacity { get; set; }  // 关键交叉口的剩余容量
        public List<int> CrossCodeList { get; set; }

        public int DynamicDistanceThreshold = 30000;
        public int CohesionDistanceThreshold = 15000;


        public SectorGraph Graph
        {
            get { return DispatchMisson.Graph; }
        }

        public AgvTaskManager()
        {
            TaskList = new Dictionary<string, AgvTask>();
            NewTasks = new List<AgvTask>();
            FinishTaskList = new Dictionary<string, AgvTask>();
            ErrorTaskList = new Dictionary<string, AgvTask>();
            FinishedNum = 0;
            CrossPortflow = new Dictionary<int, int>();
            CrossPortCapacity = new Dictionary<int, int>();
            CrossCodeList = new List<int>();
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void UpdateTaskList()
        ///
        /// @brief  通过NewTasks更新系统TaskList
        ///
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public void UpdateTaskList()
        {
            int newNum = NewTasks.Count;
            int addNum = 0;
            for (int i = 0; i < newNum; i++)
            {
                var task = NewTasks[i];
                if (task == null) continue;  // 不知道为什么
                TaskList.Add(task.Id, task);
                addNum++;
                Diagnosis.Post("Add a new task, TaskType: " + task.taskType + ",TaskId: " + task.Id);//  + ",pickupPoint: " + task.getCargoSite() + ",target: " + task.getDestination());
            }

            for (int i = 0; i < newNum; i++)
            {
                NewTasks.RemoveAt(0);
            }

            Diagnosis.Post("Input Task: " + addNum + " TaskList Num: " + TaskList.Count);

            foreach (var node in Graph.nodeDict.Values)
            {
                node.taskIds.Clear();
            }
            // 任务与节点进行绑定
            foreach (var task in TaskList.Values)
            {
                if (task.taskStatus != TaskStatus.Ready) continue;
                if (task.taskType == TaskType.Manual) continue;
                var node = Graph.nodeDict[task.sites[0].siteID];
                node.taskIds.Add(task.Id);
            }

            var errorTaskIds = new List<string>();
            // 删除异常任务
            foreach (var task in TaskList.Values)
            {
                if (task.taskStatus != TaskStatus.Ready) continue;
                if (task.isValidTarget() == false && (DateTime.Now - task.sendTime).TotalMinutes > 60)
                    errorTaskIds.Add(task.Id);
            }
            foreach (var errorTaskId in errorTaskIds)
                TaskList.Remove(errorTaskId);
        }

        public void InitExecutingInfo()
        {
            foreach (var sector in Graph.sectorDict.Values)
            {
                sector.executingTaskNum = 0;
                sector.executingTaskType = Sector.ExecutingTask_Type.DEFAULT;
            }
            var agentList = AgentManager.agentList.Values;
            foreach (var agent in agentList)
            {
                if (agent.TaskId == "-1" || TaskManager.TaskList.ContainsKey(agent.TaskId) == false)
                    continue;
                var task = getTask(agent.TaskId, agent.Id);
                updateExecutingTask4Sector(task, 1);
            }
        }
        public void InitCrossInfo()
        {
            foreach (var code in CrossCodeList)
            {
                CrossPortCapacity[code] = MAX_CROSS_CAPACITY;
                CrossPortflow[code] = 0;
            }
            var agentList = AgentManager.agentList.Values;
            foreach (var agent in agentList)
            {
                if (agent.TaskId == "-1" || TaskManager.TaskList.ContainsKey(agent.TaskId) == false) 
                    continue;
                var task = getTask(agent.TaskId);    
                if (task.taskType != TaskType.Shift && task.taskType != TaskType.Manual)  // 移库任务不存在Cross
                {
                    var portId = task.getPort();
                    if (getCrossNodeId(portId) != -1)
                    {
                        CrossPortflow[getCrossNodeId(portId)]++;
                        CrossPortCapacity[getCrossNodeId(portId)]--;
                    }
                }
            }
        }
        public void NewAgvTask(AgvTask task)
        {
            NewTasks.Add(task);
        }

        public List<AgvTask> getAllTodoTasks()
        {
            return TaskList.Values.ToList();
        }

        public List<string> getAllTodoTasksId()
        {
            return TaskList.Keys.ToList();
        }

        public void LogDetail()
        {
            LogTaskInfo();
        } 

        public void LogKey()
        {
            LogTaskNumInfo();
            LogPortInfo();
        }
        public void LogPortInfo()
        {
            var str = "CrossPortFlow, (Code, Flow): ";
            foreach (var code in CrossPortflow.Keys)
            {
                str += "( " + code + ", " + CrossPortflow[code] + "), ";
            }
            Diagnosis.Post(str);

        }
        public void LogTaskNumInfo()
        {
            var tasks = getAllTodoTasks();
            int total = tasks.Count;
            int ready = tasks.Where(task => task.taskStatus == TaskStatus.Ready).ToList().Count;
            int execte = total - ready;
            Diagnosis.Post("All Task " + total + " = " + 
            ready + " + " + execte + ", Finished: " + FinishedNum);
        }

        public void LogTaskInfo() 
        {
            // num
            int total = getAllTodoTasksId().Count;
            int ready = 0;
            int execute = 0;
            
            // log
            string readyInfo = "] Ready Tasks: ";
            string executeInfo = "] Executing Tasks: ";
            string finishInfo = "] Finshed Tasks";

            foreach (var task in getAllTodoTasks())
            {
                if (task.taskStatus == TaskStatus.Ready) 
                { 
                    ready++; 
                    readyInfo += task.Id + ", ";
                }
                if (task.taskStatus == TaskStatus.Executing) 
                { 
                    execute++; 
                    executeInfo += task.Id + ", ";
                }
            }
            Diagnosis.Post("Task num: " + total + ", Ready: " + ready + ", Execute: " + execute);
            Diagnosis.Post("[" + ready + readyInfo);
            Diagnosis.Post("[" + execute + executeInfo);
            Diagnosis.Post("[" + FinishedNum + finishInfo);
        }
        public TaskStatus getTaskStatus(string taskId)
        {
            return TaskList[taskId].taskStatus;
        }

        public AgvTask getTask(string taskId, int agentId = -1) 
        {
            if(TaskList.ContainsKey(taskId) == false)
            {
                throw new Exception("invalid taskId: " + taskId + " agentId: " + agentId);
            }
            return TaskList[taskId];
        }
        public void Release(string taskId, int agentId)
        {
            if (taskId == "-1" || TaskManager.TaskList.ContainsKey(taskId) == false) return; 
            var task = getTask(taskId);

            if (task.taskStatus != TaskStatus.Ready)
                return;

            task.executingStatus = ExecutingStatus.BeforeFetch;
            if (task.taskType != TaskType.Shift)  // 移库任务不存在Cross
            {
                var portId = task.getPort();
                if (getCrossNodeId(portId) != -1)
                {
                    CrossPortflow[getCrossNodeId(portId)]--;
                    CrossPortCapacity[getCrossNodeId(portId)]++;
                }
            }
            updateExecutingTask4Sector(task, 0);
        }

        public void Pickup(string taskId, int agentId)
        {
            var task = getTask(taskId);

            task.executingStatus = ExecutingStatus.AfterFetch;

            if (task.taskType != TaskType.Shift)  // 移库任务不存在Cross
            {
                var portId = task.getPort();
                if (getCrossNodeId(portId) != -1)
                {
                    CrossPortflow[getCrossNodeId(portId)]++;
                    CrossPortCapacity[getCrossNodeId(portId)]--;
                }
            }
            updateExecutingTask4Sector(task, 1);
        }

        public void Execute(string taskId, int agentId, Node node)
        {
            var task = getTask(taskId);
            task.taskStatus = TaskStatus.Executing;
            task.Car = agentId;

            node.taskIds.Remove(taskId);
            // node.taskIds.RemoveAt(0);
        }

        public void Finish(string taskId, int SiteID = -1)
        {
            var task = getTask(taskId);
            TaskList.Remove(taskId);
            task.taskStatus = TaskStatus.Finished;
            if (task.taskType != TaskType.Shift 
                && task.taskType != TaskType.Manual)  // 移库任务不存在Cross
            {
                var portId = task.getPort();
                if (getCrossNodeId(portId) != -1)
                {
                    CrossPortflow[getCrossNodeId(portId)]--;
                    CrossPortCapacity[getCrossNodeId(portId)]++;
                }
            }
            FinishTaskList.Add(taskId, task);
            FinishedNum++;

            updateExecutingTask4Sector(task, 0);
            
            if (task.realTaskId != "")
            {
                var oldTask = getTask(task.realTaskId);// LaterTaskList[task.laterTaskId];
                oldTask.sites[0] = (SiteID, true);//  task.getDestination();
                oldTask.isNeedAllocation = true;
                oldTask.taskStatus= TaskStatus.Ready;
                oldTask.executingStatus = ExecutingStatus.BeforeFetch;
                // TaskList.Add(laterTask.Id, laterTask);
                // LaterTaskList.Remove(laterTask.Id);
            }
            
        }

        public void Stop(string taskId, int SiteID = -1)    // 专门为暂存区设计的Stop 此时小车进行虚拟任务
        {
            var task = getTask(taskId);
            if (task.taskType != TaskType.Shift
                && task.taskType != TaskType.Manual)  // 移库任务不存在Cross
            {
                var portId = task.getPort();
                if (getCrossNodeId(portId) != -1)
                {
                    CrossPortflow[getCrossNodeId(portId)]--;
                    CrossPortCapacity[getCrossNodeId(portId)]++;
                }
            }
            updateExecutingTask4Sector(task, 0);
            task.isNeedAllocation = false;
            task.Car = -1;
            task.executingStatus = ExecutingStatus.BeforeFetch;
            task.taskStatus = TaskStatus.Ready;
        }

        public void Delete(string taskId)
        {
            var task = getTask(taskId);
            TaskList.Remove(taskId);
            if (task.taskType != TaskType.Shift
                && task.taskType != TaskType.Manual)  // 移库任务不存在Cross
            {
                var portId = task.getPort();
                if (getCrossNodeId(portId) != -1)
                {
                    CrossPortflow[getCrossNodeId(portId)]--;
                    CrossPortCapacity[getCrossNodeId(portId)]++;
                }
            }
            updateExecutingTask4Sector(task, 0);
        }

        public void RollBack(string taskId, int cargoSite)
        {
            var task = getTask(taskId);
            task.executingStatus = ExecutingStatus.BeforeFetch;
            task.taskStatus = TaskStatus.Ready;
            task.Car = -1;
            if (task.taskType != TaskType.Shift
                && task.taskType != TaskType.Manual)  // 移库任务不存在Cross
            {
                var portId = task.getPort();
                if (getCrossNodeId(portId) != -1)
                {
                    CrossPortflow[getCrossNodeId(portId)]--;
                    CrossPortCapacity[getCrossNodeId(portId)]++;
                }
            }
            updateExecutingTask4Sector(task, 0);
            task.sites[0] = (cargoSite, true);

            if (task.taskType == TaskType.InFlow) 
                task.taskType = TaskType.Shift;
        }

        public List<int> getSites(string taskId)
        {
            List<int> siteList = new List<int>();
            var task = TaskList[taskId];
            siteList.Add(task.sites[0].siteID);
            siteList.Add(task.sites[1].siteID);
            return siteList;
        }
        public List<AgvTask> getReadyTasks(TaskType taskType)
        {
            return getAllTodoTasks()
                .Where(task => task.taskStatus == TaskStatus.Ready && task.taskType == taskType)
                .OrderBy(task => task.sendTime)
                .ToList();
        }
        
        public List<AgvTask> getAllocationTasks(TaskType taskType)
        {
            return getAllTodoTasks()
                .Where(task => task.taskStatus == TaskStatus.Ready && task.taskType == taskType && task.executingStatus == ExecutingStatus.BeforeFetch && task.isNeedAllocation == true)
                .OrderBy(task => task.sendTime)
                .ToList();
        }
        public List<AgvTask> getAllocationTasksWithPort()
        {
            return getAllTodoTasks()
                .Where(task => task.taskStatus == TaskStatus.Ready 
                && (task.taskType == TaskType.InFlow || task.taskType == TaskType.OutFlow) 
                && task.executingStatus == ExecutingStatus.BeforeFetch
                && task.isNeedAllocation == true)
                .OrderBy(task => task.sendTime)
                .ToList();

        }
        public int getCrossNodeId(int portNodeId)
        {
            var node = Graph.nodeDict[portNodeId];
            var code = node.crossId;
            return code;
        }
        
        public List<String> getTaskListForAllocation() // 得到现在还没有处理的Task
        {
            List<String> readyTasks = new List<String>();
            var crossflowTasks = getAllocationTasksWithPort();
            foreach (var cross in CrossPortCapacity)
            {
                var readyTasks_ = crossflowTasks.Where(task => getCrossNodeId(task.getPort()) == cross.Key).ToList();
                for (int i = 0; i < cross.Value && i < readyTasks_.Count; i++)
                {
                    var task = readyTasks_[i];
                    if (readyTasks.Count >= MAX_ALLOCATION_TASK_NUM_PER) break;
                    readyTasks.Add(task.Id);
                }
            }
            var shiftTasks = getAllocationTasks(TaskType.Shift);
            foreach (var task in shiftTasks)
            {
                if (readyTasks.Count >= MAX_ALLOCATION_TASK_NUM_PER) break;
                readyTasks.Add(task.Id);
            }
            return readyTasks;
        }
        
        public void InitCrossCode()
        {
            if (Graph.ISLARGEGRAPH == true) MAX_CROSS_CAPACITY = 25;
            else MAX_CROSS_CAPACITY = 8;
            foreach (var node in Graph.nodeDict.Values)
            {
                var code = node.crossId;
                if (CrossPortflow.ContainsKey(code) == false)
                {
                    CrossPortflow.Add(code, 0);
                    CrossPortCapacity.Add(code, MAX_CROSS_CAPACITY);
                    CrossCodeList.Add(code);
                }
            }
        }
        public String getEarlyTaskId(Node node)
        {
            var TaskIds = node.taskIds;
            if(TaskIds.Count == 0)
            {
                return "-1";
                // throw new Exception("TaskIds is Empty!, NodeID: " + node.id);
            }
            foreach (var item in TaskIds.Where(x => TaskManager.TaskList.ContainsKey(x) == false).ToList())
            {
                TaskIds.Remove(item);
            }
            var taskId = TaskIds[0];
            foreach (var otherTaskId in TaskIds)
            {
                if (TaskManager.TaskList.ContainsKey(otherTaskId) == false) continue;
                if (getTask(taskId).sendTime > getTask(otherTaskId).sendTime)
                    taskId = otherTaskId;
            }
            return taskId;
        }

        public void updateExecutingTask4Sector(AgvTask task, int IsAdd = 1)
        {
            if (task.taskType == TaskType.Manual) return;

            var shelfId = task.getShelf(1);
            var shelfSectorId = Graph.nodeId2SectorId[shelfId];
            // Diagnosis.Post(" taskID: " + task.Id + "sectorID: " + shelfSectorId);
            var shelfSector = Graph.sectorDict[shelfSectorId];
            if (task.taskType == TaskType.InFlow)
            {
                if(IsAdd == 1) shelfSector.addExecutingTask(Sector.ExecutingTask_Type.IN);
                else shelfSector.delExecutingTask(Sector.ExecutingTask_Type.IN);
            }
            else if(task.taskType == TaskType.OutFlow)
            {
                if (IsAdd == 1) shelfSector.addExecutingTask(Sector.ExecutingTask_Type.OUT);
                else shelfSector.delExecutingTask(Sector.ExecutingTask_Type.OUT);
            }
            else if(task.taskType == TaskType.Shift)
            {
                if (IsAdd == 1) shelfSector.addExecutingTask(Sector.ExecutingTask_Type.IN);
                else shelfSector.delExecutingTask(Sector.ExecutingTask_Type.IN);

                shelfId = task.getShelf(0);
                shelfSectorId = Graph.nodeId2SectorId[shelfId];
                shelfSector = Graph.sectorDict[shelfSectorId];

                if (IsAdd == 1) shelfSector.addExecutingTask(Sector.ExecutingTask_Type.OUT);
                else shelfSector.delExecutingTask(Sector.ExecutingTask_Type.OUT);

            }
        }
    }
}
