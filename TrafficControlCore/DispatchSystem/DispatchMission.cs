/************************************************************************/
/* File:	  DispatchMission.cs										*/
/* Func:	  调度线程入口，包含所有Manager；                             */
/* Author:	  Tan Yuhong                                                */
/************************************************************************/
using SimpleComposer.RCS;
using SimpleCore.Library;
using SimpleCore.PropType;
using SimpleCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using TrafficControlCore.TaskSystem;
using TrafficControlCore.DispatchSystem;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Forms.VisualStyles;
using TrafficControlCore.DispatchSystem.Graph;
using TrafficControlCore.AvoidSystem;
using System.Collections;
using System.Windows.Forms;
using System.IO.Pipes;
using static TrafficControlCore.TaskSystem.AgvTask;
using SimpleComposer;
using static TrafficControlCore.DispatchSystem.Graph.Sector;
using TrafficControlCore.AgentSystem;
using TrafficControlCore.UI;
using TrafficControlCore.PreMoveSystem;
using TrafficControlCore.DispatchSystem.Pathplan;

namespace TrafficControlCore.DispatchSystem
{
    [MissionType("规划调度线程", editor = typeof(DispatchMisson))]

    public class DispatchMisson : Mission
    {
        public class DispatchMissionStatus : MissionStatus
        {
            public int Enqueued;
            public int Performed = 0;
            public int Running = 0;
            public double Jph = 0;
            public string StuckInfo = "/";
            public bool Simulating;
            public int StuckN;
        }
        public static SectorGraph Graph { get; set; }
        public static AgvTaskManager TaskManager { get; set; } = new AgvTaskManager();                          // 任务管理系统
        public static AgvChargeManager ChargeManager { get; set; } = new AgvChargeManager();                    // 充电管理系统
        public static AgvAgentManager AgentManager { get; set; } = new AgvAgentManager();                       // Agent管理系统
        public static AgvExceptionManager ExceptionManager { get; set; } = new AgvExceptionManager();           // 异常管理系统
        public static AgvAvoidManager AvoidManager { get; set; } = new AgvAvoidManager();
        public static LockAndReservation LockAndReservationManager { get; set; } = new LockAndReservation();    // 锁格预约管理系统
        public static ActionExecutor ActionManager { get; set; } = new ActionExecutor();                        // Agent执行系统
        public static PreMove PreMoveManager { get; set; } = new PreMove();                                     // 预调度管理系统
        public static AgvPathPlan PathPlanManager { get; set; } = new AgvPathPlan();                            // 路径规划管理系统
        public override MissionStatus status { get; set; } = new DispatchMissionStatus();

        public static int DurTime
        {
            get {
                return (int)((DateTime.Now - _initTime).TotalSeconds);
            }
        }
        private static DateTime _initTime;

        // 构造函数
        public DispatchMisson()
        {
            _initTime = DateTime.Now;
            Execute();
        }
        // 重写Create方法，使其返回DispatchMission对象
        public static Mission Create()
        {
            return new DispatchMisson();
        }
        // 重写Execute方法，使其每隔一秒执行一次
        public override void Execute()
        {
            status.status = "已启动";
            var stat = (DispatchMissionStatus)status;
            new Thread(() =>
            {
                Graph = new SectorGraph();
                ChargeManager.initList(Graph);
                TaskManager.InitCrossCode();
                tmpStorageHelper.init();

                // 将仿真充电电量上升调整为0.2
                foreach(var car in SimpleLib.GetAllCars())
                {
                    var acar = car as DummyCar;
                    acar.battery_increase = 1;
                    acar.battery_decrease = 0.5;
                    acar.speed = 800;
                    //acar.lstatus = "下线";
                }
                
                while (true)
                {
                    DispatchRun();
                    Thread.Sleep(200);
                }
            }).Start();
        }
        public bool DispatchRun()
        {
            DateTime curTime = DateTime.Now;
            double currentTime = (curTime - _initTime).TotalSeconds;
            
            Diagnosis.Post("###################### Update Status Module ######################"); 
            UpdateStatus();
            Diagnosis.Post("##################### Task Allocation Module #####################");
            TaskAllocation();
            tmpStorageHelper.tmpStorageChecker();
            Diagnosis.Post("###################### PreDispatch and Charge Module #######################");
            PreDispatchAndCharge();
            Diagnosis.Post("###################### Path Planning and Cmd Exe Module ######################");
            PathPlanAndFormatOutput();
            Diagnosis.Post("###################### Solve DeadBlock and Safety Module #######################");
            SolveDeadBlockAndSafety();
            
            // Graph.printSectorLockState();
            Graph.checkReservation();
            // ChargeManager.checkBattery();
            return true;
        }
        private void UpdateStatus() 
        {
            // TaskManager.LogDetail();
            TaskManager.LogKey();
            AgentManager.LogAgentInfo();
            // AgentManager.LogPathInfo();
            AgentManager.LogKey();
            AvoidManager.LogDetail();

            AgentManager.UpdateAgentList();
            TaskManager.UpdateTaskList();
            AvoidManager.UpdateAvoidTable();

            // 更新agent状态
            ChargeManager.gotoFree();
            AgentManager.updateState();
            ChargeManager.gotoCharge();

            Graph.Init();
            TaskManager.InitExecutingInfo();
            TaskManager.InitCrossInfo();
            LockAndReservationManager.init();
            tmpStorageHelper.storageResReset();
        }
        private void TaskAllocation()
        {
            allocationHelper.TaskAllocation();
        }
        private void PreDispatchAndCharge()
        {
            PreMoveManager.PreDispatchAndCharge();
        }
        
