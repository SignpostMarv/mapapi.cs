using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Server.Handlers.Base;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using Diva.Utils;

// Our plugin loader needs the information about our module
[assembly: Addin("MapAPI.cs", "0.1")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace SignpostMarv.OpenSim
{
    public class MapAPI
    {
        /// <summary>
        /// Config section name
        /// </summary>
        private const string m_ConfigName = "MapAPI";

        public static bool enableWebSocketAPI { get; private set; }

        public static bool enableHTTPAPI { get; private set; }

        public static string mapImageServerURI { get; private set; }



        public static void Init(IConfigSource config, IHttpServer server)
        {
            IConfig serverConfig = config.Configs[m_ConfigName];
            if (serverConfig == null)
                throw new Exception(string.Format("No section {0} in config file", m_ConfigName));

            enableWebSocketAPI = serverConfig.GetBoolean(
                    "EnableWebSocketAPI", false);

            enableHTTPAPI = serverConfig.GetBoolean(
                    "EnableHTTPAPI", false);

            #region Adapted from MapImageServicesConnector.Initialise

            IConfig mapConfig = config.Configs["MapImageService"];
            if (config == null)
            {
                throw new Exception("MapImage connector init error");
            }

            string serviceURI = mapConfig.GetString("MapImageServerURI",
                    String.Empty);

            if (serviceURI == String.Empty)
            {
                mapImageServerURI =
                    (server.UseSSL ? "https" : "http") + "://" +
                    (server.UseSSL ? server.SSLCommonName : "localhost") + ":" +
                    (server.UseSSL ? server.SSLPort : server.Port).ToString();
            }
            else
            {
                mapImageServerURI = serviceURI.TrimEnd('/');
            }

            #endregion

            if (!enableHTTPAPI && !enableHTTPAPI)
                throw new Exception("All APIs disabled.");

            if (enableHTTPAPI)
                server.AddStreamHandler(new MapAPIHTTPHandler(enableHTTPAPI, enableWebSocketAPI));

            if (enableWebSocketAPI)
                server.AddWebSocketHandler("/mapapi", MapAPIWebSocketHandler.Init);
        }


        public static OSDString pos2region(uint x, uint y)
        {
            Scene scene;
            if (SceneManager.Instance.TryGetScene(x, y, out scene))
                return (OSDString)scene.RegionInfo.RegionName;
            else
                return (OSDString)string.Empty;
        }

        public static OSDMap region2pos(string name)
        {
            Scene scene;
            OSDMap result;
            if (SceneManager.Instance.TryGetScene(name, out scene))
            {
                result = new OSDMap(new Dictionary<string, OSD>(){
                    {"x", scene.RegionInfo.RegionLocX},
                    {"y", scene.RegionInfo.RegionLocY},
                    {"region", scene.RegionInfo.RegionName}
                });
            }
            else
            {
                result = new OSDMap(new Dictionary<string, OSD>(){
                    {"error", "Could not find region with specified name."}
                });
            }

            return result;
        }

        public static OSDMap config()
        {
            return new OSDMap(new Dictionary<string, OSD>(){
                {"config", new OSDMap(new Dictionary<string ,OSD>(){
                    {"HTTP", enableHTTPAPI},
                    {"WebSocket", enableWebSocketAPI},
                    {"MapImageURI", mapImageServerURI}
                })}
            });
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
                string configName)
            : base(config, server, configName)
        {
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
        private bool m_HTTPEnabled = false;
        private bool m_WebSocketEnabled = false;

        public MapAPIHTTPHandler(bool HTTP, bool WebSocket)
            : base("GET", "/mapapi")
        {
            m_HTTPEnabled = HTTP;
            m_WebSocketEnabled = WebSocket;
        }

        public override byte[] Handle(string path, Stream requestData,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            string resource = Uri.UnescapeDataString(GetParam(path)).Trim(
                    WebAppUtils.DirectorySeparatorChars);

            OSD result = (OSDString)string.Empty;

            httpResponse.ContentType = "application/json";

            bool region2pos = resource.StartsWith("region2pos/");

            if (resource == string.Empty)
            {
                result = MapAPI.config();
            }
            else if (resource.StartsWith("pos2region/"))
            {
                string[] args = resource.Split('/');
                if (args.Length == 3)
                {
                    uint x;
                    uint y;
                    if (uint.TryParse(args[1], out x) && uint.TryParse(args[2], out y))
                    {
                        result = new OSDMap(new Dictionary<string, OSD>(){
                            {"region", MapAPI.pos2region(x, y)}
                        });
                    }
                    else
                    {
                        result = new OSDMap(new Dictionary<string, OSD>(){
                            {"error", "X & Y arguments should be integers"}
                        });
                    }
                }
                else
                {
                    result = new OSDMap(new Dictionary<string, OSD>(){
                        {"error", args.Length < 3 ?
                            "Insufficient arguments (x/y coords missing)" :
                            "Too many arguments"
                        }
                    });
                }
            }
            else if (resource.StartsWith("region2pos/"))
            {
                string[] args = resource.Split('/');
                if (args.Length == 2)
                {
                    result = MapAPI.region2pos(args[1]);
                }
                else
                {
                    result = new OSDMap(new Dictionary<string, OSD>(){
                        {"error", args.Length < 2 ?
                            "Insufficient arguments (region name missing)" :
                            "Too many arguments"
                        }
                    });
                }
            }

            return WebAppUtils.StringToBytes(OSDParser.SerializeJsonString((OSD)result, true));
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