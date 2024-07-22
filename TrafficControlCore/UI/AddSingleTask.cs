using SimpleCore;
using SimpleCore.PropType;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrafficControlCore.DispatchSystem;
using TrafficControlCore.TaskSystem;
using static TrafficControlCore.TaskSystem.Utils;

namespace TrafficControlCore.UI
{
    public partial class AddSingleTask : Form
    {
        public AddSingleTask()
        {
            InitializeComponent();
            Init();
        }
        Site[] sites;
        Dictionary<string, List<Site>> shelfSites;
        Dictionary<string, List<Site>> outSites;
        Dictionary<string, List<Site>> inSites;
        Dictionary<string, List<KeyValuePair<int, ColumnTaskInfo>>> columnInfo;
        AgvTask task;
        void Init()
        {
            sites = SimpleLib.GetAllSites();
            shelfSites = sites.Where(p => p.fields.ContainsKey("shelf")).GroupBy(p => p.layerName).OrderBy(p => p.Key).ToDictionary(p => p.Key, v => v.Select(p => p).ToList());
            outSites = sites.Where(p => p.fields.ContainsKey("out")).OrderByDescending(s => s.y).GroupBy(p => p.layerName).OrderBy(p => p.Key).ToDictionary(p => p.Key, v => v.Select(p => p).ToList()); ;
            inSites = sites.Where(p => p.fields.ContainsKey("in")).OrderByDescending(s => s.y).GroupBy(p => p.layerName).OrderBy(p => p.Key).ToDictionary(p => p.Key, v => v.Select(p => p).ToList()); ;
            columnInfo = testMission.allColumnInfo.GroupBy(p => p.Value.siteList?.First().layerName).OrderBy(p => p.Key).ToDictionary(p => p.Key, v => v.Select(p => p).ToList());
        }
        private void button1_Click(object sender, EventArgs e)
        {
            List<int> numbers = taskSitesSet.Text.Split(',')
                         .Select(int.Parse)
                         .ToList();
            List<(int, bool)> movements = numbers.Select(p => (p,true)).ToList();
            AgvTask task = new AgvTask(movements)
            {
                Car = -1,
                sites = movements,
                sendTime = DateTime.Now,
                taskType = (TaskType)Enum.Parse(typeof(TaskType), taskTypeSelect.Text),
                Priority = int.Parse(taskPrioritySet.Text)
            };
            DispatchMisson.TaskManager.NewAgvTask(task);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                Random rdtt = new Random();
                AgvTask task = null;
                List<(int, bool)> movements = new();
                TaskType type = TaskType.InFlow;
            retry:
                string randomKey = columnInfo.Keys.ElementAt(rdtt.Next(columnInfo.Count));
                var columns = (Key: randomKey, Value: columnInfo[randomKey]);
                var column = columns.Value[rdtt.Next(columns.Value.Count)];
                if (column.Value.curTaskType == ColumnTaskInfo.ColumnType.INFLOW)
                {
                    Site startsite = inSites[columns.Key][rdtt.Next(outSites[columns.Key].Count)];
                    Site s;

                    if (!column.Value.RequestSite(ColumnTaskInfo.ColumnType.INFLOW, out s))
                    {
                        goto retry;
                    }
                    var endNodeId = s.id.ToString();

                     movements = new() {
                                    (startsite.id, true),
                                    (s.id, true),
                                    };
                    type = TaskType.InFlow;

                }
                else if (column.Value.curTaskType == ColumnTaskInfo.ColumnType.OUTFLOW)
                {
                    int colTaskNum = rdtt.Next(column.Value.availableFetch);
                    Site site = null;
                    Site outsite = outSites[columns.Key][rdtt.Next(outSites[columns.Key].Count)];
                    if (!column.Value.RequestSite(ColumnTaskInfo.ColumnType.OUTFLOW, out site))
                        goto retry;
                    movements = new() { new(site.id, true), new(outsite.id, true) };

                    type = TaskType.OutFlow;

                }
                taskTypeSelect.Text = type.ToString();
                taskSitesSet.Text = string.Join(",", movements.Select(p => p.Item1).ToList());
                taskTypeSelect.Enabled = false;
                taskSitesSet.Enabled = false;
            }
            else
            {
                taskTypeSelect.Text = "";
                taskSitesSet.Text = "";
                taskTypeSelect.Enabled = true;
                taskSitesSet.Enabled = true;
            }
        }



    }
}