        private List<Agent> generateReplanAgents()
        {
            if ((int)(DateTime.Now - _initTime).TotalSeconds % 60 == 0)
            {
                // Diagnosis.Post(" replan for ALL! ");
                return AgentManager.getAllAgents();
            }

            var needPlanList = new List<Agent>();
            foreach (var agent in AgentManager.getAllAgents())
            {
                if (agent.IsFault) continue;
                // if (agent.haveGoals() == false) continue;
                if (SectorAstar.needPathPlan(Graph, agent) == true)
                {
                    needPlanList.Add(agent); 
                    // Diagnosis.Post("Agent Id: " + agent.Id + " Do need replan! ");
                }
            }
            return needPlanList;
        }
        
        private void PathPlanAndFormatOutput()
        {
            var agents = generateReplanAgents();
            do
            {
                PathPlan(agents);
                foreach (var agent in AgentManager.getAllAgents())
                    agent.iscurGoalChange = false;
                FormateOutput();
                agents = generateReplanAgents();

            } while (agents.Count > 0);
            /*
            PathPlanAndFormatOutput_ForSpecialStatus(carState.WORK);
            PathPlanAndFormatOutput_ForSpecialStatus(carState.PICKUP);
            PathPlanAndFormatOutput_ForSpecialStatus(carState.FREE);
            PathPlanAndFormatOutput_ForSpecialStatus(carState.CHARGE);
            PathPlanAndFormatOutput_ForSpecialStatus(carState.OTHER);
            */
        }
        private void PathPlan(List<Agent> agents)
        {
            foreach (var agent in agents)
            {
                PathPlanManager.PathPlan(agent);
            }
        }
        private void FormateOutput()
        {
            var agents = AgentManager.getAllAgents();
            var sortedAgents = agents.OrderBy(a => Graph.nodeDict[a.SiteID].type != Node.NODE_TYPE.FAST)
                   .ThenByDescending(a => a.State == carState.WORK)
                   .ThenByDescending(a => a.Id) // 0 means highway, 1 means shelf
                                                // .ThenBy(a => Math.Abs(Graph.nodeDict[a.SiteID].x - SectorGraph.DELIVER_LINE_X / 2))
                   .ToList();
            /*var sortedAgents = agents.OrderBy(a => a.isAvoid == false)
               .ThenByDescending(a => a.State == carState.WORK)
               .ThenByDescending(a => Graph.nodeDict[a.SiteID].type == Node.NODE_TYPE.FAST) // 0 means highway, 1 means shelf
               .ThenBy(a => Math.Abs(Graph.nodeDict[a.SiteID].x - SectorGraph.DELIVER_LINE_X / 2))
               .ToList();*/
            if (Graph.ISLARGEGRAPH == false)
            {
                foreach (var agent in sortedAgents)// AgentManager.getAllAgents())
                {
                    Diagnosis.Post("agentId: " + agent.Id + " isAvoid: " + agent.isAvoid + " status: " + agent.State + " type: "
                        + (Graph.nodeDict[agent.SiteID].type == Node.NODE_TYPE.FAST) + " x: "
                        + Math.Abs(Graph.nodeDict[agent.SiteID].x - Graph.DELIVER_LINE_X / 2));
                }
            }
            foreach (var agent in sortedAgents)// AgentManager.getAllAgents())
            {
                // Diagnosis.Post("isAvoid: " + agent.isAvoid + " status: " + agent.State + " ")
                if (agent.IsFault == true) continue;
                // if (agent.isTraversed) continue;
                ActionManager.FormatOutput(agent);
            }
        }
        /*
        private void PathPlanAndFormatOutput_ForSpecialStatus(carState carStatus)
        {
            if (carStatus != carState.OTHER){
                foreach (var agent in AgentManager.getAllAgents())
                {
                    if (agent.isTraversed) continue;
                    if (agent.State != carStatus || agent.isAvoid == true) continue;
                    PathPlanManager.PathPlan(agent);
                    ActionManager.FormatOutput(agent);
                    agent.isTraversed = true;
                }
            }
            else
            {
                foreach (var agent in AgentManager.getAllAgents())
                {
                    if (agent.isTraversed) continue;
                    if (agent.isAvoid == false) continue;
                    PathPlanManager.PathPlan(agent);
                    ActionManager.FormatOutput(agent);
                    agent.isTraversed = true;
                }
            }
        }
        */
        private void SolveDeadBlockAndSafety()
        {
            // Diagnosis.Post(" there is no SolveDeadBlockModule! ");
            ExceptionManager.solveBlockAgent();
            ExceptionManager.makeSureSafety();
            ExceptionManager.HighwayOfOneDirection();
        }
    }
}
