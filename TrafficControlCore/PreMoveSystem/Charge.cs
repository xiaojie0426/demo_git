/************************************************************************/
/* File:	  Charge.cs										            */
/* Func:	  管理充电任务                                               */
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
using System.Text.RegularExpressions;
using static System.Net.WebRequestMethods;

namespace TrafficControlCore.DispatchSystem
{
    public class AgvChargeManager
    {
        public List<int> chargeNodeList;
        public List<int> chargeSectorList;
        public List<int> batteryThreshold;
        public Dictionary<int, chargeAgent> chargeAgentList;
        public Dictionary<int, chargePile> chargePileList;
        public SectorGraph Graph
        {
            get { return DispatchMisson.Graph; }
        }
        public AgvChargeManager()
        {
            this.chargeNodeList = new List<int>();
            this.chargeSectorList = new List<int>();
            this.batteryThreshold = new List<int> { 0, 20, 50, 80, 100 };
            this.chargeAgentList = new Dictionary<int, chargeAgent>();
            this.chargePileList = new Dictionary<int, chargePile>();
        }
        public void initList(SectorGraph Graph)
        {
            foreach (var agent in AgentManager.getAllAgents())
                chargeAgentList.Add(agent.Id, new chargeAgent(agent.Id, -1));

            foreach (var site in SimpleLib.GetAllSites())
            {
                if (site.type == 2)
                {
                    var node = Graph.nodeDict[site.id];
                    chargeNodeList.Add(site.id);
                    chargeSectorList.Add(node.sectorId);
                    chargePileList.Add(site.id, new chargePile(site.id, -1));
                    node.isCharged = true;
                    var sector = Graph.sectorDict[node.sectorId];
                    sector.isCharged = true;
                }
            }
            var str = "chargeNodeList: ";
            foreach (var node in chargeNodeList)
            {
                str = str + node + ", ";
            }
            Diagnosis.Post(str);
            str = "chargeSectorList: ";
            foreach (var sec in chargeSectorList)
            {
                str = str + sec + ", ";
            }
            Diagnosis.Post(str);
        }
        public int getResPileNum()
        {
            int num = 0;
            foreach (KeyValuePair<int, chargePile> kvp in chargePileList)
            {
                var pile = kvp.Value;
                if (pile.occupiedAgent == -1) num++;
            }
            return num;
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void gotoFree()
        ///
        /// @brief  对于已经充电完成的agent，将状态从Charge调整为Free
        ///
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void gotoFree()
        {
            foreach (var agent in AgentManager.getAllAgents())
            {
                if (agent.State != carState.CHARGE) continue;
                var battery = agent.getBattery();
                var chargeAgent = chargeAgentList[agent.Id];

                var str = "ID: " + agent.Id + " battery: " + battery + " threshold: " + chargeAgent.threshold;
                Diagnosis.Post(str);

                if (battery >= chargeAgent.threshold)
                {
                    var pile = chargePileList[chargeAgent.pileId];
                    agent.State = carState.FREE;
                    chargeAgent.pileId = -1;
                    chargeAgent.threshold = -1;
                    pile.occupiedAgent = -1;
                    agent.initGoalList();
                }
            }
        }
        public void gotoFree(Agent agent)
        {
            var chargeAgent = chargeAgentList[agent.Id];
            var pile = chargePileList[chargeAgent.pileId];
            agent.State = carState.FREE;
            chargeAgent.pileId = -1;
            chargeAgent.threshold = -1;
            pile.occupiedAgent = -1;
            agent.initGoalList();
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// @fn void gotoCharge()
        ///
        /// @brief  针对场上所有agent，计算充电阈值，选择合适的充电桩和小车进行配对
        ///         将小车状态从Free改为Charge
        /// @TODO   没有考虑不同层之间的充电桩选择
        ///
        /// @author Tan Yuhong
        /// @date   2024-3-26
        /// </summary>        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void gotoCharge()
        {
            List<double> batteryList = new List<double>();
            List<int> idexList = new List<int>();

            var batteryCheck = 0.0;

            foreach (var agent in AgentManager.getAllAgents())
            {
                if (agent.isManual == true) continue;

                var battery = agent.getBattery();
                batteryList.Add(battery);
                idexList.Add(agent.Id);
                batteryCheck += battery;
            }

            batteryCheck /= AgentManager.getAllAgents().Count;

            List<double> sortedBattery = new List<double>(batteryList);
            List<int> battery2Idx = new List<int>();
            var minBattery = 101.0;
            sortedBattery.Sort();
            for (int i = 0; i < sortedBattery.Count; ++i)
            {
                int index = batteryList.IndexOf(sortedBattery[i]);
                battery2Idx.Add(idexList[index]);
                var agent = AgentManager.getAgent(idexList[index]);
                if (agent.State != carState.CHARGE) minBattery = Math.Min(agent.getBattery(), minBattery);
            }
            // var resPileNum = getResPileNum();
            var chargeThreshold = Math.Min(90, minBattery + 20);   // 最低电量 + 20

            var str = "chargeThreshold: " + chargeThreshold;
            Diagnosis.Post(str);
            str = "Average Battery: " + batteryCheck;
            Diagnosis.Post(str);

            // analysis();

            for (int i = 0; i < sortedBattery.Count; ++i)
            {
                if (sortedBattery[i] > chargeThreshold) break;
                int index = battery2Idx[i];
                var agent = AgentManager.getAgent(index);
                if (agent.State != carState.FREE) continue;

                var pileId = getValidPile(agent);
                if (pileId == -1) break;

                var pile = chargePileList[pileId];
                var chargeAgent = chargeAgentList[index];
                chargeAgent.pileId = pile.id;
                chargeAgent.generateThreshold();
                pile.occupiedAgent = chargeAgent.id;
                agent.State = carState.CHARGE;

                if (agent.haveGoals() == false || Graph.nodeDict.ContainsKey(agent.CurrentKeyGoal) == false
                   || Graph.nodeDict[agent.CurrentKeyGoal].isCharged == false)
                {
                    agent.initGoalList();
                    agent.addGoal(ChargeManager.chargeAgentList[agent.Id].pileId, true);
                    // agent.Goals.Enqueue(ChargeManager.chargeAgentList[agent.Id].pileId);
                }

            }
        }
        public bool canReceiveTask(int sectorId)
        {
            foreach (var pile in chargePileList.Values)
            {
                var pileSectorId = Graph.nodeDict[pile.id].sectorId;
                if (pileSectorId != sectorId) continue;
                if (pile.occupiedAgent != -1) return false;
                return true;
            }
            return true;
        }
        public void analysis()
        {
            var twenty = 0;
            var forty = 0;
            var sixty = 0;
            var eighty = 0;
            var hundred = 0;
            foreach (var agent in AgentManager.getAllAgents())
            {
                var battery = agent.getBattery();
                if (battery <= 20) twenty++;
                else if (battery <= 40) forty++;
                else if (battery <= 60) sixty++;
                else if (battery <= 80) eighty++;
                else hundred++;
            }

            var str = "twenty: " + twenty;
            Diagnosis.Post(str);
            str = "forty: " + forty;
            Diagnosis.Post(str);
            str = "sixty: " + sixty;
            Diagnosis.Post(str);
            str = "eighty: " + eighty;
            Diagnosis.Post(str);
            str = "hundred: " + hundred;
            Diagnosis.Post(str);
        }
        public int getValidPile(Agent agent)
        {
            int MinId = NodeFinder.FindNearestNode(Graph, agent, agent.getLastHoldingLockId(), NodeFinder.FINDMODE.Charge);
            return MinId;
            /*
            foreach (KeyValuePair<int, chargePile> kvp in chargePileList)
            {
                if (kvp.Value.occupiedAgent != -1) continue;
                var id = kvp.Key;
                var node = Graph.nodeDict[id];
                var sector = Graph.sectorDict[node.sectorId];
                if (sector.executingTaskNum > 0) continue;
                return kvp.Key;
            }
            return -1;
        */
        }
        public void checkBattery()
        {
            foreach (var car in SimpleLib.GetAllCars())
            {
                var icar = car as DummyCar;
                var bat = icar.getBattery();
                var str = "carId: " + icar.id + " battery: " + bat;
                Diagnosis.Post(str);
            }
        }
    }
    public class chargeAgent
    {
        public chargeAgent(int id = -1, int threshold = -1, int pileId = -1)
        {
            this.id = id;
            this.threshold = threshold;
            this.pileId = pileId;
        }
        public chargeAgent() { }
        public int id;
        public int threshold;
        public int pileId;
        public void generateThreshold()
        {
            var battery = AgentManager.getAgent(id).getBattery();
            threshold = (int)Math.Min(battery + 30.0, 90.0);
            /*
            if (battery < 20) threshold = 50;
            else if (battery < 80) threshold = 80;
            else threshold = 99;
            */
        }
    }

    public class chargePile
    {
        public chargePile(int id = -1, int occupiedAgent = -1)
        {
            this.id = id;
            this.occupiedAgent = occupiedAgent;
        }
        public chargePile() { }
        public int id;
        public int occupiedAgent;
    }

}
