using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;

using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using Diva.Utils;

namespace SignpostMarv.OpenSim
{
    public class MapAPI
    {
        /// <summary>
        /// Config section name
        /// </summary>
        private const string m_ConfigName = "MapAPI";

        public static void Init(IConfigSource config, IHttpServer server)
        {
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

	public class MapAPIConnector : ServiceConnector
	{
        /// <summary>
        /// Logger
        /// </summary>
        public static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Sets up the handlers
        /// </summary>
        /// <param name="config"></param>
        /// <param name="server"></param>
        /// <param name="configName"></param>
        public MapAPIConnector(IConfigSource config, IHttpServer server,
                string configName) : base(config, server, configName){
            MapAPI.Init(config, server);
        }
	}

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "LocalMapAPIConnector")]
    public class LocalMapAPIConnector : ISharedRegionModule
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        public LocalMapAPIConnector()
        {
            m_log.Debug("[LOCAL MAPAPI CONNECTOR]: LocalMapAPIConnector no params");
        }

        public LocalMapAPIConnector(IConfigSource config)
        {
            m_log.Debug("[LOCAL MAPAPI CONNECTOR]: LocalMapAPIConnector instantiated directly");
            Initialise(config);
        }

        #region ISharedRegionModule

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "LocalMapAPIConnector"; }
        }

        public void Initialise(IConfigSource config)
        {
            MapAPI.Init(config, MainServer.Instance);
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
        }

        #endregion
    }

    public class MapAPIHTTPHandler : BaseStreamHandler
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

    public class MapAPIWebSocketHandler
    {
        
        // This gets called by BaseHttpServer and gives us an opportunity to set things on the WebSocket handler before we turn it on
        public static void Init(string path, WebSocketHttpServerHandler handler)
        {

        }
    }
}