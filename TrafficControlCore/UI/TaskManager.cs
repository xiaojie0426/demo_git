using SimpleComposer.RCS;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrafficControlCore.DispatchSystem;
using TrafficControlCore.TaskSystem;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static TrafficControlCore.UI.TaskManager;
using TaskStatus = TrafficControlCore.TaskSystem.TaskStatus;

namespace TrafficControlCore.UI
{
    public partial class TaskManager : Form
    {
        public TaskManager()
        {
            InitializeComponent();
            Init();
        }
        //int firstItem;
        //ListViewItem[] taskCache;
        public void Init()
        {
            executingTaskslistView.View = View.Details;
            executingTaskslistView.Columns.Add("Id", 30, HorizontalAlignment.Center);
            executingTaskslistView.Columns.Add("任务类型", 100, HorizontalAlignment.Center);
            executingTaskslistView.Columns.Add("任务优先级", 100, HorizontalAlignment.Center);
            executingTaskslistView.Columns.Add("执行车辆", 100, HorizontalAlignment.Center);
            executingTaskslistView.Columns.Add("取货点", 100, HorizontalAlignment.Center);
            executingTaskslistView.Columns.Add("放货点", 100, HorizontalAlignment.Center);
            executingTaskslistView.Columns.Add("任务状态", 100, HorizontalAlignment.Center);
            executingTaskslistView.Columns.Add("取货状态", 100, HorizontalAlignment.Center);
            executingTaskslistView.Columns.Add("发送时间", 100, HorizontalAlignment.Center);
            executingTaskslistView.Columns.Add("取货点是否合法", 100, HorizontalAlignment.Center);

            //executingTaskslistView.RetrieveVirtualItem += RetrieveVirtualItem;
            //executingTaskslistView.CacheVirtualItems += CacheVirtualItems;
            //executingTaskslistView.DoubleBufferedListView(true);
            //executingTaskslistView.DoubleBuffering(true);
        }
        private void UpdateTaskCache()
        {
            // var taskList = DispatchMisson.TaskManager.TaskList.Values.OrderBy(t => Convert.ToInt32(t.sendTime)).ToArray();
            var taskList = DispatchMisson.TaskManager.TaskList.Values.OrderBy(t => t.sendTime).ToArray();
            executingTaskslistView.BeginUpdate();
            executingTaskslistView.Items.Clear();
            //if (taskCache == null || taskCache.Count() == 0)
            //    taskCache = new ListViewItem[taskList.Length - firstItem];
            //Fill the cache with the appropriate ListViewItems.
            for (int i = 0; i < taskList.Length; i++)
            {

                var lvi = new ListViewItem();

                //lvi.Text = (++num).ToString();
                var ts = "";
                if (taskList[i].taskStatus == TaskStatus.Finished)
                {
                    ts = "已完成";
                    lvi.BackColor = Color.LightGreen;
                }
                else if (taskList[i].taskStatus == TaskStatus.Executing)
                {
                    ts = "执行中";
                    lvi.BackColor = Color.Orange;
                }
                else if (taskList[i].taskStatus == TaskStatus.Ready)
                {
                    ts = "未开始";
                    lvi.BackColor = Color.LightGray;
                }
                lvi.Text = taskList[i].Id;
                lvi.SubItems.Add(taskList[i].taskType.ToString());
                lvi.SubItems.Add(taskList[i].Priority.ToString());
                lvi.SubItems.Add(taskList[i].Car.ToString());
                
                if (taskList[i].finalAction==FinalAction.Fetch)
                {
                    lvi.SubItems.Add(taskList[i].sites[0].siteID.ToString());
                    lvi.SubItems.Add("");
                }
                else if(taskList[i].finalAction == FinalAction.Fetch)
                {
                    lvi.SubItems.Add("");
                    lvi.SubItems.Add(taskList[i].sites[1].siteID.ToString());
                }
                else if (taskList[i].finalAction == FinalAction.None&&taskList[i].sites.Count == 1)
                {
                    lvi.SubItems.Add(taskList[i].sites[0].siteID.ToString());
                    lvi.SubItems.Add(taskList[i].sites[0].siteID.ToString());
                }
                else
                {
                    lvi.SubItems.Add(taskList[i].sites[0].siteID.ToString());
                    lvi.SubItems.Add(taskList[i].sites[1].siteID.ToString());
                }
                
                lvi.SubItems.Add(taskList[i].taskStatus.ToString());
                lvi.SubItems.Add(taskList[i].executingStatus.ToString());
                lvi.SubItems.Add(taskList[i].sendTime.ToString(@"HH\:mm\:ss"));

                lvi.SubItems.Add(taskList[i].isValidTarget().ToString());

                executingTaskslistView.Items.Add(lvi);
            }
            //executingTaskslistView.VirtualListSize = taskList.Length;
            executingTaskslistView.EndUpdate();
        }
        private void timer1_Tick(object sender, EventArgs e)
        {
            //UpdateTaskCache();

            var executingTaskNum = DispatchMisson.TaskManager.TaskList.Count;
            var finishtaskTaskNum = DispatchMisson.TaskManager.FinishTaskList.Count;

            taskNum.Text = $"执行任务总数:{executingTaskNum},已完成任务数量:{finishtaskTaskNum}";
        }

        private void toolStripDropDownButton1_Click(object sender, EventArgs e)
        {
            UpdateTaskCache();
        }
    }
    public static class DoubleBufferListView
    {
        public static void DoubleBufferedListView(this System.Windows.Forms.ListView dgv, bool flag)
        {
            var dgvType = dgv.GetType();
            var pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            if (pi != null) pi.SetValue(dgv, flag, null);
        }
    }

    public static class ControlExtensions
    {
        public static void DoubleBuffering(this Control control, bool enable)
        {
            var method = typeof(Control).GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(control, new object[] { ControlStyles.OptimizedDoubleBuffer, enable });
        }
    }
}
