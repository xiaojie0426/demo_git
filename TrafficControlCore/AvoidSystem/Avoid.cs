/************************************************************************/
/* File:	  Avoid.cs										            */
/* Func:	  管理避让                                                  */
/* Author:	  Han Xingyao                                               */
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

namespace TrafficControlCore.AvoidSystem
{
    public class Avoid
    {
        public readonly int avoidId;
        public readonly int expelAgentId;
        public readonly int blockAgentId;
        public readonly int expelNodeId;
        public readonly int hideNodeId;
        public Avoid(int avoid_id, int expel_node_id, int expel_agent_id, int block_agent_id, int hide_node_id)
        {
            avoidId = avoid_id;
            expelAgentId = expel_agent_id;
            blockAgentId = block_agent_id;
            expelNodeId = expel_node_id;
            hideNodeId = hide_node_id;
        }
        public override string ToString()
        {
            return $"AVOID: {avoidId}, expelAgentId: {expelAgentId}, blockAgentId: {blockAgentId}, expelNodeId: {expelNodeId}, hideNodeId: {hideNodeId}";
        }
    }
}
