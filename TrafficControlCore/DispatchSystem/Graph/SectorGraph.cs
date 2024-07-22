/************************************************************************/
/* File:	  SectorGraph.cs										    */
/* Func:	  生成Graph，管理Sector                                      */
/* Author:	  Tan Yuhong                                                */
/************************************************************************/
using SimpleCore;
using SimpleCore.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using static TrafficControlCore.DispatchSystem.Graph.Node;
using static TrafficControlCore.DispatchSystem.DispatchMisson;
using static TrafficControlCore.DispatchSystem.Graph.Sector;
using TrafficControlCore.TaskSystem;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace TrafficControlCore.DispatchSystem.Graph
{
    public class SectorGraph
    {
        public HashSet<int> _highwaySiteId;  // 高速路的Site Id集合
        public Dictionary<int, Track> trackDict { get; set; }   // 给定track Id得到track Dict
        public Dictionary<int, Node> nodeDict { get; set; }     // 给定node Id得到node Dict
        public Dictionary<int, Sector> sectorDict { get; set; } // 给定sector Id得到sector Dict
        public Dictionary<int, int> nodeId2SectorId { get; set; }  //给定node Id得到sector Id
        // public Dictionary<(int, int, int), int> pos2NodeId { get; set; } // 给定(x, y, layout)，得到node Id
        public int trackSize;
        public int nodeSize;
        public int sectorSize;
        public int maxNodeId = 0;

        public int PORT_LINE_X = 132000;
        public int DELIVER_LINE_X = 130000; // 97000;最右侧分界线
        public int HIDDEN_LINE_X = 112000;
        public int STORAGE_LINE_X = 128000;
        // public const int KEY_POINT_THRESHOLD_X = 130000; // 98000;
        public int MAIN_LINE_X = 131000; // 99000;从右到左倒数第二条
        public bool ISLARGEGRAPH = true;
        // public const int left_delta_x = -1000;
        // public const int right_delta_x = 1000;
        // public const int up_delta_y = 1000;
        // public const int down_delta_y = -1000;
        public int MaxY = -2147483647;
        public int MaxX = -2147483647;
        public int MinX = 2147483647;
        public int MinY = 2147483647;

        public int CrossAreaDeltaY = 0;
        public HashSet<int> keyPointNode;       // trafficKeyPoint  非常繁忙的十字路口点，不能让机器人长期占用的点位。
                                                // TODO: 位于keyPointNode的点位如果被长期堵住了，则需要自行离开

        public HashSet<int> CommandLockSites = new HashSet<int>();  // holdinglocks
        public HashSet<int> PendingLockSites = new HashSet<int>();  // pendinglocks

        public HashSet<Node> HoisterNode = new HashSet<Node>();

        public Dictionary<int, int> inv_portId2AgentId;  // 给定库口相连的nodeId 返回现在正在进行存放货的AgentId
        public HashSet<float> interactPosY;  

        public int crossAreaNum = 0;
        public int layoutNum = 0;

        public SectorGraph()
        {
            _highwaySiteId = new HashSet<int>();
            trackDict = new Dictionary<int, Track>();
            nodeDict = new Dictionary<int, Node>();
            sectorDict = new Dictionary<int, Sector>();
            nodeId2SectorId = new Dictionary<int, int>();
            // pos2NodeId = new Dictionary<(int, int, int), int>();

            // interactPosY = new HashSet<float>();
            // portY2AgentId = new Dictionary<int, int>();
            inv_portId2AgentId = new Dictionary<int, int>();

            keyPointNode = new HashSet<int>();
            HoisterNode = new HashSet<Node>();

            trackSize = 0;
            nodeSize = 0;

            /************************************************************************/
            /* 建立节点信息,开始构建nodeDict                                     */
            /************************************************************************/
            foreach (var site in SimpleLib.GetAllSites())
            {
                NODE_TYPE type;
                if (site.fields.ContainsKey("fast") && site.fields["fast"] == "true")
                    type = NODE_TYPE.FAST;
                else if ((site.fields.ContainsKey("shelf") && site.fields["shelf"] == "true")
                    || (site.fields.ContainsKey("storage") && site.fields["storage"] == "true"))
                    type = NODE_TYPE.SHELF;
                else if (site.fields.ContainsKey("disabled") && (site.fields["disabled"] == "trye" || site.fields["disabled"] == "true"))
                    type = NODE_TYPE.BLOCK;
                else type = NODE_TYPE.NORMAL;

                Node node = new Node(site.id, type);
                nodeDict.Add(site.id, node);
                nodeSize = nodeSize + 1;
                // pos2NodeId.Add((node.x, node.y, node.layout), site.id);
                maxNodeId = Math.Max(maxNodeId, node.id);
                layoutNum = Math.Max(layoutNum, node.layout);

                MaxY = Math.Max(MaxY, node.y);
                MaxX = Math.Max(MaxX, node.x);
                MinY = Math.Min(MinY, node.y);
                MinX = Math.Min(MinX, node.x);

                // if (site.x > MAIN_LINE_X && interactPosY.Contains(site.y) == false && type != NODE_TYPE.BLOCK)
                //     interactPosY.Add(site.y);

                if (site.fields.ContainsKey("hoister") == true)
                {
                    node.isHoister = true;
                    HoisterNode.Add(node);
                }
                if (site.fields.ContainsKey("gate") == true)
                {
                    node.gateId = Convert.ToInt32(site.fields["gate"]);
                }
                if (site.fields.ContainsKey("in") == true)
                    node.isPortAndType = 1;
                else if (site.fields.ContainsKey("out") == true)
                    node.isPortAndType = 2;
                else node.isPortAndType = 0;
            }

            foreach (var node in nodeDict.Values) 
                if (node.gateId != -1)
                {
                    if (nodeDict.ContainsKey(node.gateId) == false) node.gateId = -1;
                    else  nodeDict[node.gateId].inv_gateId = node.id;
                }

            if (nodeSize >= 5000)
            {
                ISLARGEGRAPH = true;
                PORT_LINE_X = 130000;
                DELIVER_LINE_X = 130000; // 97000;最右侧分界线
                HIDDEN_LINE_X = 112000;
                STORAGE_LINE_X = 128000;
                MAIN_LINE_X = 131000; // 99000;从右到左倒数第二条
            }
            else
            {
                ISLARGEGRAPH = false;
                PORT_LINE_X = 132000;
                DELIVER_LINE_X = 130000; // 97000;最右侧分界线
                HIDDEN_LINE_X = 112000;
                STORAGE_LINE_X = 128000;
                MAIN_LINE_X = 131000; // 99000;从右到左倒数第二条
            }

            /************************************************************************/
            /* 初始化高速路节点，生成高速路方向                                     */
            /************************************************************************/
            var _initHighwayList = generateInitHighWayList();// 仅仅适用于TaiLG地图进行叠加
            // var _highwayDirList = generateHighwayDir(_initHighwayList);
            /*var cnt = 1;
            for (int i = 0; i < _initHighwayList.Count; ++i)
            {
                Diagnosis.Post("highway Point: " + _initHighwayList[i] + "Dir: " + _highwayDirList[i]);
                cnt++;
            }
            */
            // 1为从左到右边，2为从右到左

            crossAreaNum = (_initHighwayList.Count / layoutNum) / 2;
            CrossAreaDeltaY = (MaxY - MinY) / crossAreaNum;
                
            foreach (var highwayId in _initHighwayList)  // 进行高速路的添加
            {
                var isector = new Sector(sectorSize, SECTOR_TYPE.HIGHWAY);
                isector.NodeList.Add(nodeDict[highwayId]);  // sector添加节点highwayId
                _highwaySiteId.Add(highwayId);
                nodeId2SectorId.Add(highwayId, sectorSize);

                var nextHighwayId = generateNextHighwayId(highwayId);
                while (nodeDict.ContainsKey(nextHighwayId) && nodeDict[nextHighwayId].x <= MAIN_LINE_X)
                {
                    isector.NodeList.Add(nodeDict[nextHighwayId]);  // sector添加节点highwayId
                    _highwaySiteId.Add(nextHighwayId);
                    nodeId2SectorId.Add(nextHighwayId, sectorSize);
                    nextHighwayId = generateNextHighwayId(nextHighwayId); // nextHighwayId + 47;
                }
                sectorDict.Add(sectorSize, isector);
                sectorSize++;
            }

            // 构建track和Neighbor
            foreach (var track in SimpleLib.GetAllTracks())
            {
                Track newTrack;
                if (track.siteA < track.siteB)
                    newTrack = new Track(track.id, track.siteA, track.siteB, track.direction);
                else
                    newTrack = new Track(track.id, track.siteB, track.siteA, (track.direction >= 1 && track.direction <= 2) ? 3 - track.direction : track.direction);

                var nodeA = nodeDict[track.siteA];
                var nodeB = nodeDict[track.siteB];

                newTrack.nodeA = nodeDict[newTrack.x];
                newTrack.nodeB = nodeDict[newTrack.y];

                if (nodeA.type == NODE_TYPE.BLOCK || nodeB.type == NODE_TYPE.BLOCK) newTrack.direction = 3; // block不可通行
                // if (_highwaySiteId.Contains(track.siteA) && _highwaySiteId.Contains(track.siteB)) // 如果是高速路的情景，即左右均为高速节点, 则一定为单向
                // {
                //     for (int i = 0; i < _initHighwayList.Count; ++i)
                //     {
                //         var nowY = nodeA.y;
                //         var initY = nodeDict[_initHighwayList[i]].y;
                //         var nowLayout = nodeA.layout;
                //         var initLayout = nodeDict[_initHighwayList[i]].layout;
                //         if (nowY == initY && nowLayout == initLayout)
                //         {
                //             if (track.siteA < track.siteB)
                //             {
                //                 if (nodeA.x < nodeB.x)
                //                     newTrack.direction = _highwayDirList[i];
                //                 else newTrack.direction = 3 - _highwayDirList[i];
                //             }
                //             else
                //             {
                //                 if (nodeA.x < nodeB.x)
                //                     newTrack.direction = 3 - _highwayDirList[i];
                //                 else newTrack.direction = _highwayDirList[i];
                //             }

                //             if (_highwayDirList[i] == 0)
                //             {
                //                 if (((i - 5) % 2 + 5) % 2 == 1) newTrack.direction = 2;
                //                 else newTrack.direction = 1;
                //             }
                //             break;
                //         }
                //     }
                // }

                if (newTrack.direction != 3)    // 如果不是BLOCK点，则两边均可以添加neibor
                {
                    nodeA.addNeighbor(track.siteB, trackSize);
                    nodeB.addNeighbor(track.siteA, trackSize);
                }

                nodeA.rawNeighBor.Add(nodeB);
                nodeB.rawNeighBor.Add(nodeA);

                trackDict.Add(trackSize, newTrack);
                trackSize++;
            }

            // 开始构建shelf sector
            var closeSet = new HashSet<int>();
            var openSet = new HashSet<int>();
            /*
            foreach (var highwayId in _highwaySiteId)
            {
                closeSet.Add(highwayId);
            }
            */
            foreach (var node in nodeDict.Values)
            {
                if (node.type == NODE_TYPE.FAST)
                {
                    closeSet.Add(node.id);
                    if (_highwaySiteId.Contains(node.id) == false)
                        _highwaySiteId.Add(node.id);
                }
            }
            foreach (KeyValuePair<int, Node> kvp in nodeDict)
            {
                var id = kvp.Key;
                var initNode = kvp.Value;
                if (closeSet.Contains(id) == true || initNode.x >= PORT_LINE_X || initNode.type == NODE_TYPE.BLOCK) { continue; }
                openSet.Clear();
                openSet.Add(id);
                sectorSize = sectorSize + 1;
                createEmptySector();
                // 从initNode开始扩展
                while (openSet.Count > 0)
                {
                    var curNodeId = openSet.First();
                    openSet.Remove(curNodeId);
                    var curNode = nodeDict[curNodeId];
                    if (closeSet.Contains(curNodeId) == true) continue;
                    closeSet.Add(curNodeId);
                    if (nodeDict.ContainsKey(curNodeId) == false || curNode.x >= PORT_LINE_X || curNode.type == NODE_TYPE.BLOCK) continue;
                    if (curNode.x != initNode.x) continue;
                    sectorDict[sectorSize].NodeList.Add(curNode);
                    nodeId2SectorId.Add(curNode.id, sectorSize);
                    foreach (var rawNei in curNode.rawNeighBor)
                    {
                        openSet.Add(rawNei.id);
                    }
                }
            }
            // 更新sector中的type, 高速端点； 更新node中的sectorId
            foreach (KeyValuePair<int, Sector> kvp in sectorDict)
            {
                var key = kvp.Key;
                var value = kvp.Value;
                foreach (var inode in value.NodeList)
                {
                    inode.sectorId = key;
                }
                if (value.type == SECTOR_TYPE.HIGHWAY) continue;

                value.siteUp = -1;
                value.siteDown = -1;
                value.deadEndDir = 0;

                if (value.insideNode.Count == 0) continue;

                value.generateInsideUpAndDownPoint();   // 生成insideUpPoint，insideDownPoint
                var upPoint = value.insideUpPoint;
                foreach (var neiborNode in nodeDict[upPoint].rawNeighBor)// nodeDict[upPoint].neighBor)
                {
                    var neibor = neiborNode.id;
                    if (_highwaySiteId.Contains(neibor))
                    {
                        if (nodeDict[neibor].y > nodeDict[upPoint].y && nodeDict[neibor].x == nodeDict[upPoint].x)
                        {
                            value.siteUp = neibor;
                            value.deadEndDir |= 1;
                        }
                        // break;
                    }
                }
                var downPoint = value.insideDownPoint;
                foreach (var neiborNode in nodeDict[downPoint].rawNeighBor)// nodeDict[downPoint].neighBor)
                {
                    var neibor = neiborNode.id;
                    if (_highwaySiteId.Contains(neibor))
                    {
                        if (nodeDict[neibor].y < nodeDict[downPoint].y && nodeDict[neibor].x == nodeDict[downPoint].x)
                        {
                            value.siteDown = neibor;
                            value.deadEndDir |= 2;
                        }
                        // break;
                    }
                }
                if (value.deadEndDir == 3)
                {
                    value.type = SECTOR_TYPE.TwoWay_SHELF;
                    nodeDict[value.siteUp].highwayConnectSectorIds.Add(key);
                    nodeDict[value.siteDown].highwayConnectSectorIds.Add(key);
                }
                else
                {
                    value.type = SECTOR_TYPE.DeadEnd_SHELF; // 断头路

                    if (value.deadEndDir == 1)
                    {
                        nodeDict[value.siteUp].highwayConnectSectorIds.Add(key);  // 如果上面存在出口，上面的node中加入Sector
                        if (/*nodeDict[downPoint].x < HIDDEN_LINE_X &&*/ value.insideNode.Count > 1) 
                            nodeDict[downPoint].canHidden = 1;//hiddenNode.Add(downPoint);  // 下面点为hiddenNode
                    }
                    if (value.deadEndDir == 2)
                    {
                        nodeDict[value.siteDown].highwayConnectSectorIds.Add(key);   // 如果下面存在出口，下面的node中加入Sector
                        if (/*nodeDict[upPoint].x < HIDDEN_LINE_X &&*/ value.insideNode.Count > 1) 
                            nodeDict[upPoint].canHidden = 1;//hiddenNode.Add(upPoint);  // 上面点位为hiddenNode
                    }

                }

                var sample = value.NodeList[0];
                //if (sample.x > DELIVER_LINE_X)
                //     value.type = SECTOR_TYPE.DELIVERY; // 更新DELIVERY

                if (sample.type == NODE_TYPE.SHELF)
                    value.isShelf = true;
                else value.isShelf = false;

                var sampleSite = SimpleLib.GetSite(sample.id);
                if (sampleSite.fields.ContainsKey("storage") && sampleSite.fields["storage"] == "true")
                    value.isStorage = true;
                else value.isStorage = false;


                if (value.NodeList.Count > 0)
                    value.layout = sample.layout;
            }
            // 更新TrackList
            foreach (var track in trackDict.Values)
            {
                if (sectorDict.ContainsKey(track.nodeA.sectorId) == false || sectorDict.ContainsKey(track.nodeB.sectorId) == false) continue;
                var sectorA = sectorDict[track.nodeA.sectorId];
                var sectorB = sectorDict[track.nodeB.sectorId];

                if (track.nodeA.sectorId != track.nodeB.sectorId)
                {
                    if (sectorA.type == SECTOR_TYPE.TwoWay_SHELF)
                    {
                        if (sectorA.TrackList.Contains(track) == false)
                            sectorA.TrackList.Add(track);
                    }
                    if (sectorB.type == SECTOR_TYPE.TwoWay_SHELF)
                    {
                        if (sectorB.TrackList.Contains(track) == false)
                            sectorB.TrackList.Add(track);
                    }
                }
                else
                {
                    var sector = sectorDict[track.nodeB.sectorId];
                    if (sector.TrackList.Contains(track) == false)
                        sector.TrackList.Add(track);
                }
            }
            /*
            // 最左侧竖条修改track方向，建立小回环
            foreach (var sector in sectorDict.Values)
            {
                if (sector.type != SECTOR_TYPE.TwoWay_SHELF) continue;
                // Diagnosis.Post(" siteDown: " + sector.siteDown + " siteUp: " + sector.siteUp);
                if (_initHighwayList.Contains(sector.siteDown) == false || _initHighwayList.Contains(sector.siteUp) == false)
                    continue;

                var upId = _initHighwayList.IndexOf(sector.siteUp);
                var downId = _initHighwayList.IndexOf(sector.siteDown);

                // 1为从左到右边，2为从右到左
                if (_highwayDirList[upId] == 1 || _highwayDirList[downId] == 2)
                    sector.changeDir(1); // 向上
                else if (_highwayDirList[upId] == 2 || _highwayDirList[downId] == 1)
                    sector.changeDir(2); // 向下
                else sector.changeDir(0); // 双向
            }
            */
            // 开始构建keyPoint
            foreach (Node node in nodeDict.Values)
            {
                if (node.x >= DELIVER_LINE_X && node.x <= MAIN_LINE_X)
                {
                    var site = SimpleLib.GetSite(node.id);
                    var du = site.relatedTracks.Count;
                    if (du >= 3) keyPointNode.Add(node.id);
                }
                else if (_highwaySiteId.Contains(node.id) && node.x < DELIVER_LINE_X)
                {
                    var flag = false;
                    foreach (var nei in node.neighBor)
                    {
                        if (nodeDict[nei].type == NODE_TYPE.NORMAL)
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (flag == true) keyPointNode.Add(node.id);
                }
            }
            foreach (var sector in sectorDict.Values)
            {
                if (nodeDict.ContainsKey(sector.insideUpPoint) && keyPointNode.Contains(sector.insideUpPoint) == false 
                    && nodeDict[sector.insideUpPoint].x < DELIVER_LINE_X)
                    keyPointNode.Add(sector.insideUpPoint);
                if (nodeDict.ContainsKey(sector.insideDownPoint) && keyPointNode.Contains(sector.insideDownPoint) == false
                    && nodeDict[sector.insideDownPoint].x < DELIVER_LINE_X)
                    keyPointNode.Add(sector.insideDownPoint);
            }
            /*
            foreach (Node node in nodeDict.Values)
            {
                if (keyPointNode.Contains(node.id))
                    Diagnosis.Post("keyPointId: " + node.id);
            }
            */
            /*
            foreach (var highwayId in _highwaySiteId)
            {
                var curNode = nodeDict[highwayId];
                if (curNode.x >= DELIVER_LINE_X) continue;
                if (curNode.x <= DELIVER_LINE_X && curNode.type == NODE_TYPE.NORMAL)
                {
                    keyPointNode.Add(highwayId);
                    continue;
                }
            }
            */
            // 更新crossId
            // HashSet<int> CossIds = new HashSet<int>();
            foreach (KeyValuePair<int, Node> kvp in nodeDict)
            {
                var node = kvp.Value;
                node.crossId = 10 * node.layout + Math.Abs((int)(node.y / CrossAreaDeltaY));
                /*if (CossIds.Contains(node.crossId) == false)
                {
                    Diagnosis.Post("layout " + node.layout + " end: " + (int)(node.y / CrossAreaDeltaY) + " hash: " + node.crossId);
                    CossIds.Add(node.crossId);
                }*/
            }
            // Diagnosis.Post("crossNum: " + CossIds.Count);
            // checkGenerate();
        }
        private List<int> generateInitHighWayList()
        {
            var list = new List<int>();
            var keyYList = new List<int>();
            foreach (KeyValuePair<int, Node> kvp in nodeDict)
            {
                var node = kvp.Value;
                if(node.type == NODE_TYPE.FAST && node.x < DELIVER_LINE_X)
                {
                    if (keyYList.Contains(node.y) == false)
                        keyYList.Add(node.y);
                }
            }
            foreach (KeyValuePair<int, Node> kvp in nodeDict)
            {
                var node = kvp.Value;
                if (node.x == MinX && keyYList.Contains(node.y) == true)
                    list.Add(node.id);
            }
            return list;
        }
        private List<int> generateHighwayDir(List<int> initHighwayList)
        {
            var list = new List<int>();
            foreach (var first in initHighwayList)
            {
                var firstNode = nodeDict[first];
                var flag = 0;
                foreach (var track in SimpleLib.GetAllTracks())
                {
                    var siteA = track.siteA; var siteB = track.siteB;
                    var nodeA = nodeDict[siteA]; var nodeB = nodeDict[siteB];
                    if (nodeA.y != nodeB.y || nodeA.y != firstNode.y) continue;
                    if (track.direction >= 1 && track.direction <= 2)
                    {
                        if (nodeA.x < nodeB.x) list.Add(track.direction);
                        else list.Add(3 - track.direction);
                        flag = 1;
                        break;
                    }
                }
                if (flag == 0) list.Add(0);
            }
            return list;
        }
        private int generateNextHighwayId(int nowId)
        {
            var node = SimpleLib.GetSite(nowId);
            var trackList = node.relatedTracks;
            foreach (var trackId in trackList)
            {
                var track = SimpleLib.GetTrack(trackId);
                var nexId = track.GetOther(nowId);
                var neighbor = SimpleLib.GetSite(nexId);
                if (neighbor.y == node.y && neighbor.x > node.x) return neighbor.id;
            }
            return -1;
        }
        private void createEmptySector()  // 创建空EmptySector
        {
            if (sectorDict.ContainsKey(sectorSize) == false)
            {
                var isector = new Sector(sectorSize, SECTOR_TYPE.TwoWay_SHELF); // 默认缺省值为TwoWaySHELF
                sectorDict.Add(sectorSize, isector);
            }
        }
        private void checkGenerate()
        {
            Diagnosis.Post(" SectorGraph has been GENERATED successful!  ");
            var str = " posNum: " + nodeSize + " secNum: " + sectorSize + " traNum: " + trackSize;
            Diagnosis.Post(str);

            foreach (KeyValuePair<int, Sector> kvp in sectorDict)
            {
                var key = kvp.Key;
                var value = kvp.Value;
                foreach(var item in value.NodeList)
                {
                    str = " secId: " + key + " secItem: " + item.id + " x: " + item.x + " y: " + item.y + " type: " + item.type + " lay: " + item.layout + " corssId: " + item.crossId;
                    Diagnosis.Post(str);
                }
                
                str = " secId: " + key + " secSize: " + value.NodeList.Count + " secType: " + value.type + " secUp: " + value.siteUp + " secDown: " + value.siteDown + 
                    " insideUp: " + value.insideUpPoint + " insideDown: " + value.insideDownPoint + " isShelf: " + value.isShelf +" lay: " + value.layout;
                Diagnosis.Post(str);
            }
        }
        public void checkReservation()
        {
            if (ISLARGEGRAPH == false)
            {
                foreach (var pair in sectorDict)
                {
                    var sector = pair.Value;
                    if (sector.reservationPoint != -1)
                    {
                        var str = "reservation SecID: " + pair.Key;
                        str += " Point: " + sector.reservationPoint + " Car: ";

                        foreach (var car in sector.reservationCar)
                            str += car + ", ";
                        Diagnosis.Post(str);
                    }
                }
            }
        }
        public int findTrackId(int siteA, int siteB)
        {
            foreach(var trackId in nodeDict[siteA].neighBorTrack)
            {
                if (trackDict[trackId].getOther(siteA) == siteB) return trackId;
            }
            return -1;
        }
        /*
        public void updateTaskMessage()
        {
            foreach (var agentId in AgentManager.agentList.Keys)
            {
                var agent = AgentManager.agentList[agentId];
                if (agent.State == carState.FREE || agent.State == carState.CHARGE) continue;
                var taskId = agent.TaskId;
                var task = TaskManager.getTask(taskId);
                var shelfNode = nodeDict[task.getShelf()];
                var shelfSector = sectorDict[shelfNode.sectorId];
                
                // if (shelfSector.AgentList.Contains(agentId) == false) shelfSector.AgentList.Add(agentId);

                // shelfNode.taskId = taskId;
            }
        }
        */
        private void clearSectorLocks()
        {
            foreach(var pair in sectorDict)
            {
                var value = pair.Value;
                value.isLocked = 0;
                value.beLockedCar = -1;
            }
        }
        private void clearActiveAgentIds()
        {
            foreach (var pair in sectorDict)
            {
                var sector = pair.Value;
                sector.ActiveAgentId.Clear();
            }
        }
        private void clearCommandLockSite()
        {
            foreach(var site in CommandLockSites)
            {
                nodeDict[site].commandLockAgent = -1;
            }
            foreach(var site in PendingLockSites)
            {
                nodeDict[site].pendingLockAgent = -1;
            }
            CommandLockSites.Clear();
            PendingLockSites.Clear();
            inv_portId2AgentId.Clear();
        }
        // 判断从curPoint到nexPoint 是进入SHELF还是离开SHELF
        // 1为进入，2为离开，0为hold，-1为不相关
        public void Init()
        {
            clearCommandLockSite();       // 清空锁格子状态
            clearSectorLocks();
            clearActiveAgentIds();
            updateBusyCost();         // 更新Graph BusyCost
        }
        public int isInOutOrStaySector(int curPointId, int nexPointId)    
        {
            Node nexPoint = nodeDict[nexPointId];
            Node curPoint = nodeDict[curPointId];
            
            if (nexPoint.neighBor.Contains(curPoint.id) == false) return -1;

            Sector nexSector = sectorDict[nodeId2SectorId[nexPointId]];
            Sector curSector = sectorDict[nodeId2SectorId[curPointId]];
            if (curSector.id == nexSector.id) return 0;
            if (nexSector.type == SECTOR_TYPE.HIGHWAY) return 2;
            // 如果下一步不是SHELF，则是从SHELF离开，返回2
            else if(curSector.type == SECTOR_TYPE.HIGHWAY) return 1;
            return -1;
        }
        public void printSectorLockState()
        {
            var str = "lockSector, ";
            foreach(var pair in sectorDict)
            {
                var key = pair.Key;
                var value = pair.Value;
                if(value.isLocked == 1) Diagnosis.Post(str + " " + key);
            }
        }
        public void updateBusyCost()
        {
            foreach (var node in nodeDict.Values)
            {
                node.occupiedThisTime = 0;
            }
            var agentList = AgentManager.agentList.Values;
            foreach (var agent in agentList)
            {
                var curPointId = agent.SiteID;
                var curPoint = nodeDict[curPointId];
                curPoint.occupiedThisTime = 1;
                curPoint.busyCost++;
            }
            foreach (var pair in nodeDict)
            {
                var node = pair.Value;
                if (node.occupiedThisTime == 0) node.busyCost = 0;
            }
        }

        // bug fix
        public bool checkBlockTheHighway(int curPointId, int agentId)
        {
            var agent = AgentManager.agentList[agentId];
            var pointId = agent.SiteID;
            if (nodeId2SectorId.ContainsKey(pointId) == false) return false;
            var sectorId = nodeId2SectorId[pointId];
            var sector = sectorDict[sectorId];
            if (sector.type != SECTOR_TYPE.DeadEnd_SHELF) return false;
            foreach(var otherAgent in AgentManager.getAllAgents())
            {
                var point = -1;
                if (otherAgent.SiteID == sector.siteDown)
                {
                    point = sector.siteDown;
                }
                if (otherAgent.SiteID == sector.siteUp)
                {
                    point = sector.siteUp;
                }
                if (point == -1) continue;
                if (otherAgent.haveGoals() == false) continue;
                var target = otherAgent.CurrentGoal;
                if (sector.insideNode.Contains(target) == true || sector.siteUp == target || sector.siteDown == target) return true;
            }
            return false;
        }
    }
    public class Track
    {
        public int id;  // 边的Id
        public int x;   // 边的顶点x
        public int y;   // 边的顶点y
        public int direction; // 边的方向, 0为双向，1为x->y，2为y->x, 3为不可通行
        public Node nodeA;
        public Node nodeB;

        public Track() { }
        public Track(int id, int x, int y, int dir)
        {
            this.id = id;
            this.x = x;
            this.y = y;
            this.direction = dir;
        }
        public int getOther(int siteA)
        {
            if (siteA == x) return y;
            else if (siteA == y) return x;
            return -1;
        }
    }
    public class Sector
    {
        public Sector() { }
        public Sector(int id, SECTOR_TYPE type)
        {
            this.id = id;
            this.type = type;
            this.NodeList = new List<Node>();
            this.reservationCar = new HashSet<int>();
            this.reservationPoint = -1;
            this.isLocked = 0;
            this.beLockedCar = -1;
            // this.AgentList = new HashSet<int>();
            this.executingTaskType = ExecutingTask_Type.DEFAULT;
            this.executingTaskNum = 0;
            this.ActiveAgentId = new HashSet<int>();
            this.isCharged = false;
            this.isShelf = false;
            this.isStorage = false;
            this.layout = -1;
            this.insideUpPoint = -1;
            this.insideDownPoint = -1;
            this.TrackList = new List<Track>();
        }
        public enum SECTOR_TYPE
        {
            TwoWay_SHELF,   // 两头出口货架
            DeadEnd_SHELF,  // 断头货架
            HIGHWAY,        // 高速路
            DELIVERY        // 最右侧出货口道路
        }

        // 将任务类型表示为 IN and OUT
        public enum ExecutingTask_Type
        {
            IN = 0,
            OUT = 1,
            DEFAULT = 2
        }

        public int id;  // sector的编号ID
        public List<Node> NodeList;
        public List<Track> TrackList;
        public List<int> insideNode { get { return NodeList.Select(node => node.id).ToList(); } }  // 内含node的id
        // public HashSet<int> AgentList;  // 该 sector 正在执行任务的AgentList
        /*public int AgentNum
        {
            get { return AgentList.Count; }
        }
        */
        public int layout;
        public SECTOR_TYPE type;
        public bool isShelf;
        public bool isCharged;  // 内部是否有充电店
        public bool isStorage;  // 是否为Storage储存sector

        public int siteUp;//上面的高速路点
        public int siteDown;//下面的高速路点
        public int insideUpPoint;//内部最靠上面的点
        public int insideDownPoint;//内部最靠下面的点
        public int deadEndDir; // 1表示上方存在出口，2表示下方存在出口，3表示均存在出口

        public HashSet<int> reservationCar;  // 对于Sector，当前存在的Car
        public int reservationPoint;
        public HashSet<int> ActiveAgentId;   // 仅仅考虑SHELF，当前sector中活跃的Agent编号

        public ExecutingTask_Type executingTaskType;  
        public int executingTaskNum;
        public int isLocked;    // 是否被锁住，不允许其他车辆进入
        public int beLockedCar; // 被锁住的小车ID，只允许该小车随意进出.-1表示不存在
        public void updateLayout()
        {
            if (NodeList.Count == 0) return;
            layout = NodeList[0].layout;
        }
        public void getSectorInfo()
        {
            String str = "";
            str += "InsideNode.Count: " + insideNode.Count + "\n";
            Diagnosis.Post(str);
        }

        public void changeDir(int dir)
        {
            // 1向上 2向下
            foreach (var track in TrackList)
            {
                var nodeA = track.nodeA;
                var nodeB = track.nodeB;
                if (nodeA.x != nodeB.x) continue;
                if (dir == 0) track.direction = 0;
                else if(dir == 1)
                {
                    if (nodeA.y < nodeB.y) track.direction = 1;
                    else track.direction = 2;
                }
                else if(dir == 2)
                {
                    if (nodeA.y < nodeB.y) track.direction = 2;
                    else track.direction = 1; 
                }
            }
        }

        public void generateInsideUpAndDownPoint()
        {
            if (NodeList.Count == 0) return;
            var upNode = NodeList[0];
            var downNode = NodeList[0];
            
            foreach(var node in NodeList)
            {
                if (node.y > upNode.y) upNode = node;
                if (node.y < downNode.y) downNode = node;
            }
            insideUpPoint = upNode.id;
            insideDownPoint = downNode.id;
        }
        public Node generateUpPoint(List<Node> cargoList)
        {
            var upNode = cargoList[0];
            foreach (var node in cargoList)
            {
                if (node.y > upNode.y) upNode = node;
            }
            return upNode;
        }
        public Node generateDownPoint(List<Node> cargoList)
        {
            var downNode = cargoList[0];
            foreach (var node in cargoList)
            {
                if (node.y < downNode.y) downNode = node;
            }
            return downNode;
        }
        public int getDoor()
        {
            if (deadEndDir == 1 || deadEndDir == 3) return siteUp;
            else if (deadEndDir == 2) return siteDown;
            else throw new Exception("Value of deadEndDir should be in [1, 2, 3]");
        }
        public List<Node> getCargoIndexList()  // 货物分布
        {
            List<Node> cargoIndexList = new List<Node>();
            for (int i = 0; i < NodeList.Count; i++)
            {
                var site = SimpleLib.GetSite(insideNode[i]);
                if (site.tags.Contains("Goods"))
                {
                    cargoIndexList.Add(NodeList[i]);
                }
            }
            return cargoIndexList;
        }

        public void delExecutingTask(ExecutingTask_Type type)
        {
            if (type != executingTaskType)
                new Exception("Type is not equal to InputType, Input: " + type + " executing: " + executingTaskType + " sectorID: " + id);
            executingTaskNum--;
            if (executingTaskNum == 0) executingTaskType = Sector.ExecutingTask_Type.DEFAULT;
        }
        public void addExecutingTask(ExecutingTask_Type type)
        {
            if (type != executingTaskType)
                new Exception("Type is not equal to InputType, Input: " + type + " executing: " + executingTaskType + " sectorID: " + id);
            if (executingTaskNum == 0) executingTaskType = type;
            executingTaskNum++;
        }
        public bool checkOtherActiveAgent(int agentId)  // 1表示存在，0表示不存在
        {
            foreach (var id in ActiveAgentId)
            {
                if (id != agentId) return true;
            }
            return false;
        }
        public bool checkConflictAgent2Point(int agentId, int PointId)
        {
            var nowAgent = AgentManager.getAgent(agentId); // 需要判断的nowAgent
            var nowSiteId = nowAgent.SiteID;
            if (insideNode.Contains(nowSiteId) == false) return false;

            foreach(var id in ActiveAgentId)
            {
                if(id == agentId) continue;
                var otherSiteId = AgentManager.getAgent(id).SiteID;
                var nowSite = SimpleLib.GetSite(nowSiteId);
                var otherSite = SimpleLib.GetSite(otherSiteId);
                var Point = SimpleLib.GetSite(PointId);

                if (nowSite.y <= otherSite.y && otherSite.y <= Point.y) return true;
                if (nowSite.y >= otherSite.y && otherSite.y >= Point.y) return true;
            }
            return false;
        }
        public Node generateNextDownPoint(Node node)
        {
            foreach(var neibor in node.rawNeighBor)
            {
                if (neibor.y < node.y && neibor.x == node.x) return neibor;
            }
            return null;
        }
        public Node generateNextUpPoint(Node node)
        {
            foreach (var neibor in node.rawNeighBor)
            {
                if (neibor.y > node.y && neibor.x == node.x) return neibor;
            }
            return null;
        }
        public List<int> generateValidTarget(carState state)
        {
            var validTarget = new List<int>();
            var cargoList = getCargoIndexList();
            if (state == carState.PICKUP)   // 取货
            {
                if (cargoList == null || cargoList.Count == 0) return validTarget;
                var upGoodPoint = generateUpPoint(cargoList);
                var downGoodPoint = generateDownPoint(cargoList);
                if (siteDown != -1 && downGoodPoint.taskIds.Count > 0)
                    validTarget.Add(downGoodPoint.id);
                if (siteUp != -1 && upGoodPoint.taskIds.Count > 0) 
                    validTarget.Add(upGoodPoint.id);
            }
            else if (state == carState.WORK)    // 放货
            {
                if (isCharged == true)
                {
                    foreach (var node in NodeList)
                        if (node.isCharged == true)
                            cargoList.Add(node);
                }

                if (cargoList.Count == 0)
                {
                    if (siteDown != -1 && siteUp != -1)
                    {
                        int middleIndex = insideNode.Count / 2;//计算中间索引号
                         if (insideNode.Count % 2 == 0)  
                         {  
                            return insideNode.GetRange(middleIndex - 1, 2); // 返回中间两个元素  
                         }  
                         else  
                         {  
                             return new List<int> { insideNode[middleIndex] }; // 返回中间一个元素  直接返回insideNode[middleIndex]数据类型不对
                        }
                    }
                    else
                    {
                        if (siteDown != -1) validTarget.Add(insideUpPoint); // 下方有出口，应当放在最上方
                        if (siteUp != -1) validTarget.Add(insideDownPoint);
                    }
                }
                else
                {
                    var upGoodPoint = generateUpPoint(cargoList);
                    var downGoodPoint = generateDownPoint(cargoList);
                    var validDownPoint = generateNextDownPoint(downGoodPoint);
                    var validUpPoint = generateNextUpPoint(upGoodPoint);
                    if (siteDown != -1 && validDownPoint != null) validTarget.Add(validDownPoint.id); 
                    if (siteUp != -1 && validUpPoint != null) validTarget.Add(validUpPoint.id);
                }
            }
            return validTarget;
        }

        // 检修区相关
        public bool isDisabled
        {
            get
            {
                foreach (var node in NodeList)
                {
                    if (node.isDisabled == true) return true;
                }
                return false;
            }
        }

    }
    public class Node
    {
        public Node() { }
        public Node(int id, NODE_TYPE type)
        {
            this.id = id;
            this.type = type;
            this.x = (int)SimpleLib.GetSite(id).x; 
            this.y = (int)SimpleLib.GetSite(id).y;
            this.neighBor = new HashSet<int>();
            this.rawNeighBor = new HashSet<Node>();
            this.highwayConnectSectorIds = new List<int>();
            this.canHidden = 0;
            this.hiddenCar = -1;
            this.taskIds = new List<string>();
            this.busyCost = 0;
            this.occupiedThisTime = 0;
            this.isCharged = false;
            this.isHoister = false;
            this.layout = SimpleLib.GetSite(id).layerName[0] - '0';
            this.crossId = -1;
            this.gateId = -1;
            this.inv_gateId = -1;

        }

        public enum NODE_TYPE
        {
            BLOCK,      // 地图上不存在的点位
            SHELF,      // 可能出现货架的点位
            FAST,       // 高速路点
            NORMAL      // 非高速路的灰点，与普通的shelf区别在于一定不会存在货架
        }
        public int id;  // 点的编号ID
        public NODE_TYPE type;  // 节点的类型

        public int sectorId;  //  所属的sector编号
        public HashSet<int> neighBor;  // 相邻节点
        public HashSet<Node> rawNeighBor; // 原始的neibor 所有路均为双向 不考虑Block
        
        public HashSet<int> neighBorTrack;

        public int x;
        public int y;
        public int layout;
        public int crossId;
        public List<int> highwayConnectSectorIds;  // 对于高速路和断头路相连的节点，得需要知道相连的sectorId列表

        public int canHidden;   // 该点能否躲藏，0为不行，1为可以; 表示为一种本身能力，不会因为已经被小车占据而改变
        public int hiddenCar;   // 该点目前是否有小车占据，如果没有则为-1，如果存在则为小车编号

        public List<string> taskIds; // 该点的taskId列表，对于port而言是一个list，对于shelf而言长度一定为1

        public int busyCost;    // 连续的busy长度
        public int occupiedThisTime;    // 实际物理意义上的当前时刻该点是否被小车占据
        public int commandLockAgent;    // 与CommandLockSites对应，当前点被占据的小车id
        public int pendingLockAgent;    

        public bool isCharged;   // 是否是充电点
        public bool isHoister;
        public int isPortAndType; // 0 表示不为port 1表示in 2表示out
        public int gateId; // site节点的gate属性 如果为-1则表示不存在
        public int inv_gateId; // port节点对应到的路径点 

        public void addNeighbor(int neiborId, int trackId)
        {
            if (neighBor == null) neighBor = new HashSet<int>();
            if (neighBor.Contains(neiborId)) return;
            neighBor.Add(neiborId);

            if(neighBorTrack == null) neighBorTrack = new HashSet<int>();
            if (neighBorTrack.Contains(trackId)) return;
            neighBorTrack.Add(trackId);

        }


        // 检修区相关
        public bool isDisabled
        {
            get {
                var site = SimpleLib.GetSite(id);
                if (site.fields.ContainsKey("disabled") && (site.fields["disabled"] == "trye" || site.fields["disabled"] == "true"))
                    return true;
                return false;
            }
        }
    }
}
