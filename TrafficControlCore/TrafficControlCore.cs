using SimpleComposer;
using SimpleComposer.RCS;
using SimpleComposer.UI;
using SimpleCore;
using SimpleCore.Library;
using SimpleCore.PropType;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrafficControlCore.TaskSystem;
using TrafficControlCore.DispatchSystem;
using static TrafficControlCore.TaskSystem.AgvTask;
using static TrafficControlCore.TaskSystem.Utils;
using TrafficControlCore.UI;

namespace TrafficControlCore
{
    [MissionType("主线程", editor = typeof(testMission))]
    internal class testMission : Mission
    {
        public static Mission Create()
        {
            var cars = SimpleLib.GetAllCars();
            foreach (var car in cars)
            {
                ((DummyCar)car).Reset();
            }
            ComputeColumnDepthMap();
            ComputeSitesByColumn();
            allColumnInfo = ComputeColumnInfo();
            return new testMission();
        }
        public static TaskManager taskManagerForm;
        public static Dictionary<int, (int columnId, int depth)> columnDepthMap = new(); // 记录每个货位点的列索引及其在对应列的深度
        public static Dictionary<int, List<Site>> sitesByColumn = new(); // 按列记录深度记录每个列内的所有货位点
        public static Dictionary<int, ColumnTaskInfo> allColumnInfo = new Dictionary<int, ColumnTaskInfo>();
        public static Dictionary<int, int> depthMap = new() { { 0, 10000 } }; //id->depth
        [FieldMember(desc = "每分钟下发任务数量")] public int numberPerMinutes = 20;

