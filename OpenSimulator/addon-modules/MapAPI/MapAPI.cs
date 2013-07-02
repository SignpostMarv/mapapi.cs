using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;

using OpenSim.Server.Base;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;

using Diva.Utils;

namespace SignpostMarv.OpenSim
{
	class MapAPIConnector : ServiceConnector
	{
        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Config section name
        /// </summary>
        private const string m_ConfigName = "MapAPI";

        /// <summary>
        /// Sets up the handlers
        /// </summary>
        /// <param name="config"></param>
        /// <param name="server"></param>
        /// <param name="configName"></param>
        public MapAPIConnector(IConfigSource config, IHttpServer server,
            string configName) : base(config, server, configName){
            IConfig serverConfig = config.Configs[m_ConfigName];
            if(serverConfig == null)
                throw new Exception(string.Format("No section {0} in config file", m_ConfigName));

            bool enableWebSocketAPI = serverConfig.GetBoolean(
                    "EnableWebSocketAPI", false);

            bool enableHTTPAPI = serverConfig.GetBoolean(
                    "EnableHTTPAPI", false);

            if(!enableHTTPAPI && !enableHTTPAPI)
                throw new Exception("All APIs disabled.");

            if(enableHTTPAPI)
                server.AddStreamHandler(new MapAPIHTTPHandler());

            if(enableWebSocketAPI)
                server.AddWebSocketHandler("/mapapi", MapAPIWebSocketHandler.Init);
        }
	}

    class MapAPIHTTPHandler : BaseStreamHandler
    {
        public MapAPIHTTPHandler() : base("GET", "/mapapi")
        {
        }

        public override byte[] Handle(string path, Stream requestData,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            string resource = Uri.UnescapeDataString(GetParam(path)).Trim(
                    WebAppUtils.DirectorySeparatorChars);

            httpResponse.ContentType = "application/json";

            return WebAppUtils.StringToBytes(string.Empty);
        }
    }

    class MapAPIWebSocketHandler
    {
        
        // This gets called by BaseHttpServer and gives us an opportunity to set things on the WebSocket handler before we turn it on
        public static void Init(string path, WebSocketHttpServerHandler handler)
        {

        }
    }
}