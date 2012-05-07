﻿/*
Copyright (c) 2012 Contributors
See CONTRIBUTORS.md for a full list of copyright holders.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.
    * Neither the name of the OpenSimulator Project nor the
      names of its contributors may be used to endorse or promote products
      derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE
GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR
OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Web;

using BitmapProcessing;

using Nini.Config;

using OpenMetaverse;
using OpenMetaverse.Imaging;
using OpenMetaverse.StructuredData;

using Aurora.Framework;
using Aurora.Framework.Servers.HttpServer;
using Aurora.Simulation.Base;
using RegionFlags = Aurora.Framework.RegionFlags;

using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace Aurora.Services
{
    public class MapAPIHandler : IService
    {
        protected IRegistryCore m_registry;
        private IHttpServer m_textureServer = null;
        private IHttpServer m_server = null;

        protected static string urlMapTexture = "MapAPI_MapTexture";
        protected static string tileCacheDir = "MapAPI_tileCache";

        public string Name
        {
            get { return GetType().Name; }
        }

        #region IService

        public void Initialize(IConfigSource config, IRegistryCore registry)
        {
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
            m_registry = registry;

            IConfig handlerConfig = config.Configs["Handlers"];
            string name = handlerConfig.GetString(Name, "");

            if (name != Name)
            {
                MainConsole.Instance.Warn("[MapAPI] module not loaded");
                return;
            }
            MainConsole.Instance.Info("[MapAPI] module loaded");

            ISimulationBase simBase = registry.RequestModuleInterface<ISimulationBase>();

            m_textureServer = simBase.GetHttpServer(handlerConfig.GetUInt(Name + "TextureServerPort", 8002));
            m_textureServer.AddHTTPHandler(urlMapTexture, OnHTTPGetMapImage);

            m_server = simBase.GetHttpServer(handlerConfig.GetUInt(Name + "Port", 8007));
            m_server.AddStreamHandler(new MapAPIHTTPHandler_GET(this, registry, m_textureServer));

        }

        public void FinishedStartup()
        {
        }

        #endregion

        #region Textures

        // From msdn
        private static ImageCodecInfo GetEncoderInfo(String mimeType)
        {
            ImageCodecInfo[] encoders;
            encoders = ImageCodecInfo.GetImageEncoders();
            for (int j = 0; j < encoders.Length; ++j)
            {
                if (encoders[j].MimeType == mimeType)
                {
                    return encoders[j];
                }
            }
            return null;
        }

        public Hashtable OnHTTPGetMapImage(Hashtable keysvals)
        {
            Hashtable reply = new Hashtable();

            if (keysvals["method"].ToString() != urlMapTexture)
            {
                return reply;
            }

            uint zoom = (keysvals.ContainsKey("zoom")) ? uint.Parse(keysvals["zoom"].ToString()) : 7;
            uint x = (keysvals.ContainsKey("x")) ? (uint)float.Parse(keysvals["x"].ToString()) : 0;
            uint y = (keysvals.ContainsKey("y")) ? (uint)float.Parse(keysvals["y"].ToString()) : 0;

            MainConsole.Instance.Debug("[MapAPI]: Sending map image jpeg");
            int statuscode = 200;
            byte[] jpeg = new byte[0];

            MemoryStream imgstream = new MemoryStream();
            Bitmap mapTexture = CreateZoomLevel(zoom, x, y);
            EncoderParameters myEncoderParameters = new EncoderParameters();
            myEncoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);

            // Save bitmap to stream
            mapTexture.Save(imgstream, GetEncoderInfo("image/jpeg"), myEncoderParameters);

            // Write the stream to a byte array for output
            jpeg = imgstream.ToArray();

            // Reclaim memory, these are unmanaged resources
            // If we encountered an exception, one or more of these will be null
            if (mapTexture != null)
            {
                mapTexture.Dispose();
            }

            if (imgstream != null)
            {
                imgstream.Close();
                imgstream.Dispose();
            }

            reply["str_response_string"] = Convert.ToBase64String(jpeg);
            reply["int_response_code"] = statuscode;
            reply["content_type"] = "image/jpeg";

            return reply;
        }

        private Bitmap CreateZoomLevel(uint zoomLevel, uint regionX, uint regionY)
        {
            if (!Directory.Exists(tileCacheDir))
            {
                Directory.CreateDirectory(tileCacheDir);
            }

            zoomLevel += 1;
            uint regionsPerTileEdge = (uint)Math.Pow(2, zoomLevel - 1);
            regionX -= regionX % regionsPerTileEdge;
            regionY -= regionY % regionsPerTileEdge;

            string fileName = Path.Combine(tileCacheDir, (zoomLevel - 1) + "-" + regionX + "-" + regionY + ".jpg");
            if (File.Exists(fileName))
            {
                DateTime lastWritten = File.GetLastWriteTime(fileName);
                if ((DateTime.Now - lastWritten).Minutes < 10) //10 min cache
                {
                    return (Bitmap)Bitmap.FromFile(fileName);
                }
            }

            int imageSize = 256;
            Bitmap mapTexture = new Bitmap(imageSize, imageSize);
            Graphics g = Graphics.FromImage(mapTexture);
            Color seaColor = Color.FromArgb(29, 71, 95);
            SolidBrush sea = new SolidBrush(seaColor);
            g.FillRectangle(sea, 0, 0, imageSize, imageSize);

            IGridService gridService = m_registry.RequestModuleInterface<IGridService>();

            if (gridService == null)
            {
                MainConsole.Instance.Error("[" + Name + "] Could not find grid service, cannot generate textures");
                return mapTexture;
            }

            uint imageX = regionX * Constants.RegionSize;
            uint imageY = regionY * Constants.RegionSize;
            uint imageTop = (regionY - regionsPerTileEdge) * Constants.RegionSize;

            float tileCenterX = (regionX + ((float)regionsPerTileEdge / (float)2.0)) * Constants.RegionSize;
            float tileCenterY = (regionY + ((float)regionsPerTileEdge / (float)2.0)) * Constants.RegionSize;

            uint squareRange = (Constants.RegionSize / 2) * regionsPerTileEdge;

            List<GridRegion> regions = gridService.GetRegionRange(UUID.Zero, tileCenterX, tileCenterY, squareRange - 1);

            if (regions.Count == 0)
            {
                return mapTexture;
            }

            List<Image> bitImages = new List<Image>();
            List<FastBitmap> fastbitImages = new List<FastBitmap>();

            foreach (GridRegion r in regions)
            {
                AssetBase texAsset = m_registry.RequestModuleInterface<IAssetService>().Get(r.TerrainImage.ToString());

                if (texAsset != null)
                {
                    ManagedImage managedImage;
                    Image image;
                    if (OpenJPEG.DecodeToImage(texAsset.Data, out managedImage, out image))
                    {
                        bitImages.Add(image);
                        fastbitImages.Add(new FastBitmap((Bitmap)image));
                    }
                }
            }

            float regionSizeOnImage = (float)imageSize / (float)regionsPerTileEdge;
            float zoomScale = (imageSize / zoomLevel);

            for (int i = 0; i < regions.Count; i++)
            {
                float width = (bitImages[i].Width * (regions[i].RegionSizeX / bitImages[i].Width)) * ((float)regionSizeOnImage / (float)Constants.RegionSize);
                float height = (bitImages[i].Height * (regions[i].RegionSizeY / bitImages[i].Height)) * ((float)regionSizeOnImage / (float)Constants.RegionSize);

                float tileFactorWidth = (float)bitImages[i].Width / (float)regions[i].RegionSizeX;
                float tileFactorHeight = (float)bitImages[i].Height / (float)regions[i].RegionSizeY;

                float posX = ((((float)regions[i].RegionLocX - (float)imageX)) / Constants.RegionSize) * regionSizeOnImage;
                float posY = ((((float)regions[i].RegionLocY - (float)imageY)) / Constants.RegionSize) * regionSizeOnImage;

                g.DrawImage(bitImages[i], posX, imageSize - posY - height, width, height); // y origin is top
            }

            mapTexture.Save(fileName, ImageFormat.Jpeg);

            return mapTexture;
        }

        #endregion
    }


    public class MapAPIHTTPHandler_GET : BaseStreamHandler
    {
        protected MapAPIHandler m_mapapi;
        protected IRegistryCore m_registry;
        protected IHttpServer m_textureServer;
        protected static string httpPath = "/mapapi";
        private Dictionary<string, MethodInfo> APIMethods = new Dictionary<string, MethodInfo>();

        public MapAPIHTTPHandler_GET(MapAPIHandler mapapi, IRegistryCore reg, IHttpServer textureServer)
            : base("GET", httpPath)
        {
            m_mapapi = mapapi;
            m_registry = reg;
            m_textureServer = textureServer;
            MethodInfo[] methods = this.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (uint i = 0; i < methods.Length; ++i)
            {
                if (methods[i].IsPrivate && typeof(OSD).IsAssignableFrom(methods[i].ReturnType) && methods[i].GetParameters().Length == 1 && methods[i].GetParameters()[0].ParameterType == typeof(string[]))
                {
                    APIMethods[methods[i].Name] = methods[i];
                }
            }
        }

        #region BaseStreamHandler

        public override byte[] Handle(string path, Stream requestData, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            httpResponse.ContentType = "text/plain";

            string methodPath = path.Substring(httpPath.Length).Trim();
            if (methodPath != string.Empty && methodPath.Substring(0, 1) == "/")
            {
                methodPath = methodPath.Substring(1);
            }

            string[] parts = new string[0];
            if (methodPath != string.Empty)
            {
                parts = methodPath.Split('/');
            }
            for (int i = 0; i < parts.Length; ++i)
            {
                parts[i] = HttpUtility.UrlDecode(parts[i]);
            }

            if (parts.Length == 0)
            {
                return new byte[0];
            }

            string method = string.Empty;
            OSD resp = null;
            try
            {
                method = parts[0];
                if (APIMethods.ContainsKey(method))
                {
                    resp = (OSD)APIMethods[method].Invoke(this, new object[1]{ parts });
                }
                else
                {
                    MainConsole.Instance.TraceFormat("[MapAPI] Unsupported method called ({0})", method);
                }
            }
            catch (Exception e)
            {
                MainConsole.Instance.TraceFormat("[MapAPI] Exception thrown: " + e.ToString());
            }
            if (resp == null)
            {
                resp = new OSDBoolean(false);
            }
            UTF8Encoding encoding = new UTF8Encoding();
            httpResponse.ContentType = "application/json";
            return encoding.GetBytes(OSDParser.SerializeJsonString(resp, true));
        }

        #endregion


        #region MapAPI Methods

        private OSDArray MonolithicRegionLookup(string[] parts)
        {
            OSDArray resp = new OSDArray();

            if (parts.Length < 1 || parts[0] != "MonolithicRegionLookup")
            {
                return resp;
            }

            IRegionData regiondata = Aurora.DataManager.DataManager.RequestPlugin<IRegionData>();

            if (regiondata != null)
            {
                List<GridRegion> regions = regiondata.Get(RegionFlags.RegionOnline);
                OSDMap regionResp;
                foreach (GridRegion region in regions)
                {
                    regionResp = new OSDMap(8);

                    regionResp["Name"] = region.RegionName;
                    regionResp["UUID"] = region.RegionID;
                    regionResp["x"] = region.RegionLocX / Constants.RegionSize;
                    regionResp["y"] = region.RegionLocY / Constants.RegionSize;
                    regionResp["z"] = region.RegionLocZ / Constants.RegionSize;
                    regionResp["width"] = region.RegionSizeX;
                    regionResp["height"] = region.RegionSizeY;
                    regionResp["depth"] = region.RegionSizeZ;

                    resp.Add(regionResp);
                }
            }

            return resp;
        }

        private OSDString mapTextureURL(string[] parts)
        {
            OSDString resp = (OSDString)OSD.FromString(string.Empty);

            if (parts.Length < 1 || parts[0] != "mapTextureURL")
            {
                return resp;
            }

            return (OSDString)OSD.FromString( m_textureServer.ServerURI + "/index.php?method=MapAPI_MapTexture&x=_%x%_&y=_%y%_&zoom=_%zoom%_" );
        }

        private OSDMap RegionDetails(string[] parts)
        {
            OSDMap resp = new OSDMap();

            if (parts.Length < 2 || parts[0] != "RegionDetails")
            {
                resp["Error"] = OSD.FromString("Invalid method invokation");
            }
            else
            {
                IRegionData regiondata = Aurora.DataManager.DataManager.RequestPlugin<IRegionData>();
                GridRegion region = null;

                if (regiondata == null)
                {
                    resp["Error"] = OSD.FromString("Could not find IRegionData");
                }
                else
                {
                    UUID scopeID = UUID.Zero;
                    UUID regionID = UUID.Zero;
                    int x;
                    int y;
                    int z=0;
                    if (parts.Length == 2)
                    {
                        bool isUUID = UUID.TryParse(parts[1], out regionID);
                        if (isUUID)
                        {
                            region = regiondata.Get(regionID, scopeID);
                        }
                        else
                        {
                            List<GridRegion> regions = regiondata.Get(parts[1], scopeID);
                            if (regions.Count > 0)
                            {
                                region = regions[0];
                            }
                        }
                    }
                    else if (parts.Length == 3)
                    {
                        bool hasScopeID = UUID.TryParse(parts[1], out scopeID);
                        bool hasRegionID = UUID.TryParse(parts[2], out regionID);

                        if (!hasScopeID && !hasRegionID && (int.TryParse(parts[1], out x) && int.TryParse(parts[2], out y)))
                        {
                            List<GridRegion> regions = regiondata.Get(x, y, UUID.Zero);
                            if (regions.Count == 1)
                            {
                                region = regions[0];
                            }
                            else
                            {
                                foreach (GridRegion _region in regions)
                                {
                                    if (_region.RegionLocZ == 0)
                                    {
                                        region = _region;
                                        break;
                                    }
                                }
                            }
                        }
                        else if (hasScopeID && hasRegionID)
                        {
                            region = regiondata.Get(regionID, scopeID);
                        }
                        else if (hasScopeID && !hasRegionID)
                        {
                            List<GridRegion> regions = regiondata.Get(parts[1], scopeID);
                            if (regions.Count > 0)
                            {
                                region = regions[0];
                            }
                        }
                    }
                    else if (parts.Length == 4 && (int.TryParse(parts[1], out x) && int.TryParse(parts[2], out y) && int.TryParse(parts[3], out z)))
                    {
                        List<GridRegion> regions = regiondata.Get(x, y, UUID.Zero);
                        foreach (GridRegion _region in regions)
                        {
                            if (_region.RegionLocZ == z)
                            {
                                region = _region;
                                break;
                            }
                        }
                    }
                }

                if (region == null)
                {
                    resp["Error"] = OSD.FromString("Region not found");
                }
                else
                {
                    OSDMap regionDetails = new OSDMap();

                    regionDetails["ScopeID"] = region.ScopeID;
                    regionDetails["RegionID"] = region.RegionID;
                    regionDetails["RegionName"] = region.RegionName;
                    
                    regionDetails["RegionLocX"] = region.RegionLocX;
                    regionDetails["RegionLocY"] = region.RegionLocY;
                    regionDetails["RegionLocZ"] = region.RegionLocZ;

                    regionDetails["RegionSizeX"] = region.RegionSizeX;
                    regionDetails["RegionSizeY"] = region.RegionSizeY;
                    regionDetails["RegionSizeZ"] = region.RegionSizeZ;

                    regionDetails["EstateOwnerID"] = region.EstateOwner;
                    regionDetails["EstateOwnerName"] = "Unknown User";

                    IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
                    if (accountService != null && region.EstateOwner != UUID.Zero)
                    {
                        UserAccount user = accountService.GetUserAccount(region.ScopeID, region.EstateOwner);
                        if (user != null)
                        {
                            regionDetails["EstateOwnerName"] = user.Name;
                        }
                    }

                    resp["Region"] = regionDetails;
                }
            }

            return resp;
        }

        #endregion
    }
}