        public static void ComputeColumnDepthMap()
        {
            if (columnDepthMap.Count > 0)
                return;

            foreach (var site in SimpleLib.GetAllSites().Where(p => p.fields.ContainsKey("shelf")))
            {
                columnDepthMap.Add(site.id, site.status.columnInfo);
            }
        }
        public static void ComputeSitesByColumn()
        {
            if (sitesByColumn.Count > 0)
                return;
            Dictionary<int, List<(Site site, int depth)>> allColumnSites = new();
            foreach (var siteInfo in columnDepthMap)
            {
                if (siteInfo.Value.columnId == -1) continue;
                if (!allColumnSites.ContainsKey(siteInfo.Value.columnId))
                    allColumnSites[siteInfo.Value.columnId] = new List<(Site site, int depth)>() { };
                allColumnSites[siteInfo.Value.columnId].Add((SimpleLib.GetSite(siteInfo.Key), siteInfo.Value.depth));
            }

            foreach (var pair in allColumnSites)
            {
                var sitesList = pair.Value.OrderBy(p => p.depth).Select(p => p.site).ToList();
                sitesByColumn[pair.Key] = sitesList;
            }
        }
        public static Dictionary<int, ColumnTaskInfo> ComputeColumnInfo()
        {
            Dictionary<int, ColumnTaskInfo> allColumnInfo = new Dictionary<int, ColumnTaskInfo>();
            foreach (var pair in sitesByColumn)
            {
                var columnInfo = new ColumnTaskInfo()
                {
                    id = pair.Key,
                    siteList = pair.Value,
                };
                columnInfo.Init();
                allColumnInfo.Add(columnInfo.id, columnInfo);
            }
            return allColumnInfo;
        }
        public void Init()
        {
            // depth-wise.
            while (depthMap.Any(d => d.Value >= 10000))
                depthMap = SimpleLib.GetAllSites().Where(p => p.fields.ContainsKey("shelf") && !p.fields.ContainsKey("disabled")).ToDictionary(
                    p => p.id, p => p.relatedTracks.Select(t =>
                    {
                        var other = SimpleLib.GetSite(SimpleLib.GetTrack(t).GetOther(p.id));
                        return other.relatedTracks.Count >= 3
                            ? 0 // main route.
                            : depthMap.TryGetValue(other.id, out var _do)
                                ? _do // expanded
                                : 9999; //non-expanded
                    }).Min() + 1);
            Diagnosis.Post("Done initializing MWS");
            //foreach (var dic in depthMap) { Console.WriteLine($"Key:{dic.Key},Value:{dic.Value}"); }
        }
        [MethodMember(name = "展示任务列表", desc = "展示任务列表")]
        public void showUI()
        {
            Task.Run(() =>
            {
                if (taskManagerForm == null)
                    taskManagerForm = new TaskManager();
                Application.Run(taskManagerForm);
            });
            
        }
        [MethodMember(name = "随机货物分布", desc = "重新分布货物的位置")]
        public void InitShelf()
        {
            var sites = SimpleLib.GetAllSites();
            var shelfSites = sites.Where(p => p.fields.ContainsKey("shelf")).ToList();
            Random rdtt = new Random();
            for (int i = 0; i < allColumnInfo.Count / 2; ++i)
            {
                int columnIndex = rdtt.Next(allColumnInfo.Count());
                if (columnIndex == allColumnInfo.Count())
                    return;
                var column = allColumnInfo.ToArray()[columnIndex];
                var fetchNum = rdtt.Next(column.Value.siteList.Count);
                var putNum = rdtt.Next(column.Value.siteList.Count);
                var colTaskNum = fetchNum - putNum;
                if (colTaskNum > 0)
                {
                    colTaskNum = colTaskNum > column.Value.availableFetch ? column.Value.availableFetch : colTaskNum;
                }
                if (colTaskNum < 0)
                {
                    colTaskNum = -colTaskNum > column.Value.availablePut ? -column.Value.availablePut : colTaskNum;
                }
                while (colTaskNum > 0)
                {
                    Site site = null;
                    if (!column.Value.RequestSite(ColumnTaskInfo.ColumnType.OUTFLOW, out site))
                        break;
                    site?.tags.Remove("Goods");
                    if (site != null)
                        colTaskNum--;
                }
                while (colTaskNum < 0)
                {
                    Site site = null;
                    if (!column.Value.RequestSite(ColumnTaskInfo.ColumnType.INFLOW, out site))
                        break;
                    if (site.type != 2)
                    {
                        site?.tags.Add("Goods");
                    }
                    if (site != null)
                        colTaskNum++;
                }
            }
        }
        [MethodMember(name = "下发随机出库任务", desc = "依次指定向每个库口下发任务的数量")]
        public void RandomFourWayMission()
        {
            var sites = SimpleLib.GetAllSites();
            var shelfSites = sites.Where(p => p.fields.ContainsKey("shelf")).ToList();
            var outSites = sites.Where(p => p.fields.ContainsKey("out")).OrderByDescending(s => s.y).ToList();
            var inSites = sites.Where(p => p.fields.ContainsKey("in")).OrderByDescending(s => s.y).ToList();
            Console.WriteLine($"开始下发随机出库任务！");

            Random rdtt = new Random();
            while (allColumnInfo.Count < 1)
            {
                Task.Delay(1000).Wait();
            }

            if (InputBox.ShowDialog($"指定下发模式： 0-优先整列，1-随机分配") != DialogResult.OK)
                return;
            int mode = 0;
            try
            {
                mode = Convert.ToInt32(InputBox.ResultValue);
                if (mode != 0 && mode != 1)
                {
                    MessageBox.Show($"无效的模式，默认使用模式0");
                    mode = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
            if (InputBox.ShowDialog($"输入每个出库口随机出库任务的数量，逗号隔开") != DialogResult.OK)
                return;
            string[] str = InputBox.ResultValue.Split(',');
            if (str.Length > outSites.Count)
            {
                Console.WriteLine($"错误的输入格式：指定库口数量（{str.Length}）超过实际库口数量（{outSites.Count}）");
                return;
            }
            new Thread(() =>
            {
                List<AgvTask> agvTasks = new List<AgvTask>();
                // 循环手动输入的出库口可出库的数量，小车将随机生成的货物由货架上运往出库口，（当前位置-货物位置-出货口）
                for (int i = 0; i < str.Length; ++i)
                {
                    try
                    {
                        int taskNum = Convert.ToInt32(str[i]);
                        while (taskNum > 0)
                        {
                            int columnIndex = rdtt.Next(allColumnInfo.Count()); // 随机[0，Count）范围内随机数
                            if (columnIndex == allColumnInfo.Count())
                                return;
                            var column = allColumnInfo.ToArray()[columnIndex];
                            //if (column.Value.curTaskType == ColumnTaskInfo.ColumnType.INFLOW)
                            //    continue;
                            int colTaskNum = 0;
                            Site site = null;
                            if (mode == 0)
                            {
                                //colTaskNum = (column.Value.availableFetch); // 优先出库整列
                                while (taskNum > 0 && column.Value.RequestSite(ColumnTaskInfo.ColumnType.OUTFLOW, out site))
                                {
                                    List<(int, bool)> movements = new() {
                                    (site.id, true),
                                    (outSites[i].id, true),
                                    };
                                    AgvTask task = new AgvTask(movements)
                                    {
                                        Car = -1,
                                        sites = movements,
                                        sendTime = DateTime.Now,
                                        taskType = TaskType.OutFlow,
                                    };
                                    task.Name = $"{column.Key}({task.sites.Where(p => p.isKeySite).First().siteID}) - {i}";
                                    agvTasks.Add(task);
                                    taskNum--;
                                }
                            }
                            else if (mode == 1)
                            {
                                //colTaskNum = rdtt.Next(column.Value.availableFetch);
                                if (!column.Value.RequestSite(ColumnTaskInfo.ColumnType.OUTFLOW, out site))
                                    continue;
                                List<(int, bool)> movements = new() {
                                    (site.id, true),
                                    (outSites[i].id, true),
                                    };
                                AgvTask task = new AgvTask(movements)
                                {
                                    Car = -1,
                                    sites = movements,
                                    sendTime = DateTime.Now,
                                    taskType = TaskType.OutFlow,
                                };
                                task.Name = $"{column.Key}({task.sites.Where(p => p.isKeySite).First().siteID}) - {i}";
                                agvTasks.Add(task);
                                taskNum--;
                            }
                            else
                            {
                                Diagnosis.Log($"意外的模式输入mode = {mode}");
                                return;
                            }

                            //int exeNum = colTaskNum >= taskNum ? taskNum : colTaskNum;
                            //while (exeNum > 0)
                            //{
                            //    Site site = null;
                            //    if (!column.Value.RequestSite(ColumnTaskInfo.ColumnType.OUTFLOW, out site))
                            //        break;
                            //    List<(int, bool)> movements = new() {
                            //        (site.id, true),
                            //        (outSites[i].id, true),
                            //        };
                            //    AgvTask task = new AgvTask(movements)
                            //    {
                            //        Car = -1,
                            //        sites = movements,
                            //        sendTime = DateTime.Now,
                            //        taskType = TaskType.OutFlow,
                            //    };
                            //    task.Name = $"{column.Key}({task.sites.Where(p => p.isKeySite).First().siteID}) - {i}";
                            //    agvTasks.Add(task);
                            //    taskNum--;
                            //    exeNum--;
                            //}
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{ex}");
                        return;
                    }

                }
                Console.WriteLine("Num of AGV Tasks: " + agvTasks.Count());
                    foreach (var task in agvTasks)
                    {
                        DispatchMisson.TaskManager.NewAgvTask(task);
                    }
            })
            { IsBackground = true }.Start();
            Console.WriteLine($"已经下发随机出库任务！");
        }
        [MethodMember(name = "下发入库任务", desc = "按照顺序依次下发入库任务")]
        public void OrderedInFlowMission()
        {
            if (InputBox.ShowDialog($"输入待入库数量") != DialogResult.OK)
                return;
            if (!int.TryParse(InputBox.ResultValue, out int taskNum))
            {
                Console.WriteLine($"错误的输入格式");
                return;
            }
            Task.Run(async () =>
            {
                var sites = SimpleLib.GetAllSites();
                var inSites = sites.Where(p => p.fields.ContainsKey("in")).OrderByDescending(s => s.y).ToList();
                Random random = new Random();
                while (allColumnInfo.Count < 1)
                    await Task.Delay(500);
                int lastCol = 0;
                var allClolumnInfos = allColumnInfo.ToArray();
                for (int i = 0; i < taskNum; i++)
                {
                    var startNodeId = inSites[random.Next(0, inSites.Count)];
                    //Site s = SelectSite(DepthSitesWithoutGoods) ;
                    Site s;
                    var dpendDic = allColumnInfo.Where(p => p.Value.siteList?.First().layerName == startNodeId.layerName).ToArray();
                    while (!dpendDic[dpendDic.Length - lastCol - 1].Value.RequestSite(ColumnTaskInfo.ColumnType.INFLOW, out s))
                    // while (!dpendDic[lastCol].Value.RequestSite(ColumnTaskInfo.ColumnType.INFLOW, out s))
                    {
                        lastCol += 1;
                        if (lastCol >= dpendDic.Length)
                        {
                            lastCol = 0;
                            Diagnosis.Log($"所有列都已入库至少一次，重新开始循环");
                        }
                        continue;
                    }
                    var endNodeId = s.id.ToString();

                    List<(int, bool)> movements = new() {
                                    (startNodeId.id, true),
                                    (s.id, true),
                                    };
                    AgvTask task = new AgvTask(movements)
                    {
                        Car = -1,
                        sites = movements,
                        sendTime = DateTime.Now,
                        taskType = TaskType.InFlow,
                    };
                    DispatchMisson.TaskManager.NewAgvTask(task);

                }
            });

        }

        [MethodMember(name = "出库所有", desc = "出库所有")]
        public void CarryAll()
        {
            Init();
            var sites = SimpleLib.GetAllSites();
            var shelfSites = SimpleLib.GetAllSites().Where(p => p.fields.ContainsKey("shelf")).ToList();

            // 打乱列表顺序
            Random random = new Random();
            int n = shelfSites.Count;
            while (n > 1)
            {
                n--;
                int k = random.Next(n + 1);
                var value = shelfSites[k];
                shelfSites[k] = shelfSites[n];
                shelfSites[n] = value;
            }
            var outSites = SimpleLib.GetAllSites().Where(p => p.fields.ContainsKey("out")).OrderByDescending(s => s.y).ToList().GroupBy(p => p.layerName).ToDictionary(p => p.Key, v => v.ToList());
            foreach(var p in allColumnInfo)
            {
                var temp = Math.Min(p.Value.availablePut, p.Value.siteList.Count);
                for (int i = 0; i < temp; ++i)
                {
                    p.Value.RequestSite(ColumnTaskInfo.ColumnType.INFLOW, out var site);
                    if (site.type != 2)
                    {
                        site.tags.Add("Goods");
                    }
                }
            }

            shelfSites = shelfSites.OrderBy(s => depthMap.TryGetValue(s.id, out var d) ? d : 0).ToList();
            var groupSites = shelfSites.GroupBy(p => p.layerName).ToList();
            Random rdtt = new Random();
            foreach (var layerSites in groupSites)
                Task.Run(async () =>
                {
                    int num = 0;
                    Site startSite, endSite;
                    string taskName;
                    var endSites = outSites[layerSites.Key];
                    foreach (var site in layerSites)
                    {
                        try
                        {
                            startSite = site;
                            endSite = endSites[rdtt.Next(endSites.Count)];

                            List<(int, bool)> movements = new() {
                            new (startSite.id, true),
                            new (endSite.id, true)
                                //new Mobile(-1)
                            };

                            taskName = $"出库{startSite.id}->{endSite.id}";
                            AgvTask task = new AgvTask(movements)
                            {
                                Car = -1,
                                taskType = TaskType.OutFlow,
                                sendTime = DateTime.Now,
                            };
                            DispatchMisson.TaskManager.NewAgvTask(task);
                            //OrderManager.GetInstance().Add(new AgvTask[1] { task });
                            if ((num++) % numberPerMinutes == 0)
                            {
                                await Task.Delay(60000);
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                    Diagnosis.Log($"{layerSites.Key}出库完毕，线程结束");
                    return;
                });

        }

        [MethodMember(name = "入库所有", desc = "入库所有")]
        public void InputAll()
        {
            Init();
            var sites = SimpleLib.GetAllSites();
            var shelfSites = SimpleLib.GetAllSites().Where(p => p.fields.ContainsKey("shelf")).ToList();

            // 打乱列表顺序
            Random random = new Random();
            int n = shelfSites.Count;
            while (n > 1)
            {
                n--;
                int k = random.Next(n + 1);
                var value = shelfSites[k];
                shelfSites[k] = shelfSites[n];
                shelfSites[n] = value;
            }
            var inSites = SimpleLib.GetAllSites().Where(p => p.fields.ContainsKey("in")).OrderByDescending(s => s.y).ToList().GroupBy(p => p.layerName).ToDictionary(p => p.Key, v => v.ToList());

            foreach (var p in allColumnInfo)
            {
                var temp = Math.Min(p.Value.availableFetch, p.Value.siteList.Count);
                for (int i = 0; i < temp; ++i)
                {
                    p.Value.RequestSite(ColumnTaskInfo.ColumnType.OUTFLOW, out var site);
                }
            }
            shelfSites = shelfSites.OrderBy(s => depthMap.TryGetValue(s.id, out var d) ? d : 0).ToList();
            var groupSites = shelfSites.GroupBy(p => p.layerName).ToList();
            Random rdtt = new Random();
            foreach (var layerSites in groupSites)
                Task.Run(async () =>
                {

                    Random random = new Random();

                    int lastCol, clock;
                    var dpendDic = allColumnInfo.Where(p => p.Value.siteList?.First().layerName == layerSites.Key).ToArray();
                    Site startNode, endNode;
                    var startSites = inSites[layerSites.Key];
                    for (int i = 0; i < layerSites.Count() * 2; i++)
                    {
                        try
                        {

                            lastCol = random.Next(0, dpendDic.Count());
                            clock = 0;
                            while (!dpendDic[lastCol].Value.RequestSite(ColumnTaskInfo.ColumnType.INFLOW, out endNode))
                            {
                                lastCol = random.Next(0, dpendDic.Count());
                                if (clock++ >= layerSites.Count() * 2)
                                {
                                    Diagnosis.Log($"{layerSites.Key}入库完毕，线程结束");
                                    return;
                                }
                                continue;
                            }

                            startNode = startSites[random.Next(startSites.Count)];
                            List<(int, bool)> movements = new() {
                                    (startNode.id, true),
                                    (endNode.id, true),
                                    };
                            AgvTask task = new AgvTask(movements)
                            {
                                Car = -1,
                                sites = movements,
                                sendTime = DateTime.Now,
                                taskType = TaskType.InFlow,
                            };
                            DispatchMisson.TaskManager.NewAgvTask(task);
                            if (i % numberPerMinutes == 0 && i != 0)
                                await Task.Delay(60000);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                    Diagnosis.Log($"{layerSites.Key}入库完毕，线程结束");
                    return;
                });

        }
        public static bool flag = false;
        [MethodMember(name = "开启或关闭永动测试", desc = "无限发送入库和出库任务")]
        public void JustDoIt()
        {
            if (!flag)
            {
                flag = true;
                var sites = SimpleLib.GetAllSites();
                var shelfSites = sites.Where(p => p.fields.ContainsKey("shelf")).GroupBy(p => p.layerName).OrderBy(p => p.Key).ToDictionary(p => p.Key, v => v.Select(p => p).ToList());
                var outSites = sites.Where(p => p.fields.ContainsKey("out")).OrderByDescending(s => s.y).GroupBy(p => p.layerName).OrderBy(p => p.Key).ToDictionary(p => p.Key, v => v.Select(p => p).ToList()); ;
                var inSites = sites.Where(p => p.fields.ContainsKey("in")).OrderByDescending(s => s.y).GroupBy(p => p.layerName).OrderBy(p => p.Key).ToDictionary(p => p.Key, v => v.Select(p => p).ToList()); ;
                var columnInfo = allColumnInfo.GroupBy(p => p.Value.siteList?.First().layerName).OrderBy(p => p.Key).ToDictionary(p => p.Key, v => v.Select(p => p).ToList());
                Random rdtt = new Random();
                foreach (var columns in columnInfo)
                {
                    Task.Run(async () =>
                    {
                        int num = 0;
                        List<AgvTask> agvTasks = new List<AgvTask>();
                        while (flag)
                        {
                            await Console.Out.WriteLineAsync($"线程{columns.Key}执行中");
                            if (DispatchMisson.TaskManager.TaskList.Count > SimpleLib.GetAllCars().Count() * 2)
                            {
                                await Task.Delay(10000);
                                continue;
                            }
                            for (int i = 0; i < numberPerMinutes; ++i)
                            {
                                while (allColumnInfo.Count < 1)
                                {
                                    await Task.Delay(1000);
                                }
                                var column = columns.Value[rdtt.Next(columns.Value.Count)];
                                if (column.Value.curTaskType == ColumnTaskInfo.ColumnType.INFLOW)
                                {
                                    Site startsite = inSites[columns.Key][rdtt.Next(outSites[columns.Key].Count)];
                                    Site s;

                                    if (!column.Value.RequestSite(ColumnTaskInfo.ColumnType.INFLOW, out s))
                                    {
                                        continue;
                                    }
                                    var endNodeId = s.id.ToString();

                                    List<(int, bool)> movements = new() {
                                    (startsite.id, true),
                                    (s.id, true),
                                    };
                                    AgvTask task = new AgvTask(movements)
                                    {
                                        Car = -1,
                                        sites = movements,
                                        sendTime = DateTime.Now,
                                        taskType = TaskType.InFlow,
                                    };
                                    DispatchMisson.TaskManager.NewAgvTask(task);


                                }
                                else if (column.Value.curTaskType == ColumnTaskInfo.ColumnType.OUTFLOW)
                                {
                                    //colTaskNum = (column.Value.availableFetch); // 优先出库整列
                                    //if (taskByGroup.TryGetValue(column.Key, out var group) && group.Count > 0) // 当前列有出库任务未规划
                                    //    continue;
                                    int colTaskNum = rdtt.Next(column.Value.availableFetch);


                                    Site site = null;
                                    Site outsite = outSites[columns.Key][rdtt.Next(outSites[columns.Key].Count)];
                                    if (!column.Value.RequestSite(ColumnTaskInfo.ColumnType.OUTFLOW, out site))
                                        continue;
                                    //List<Movement> movements = new List<Movement> {
                                    //new Mobile(site.id, key:true),
                                    //new Mobile(outsite.id, key:true)
                                    ////new Mobile(-1)
                                    //};
                                    //AgvTask task = new AgvTask(movements)
                                    //{
                                    //    Car = -1,
                                    //    KeySites = movements.Where(p => p.Destination != -1).Select(m => m.Destination).ToList(),
                                    //    SendTime = DateTime.Now,
                                    //    pTaskType = AgvTask.TaskType.OutFlow,
                                    //};
                                    //task.Name = $"{column.Key}({task.KeySites.First()}) - {outsite.id}";
                                    //agvTasks.Add(task);

                                    //OrderManager.GetInstance().Add(agvTasks.ToArray());
                                    //agvTasks.Clear();
                                    List<(int, bool)> movements = new() { new(site.id, true), new(outsite.id, true) };

                                    AgvTask task = new AgvTask(movements)
                                    {
                                        Car = -1,
                                        taskType = TaskType.OutFlow,
                                        sendTime = DateTime.Now,
                                    };
                                    DispatchMisson.TaskManager.NewAgvTask(task);

                                }
                            }
                            if (num++ > 5)
                            {
                                num = 0;
                            retry1:
                                var columnFetch = columns.Value[rdtt.Next(columns.Value.Count)];
                                if (columnFetch.Value.curTaskType != ColumnTaskInfo.ColumnType.OUTFLOW) goto retry1;
                            retry2:
                                var columnPut = columns.Value[rdtt.Next(columns.Value.Count)];
                                if (columnPut.Value.curTaskType != ColumnTaskInfo.ColumnType.INFLOW) goto retry2;

                                Site startsite;
                                Site endsite;

                                if (!columnFetch.Value.RequestSite(ColumnTaskInfo.ColumnType.OUTFLOW, out startsite))
                                {
                                    continue;
                                }
                                if (!columnPut.Value.RequestSite(ColumnTaskInfo.ColumnType.INFLOW, out endsite))
                                {
                                    continue;
                                }

                                List<(int, bool)> movements = new() {
                                    (startsite.id, true),
                                    (endsite.id, true),
                                    };
                                AgvTask task = new AgvTask(movements)
                                {
                                    Car = -1,
                                    sites = movements,
                                    sendTime = DateTime.Now,
                                    taskType = TaskType.Shift,
                                };
                                DispatchMisson.TaskManager.NewAgvTask(task);
                            }
                            await Task.Delay(60000);
                        }
                    });

                }

            }
            else
            {
                flag = false;
            }

        }
        [MethodMember(name = "下发单个任务", desc = "下发单个任务")]
        public void SingleTask()
        {
            AddSingleTask form = new AddSingleTask();
            form.FormClosed += (sender, e) =>
            {
                form.Dispose();
            };

            Thread thread = new Thread(() =>
            {
                Application.Run(form);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        [MethodMember(name = "一键上线", desc = "一键上线")]
        public void onlineAll()
        {
            foreach (var car in SimpleLib.GetAllCars())
            {
                var acar = car as DummyCar;
                acar.lstatus = "上线";
            }
        }
        [MethodMember(name = "一键下线", desc = "一键下线")]
        public void offlineAll()
        {
            foreach (var car in SimpleLib.GetAllCars())
            {
                var acar = car as DummyCar;
                acar.lstatus = "下线";
            }
        }
        [MethodMember(name = "小车速度加速", desc = "小车速度加速")]
        public void increase_speed()
        {
            foreach (var car in SimpleLib.GetAllCars())
            {
                var acar = car as DummyCar;
                acar.speed += 500;
            }
        }
        [MethodMember(name = "小车速度减速", desc = "小车速度减速")]
        public void decrease_speed()
        {
            foreach (var car in SimpleLib.GetAllCars())
            {
                var acar = car as DummyCar;
                acar.speed = Math.Max(500, acar.speed - 500);
            }
        }
    }
}