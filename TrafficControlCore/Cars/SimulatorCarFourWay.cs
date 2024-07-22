using SimpleComposer;
using SimpleComposer.RCS;
using SimpleCore;
using SimpleCore.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrafficControlCore.DispatchSystem;
using TrafficControlCore.TaskSystem;

namespace TrafficControlCore.Cars
{
    public class SimulatorCarFourWay : DummyCar
    {
        [MethodMember(name = "故障车货物存放为某地")]
        public async void FalutPushGoods()
        {
            try
            {
                var agent = DispatchMisson.AgentManager.getAgent(id);
                var pt = await Program.UI.getPoint(new UIOps.getPointOptions() { site = true });
                var site = SimpleLib.GetSite(pt.site);
                Console.WriteLine($"push goods site {pt.site}");
                if (agent.IsFault == true && agent.have_goods == true && site.tags.Contains("Goods") == false)  // 如果存在货物且为故障
                {
                    if (agent.TaskId != "-1")
                    {
                        DispatchMisson.TaskManager.RollBack(agent.TaskId, pt.site);
                    }
                    agent.FREE();
                    agent.GetCar.tags.Remove("status");

                    site.tags.Add("Goods");
                }
                else
                    Console.WriteLine($"不合法的动作, 小车不存在货物，小车无故障或目标点不存在货物");
            }
            catch { }
        }

        [MethodMember(name = "去某地无动作")]
        public async void Goto()
        {
            try
            {
                var pt = await Program.UI.getPoint(new UIOps.getPointOptions() { site = true });
                Console.WriteLine($"goto site {pt.site}");
                var task = new AgvTask(new List<(int, bool)>() { (pt.site, true) });
                task.Car = id;
                task.sendTime = DateTime.Now;
                task.taskType = TaskType.Manual;
                task.finalAction = FinalAction.None;
                DispatchMisson.TaskManager.NewAgvTask(task);

            }
            catch { }
        }
        [MethodMember(name = "去某地取货")]
        public async void GotoFetch()
        {
            try
            {
                var pt = await Program.UI.getPoint(new UIOps.getPointOptions() { site = true });
                Console.WriteLine($"goto site {pt.site}");
                var task = new AgvTask(new List<(int, bool)>() { (pt.site, true) });
                task.Car = id;
                task.sendTime = DateTime.Now;
                task.taskType = TaskType.Manual;
                task.finalAction = FinalAction.Fetch;
                DispatchMisson.TaskManager.NewAgvTask(task);
            }
            catch { }
        }
        [MethodMember(name = "去某地放货")]
        public async void GotoPut()
        {
            try
            {
                var pt = await Program.UI.getPoint(new UIOps.getPointOptions() { site = true });
                Console.WriteLine($"goto site {pt.site}");
                var task = new AgvTask(new List<(int, bool)>() { (pt.site, true) });
                task.Car = id;
                task.sendTime = DateTime.Now;
                task.taskType = TaskType.Manual;
                task.finalAction = FinalAction.Put;
                DispatchMisson.TaskManager.NewAgvTask(task);
            }
            catch { }
        }
        [MethodMember(name = "模拟故障")]
        public void SetFault()
        {
            try
            {
                tags.Add("fault");
                ClearTraffic();
                var agent = DispatchMisson.AgentManager.getAgent(id);
                agent.initGoalList();
            }
            catch
            {

            }
        }
        [MethodMember(name = "取消故障")]
        public void RemoveFault()
        {
            try
            {
                tags.Remove("fault");
                speed = 1000;
            }
            catch
            {

            }
        }
        [MethodMember(name = "清除资源")]
        public void ClearTraffic()
        {
            try
            {
                clearTag = true;
                if (status.holdingLocks.Count > 3)
                    lock (obj)
                        status.holdingLocks.RemoveRange(3, status.holdingLocks.Count - 3);
            }
            catch
            {

            }
        }
        [MethodMember(name = "手动上线")]
        public void Manual_Online()
        {
            lstatus = "上线";
        }
        [MethodMember(name = "手动下线")]
        public void Manual_Offline()
        {
            lstatus = "下线";
        }
    }
}
