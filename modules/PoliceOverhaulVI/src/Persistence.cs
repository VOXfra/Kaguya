using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace VOX.PoliceOverhaulVI
{
    internal static class Persistence
    {
        public static void LoadCases(string path, CaseRepository repository, Action<string> log)
        {
            if (repository == null || !File.Exists(path)) return;
            try
            {
                var doc = new XmlDocument(); doc.Load(path);
                XmlNodeList nodes = doc.SelectNodes("/PoliceOverhaulVI/Cases/Case"); if (nodes == null) return;
                foreach (XmlNode node in nodes)
                {
                    int model = ReadInt(node, "SuspectModelHash", 0); if (model == 0) continue;
                    bool oldFaceKnown=ReadBool(node,"FaceKnown",false);
                    bool oldOutfitKnown=ReadBool(node,"OutfitKnown",false);
                    var c = new CaseMemory
                    {
                        SuspectModelHash = model,
                        Active = ReadBool(node, "Active", false),
                        FaceKnown = oldFaceKnown,
                        OutfitKnown = oldOutfitKnown,
                        FaceConfidence = ReadFloat(node,"FaceConfidence",oldFaceKnown?82f:0f),
                        OutfitConfidence = ReadFloat(node,"OutfitConfidence",oldOutfitKnown?58f:0f),
                        VehicleConfidence = ReadFloat(node,"VehicleConfidence",0f),
                        IdentityConfidence = ReadFloat(node,"IdentityConfidence",oldFaceKnown?82f:0f),
                        IdentityConfirmed = ReadBool(node,"IdentityConfirmed",oldFaceKnown),
                        Notoriety = ReadFloat(node,"Notoriety",0f),
                        MostWanted = ReadBool(node,"MostWanted",false),
                        MajorHeistsKnown = ReadInt(node,"MajorHeistsKnown",0),
                        SurrenderCount = ReadInt(node,"SurrenderCount",0),
                        ThreatLevel = ReadInt(node, "ThreatLevel", 0),
                        HeatPoints = ReadInt(node, "HeatPoints", 0),
                        LastWantedEndedAt = ReadInt(node,"LastWantedEndedAt",0),
                        ExpiresUtcTicks = ReadLong(node, "ExpiresUtcTicks", 0),
                        WarrantActive = ReadBool(node, "WarrantActive", false),
                        WarrantExpiresUtcTicks = ReadLong(node, "WarrantExpiresUtcTicks", 0),
                        LastKnownX = ReadFloat(node, "LastKnownX", 0f),
                        LastKnownY = ReadFloat(node, "LastKnownY", 0f),
                        LastKnownZ = ReadFloat(node, "LastKnownZ", 0f),
                        LastSource = (ObservationSource)ReadInt(node, "LastSource", 0),
                        LastObservedGameTime = ReadInt(node,"LastObservedGameTime",0),
                        WeaponKnown = ReadBool(node, "WeaponKnown", false),
                        WeaponHash = ReadInt(node, "WeaponHash", 0),
                        SuspectCountKnown = ReadBool(node, "SuspectCountKnown", false),
                        SuspectCount = ReadInt(node, "SuspectCount", 1),
                        UnpaidFines = ReadInt(node, "UnpaidFines", 0)
                    };
                    XmlNode outfit = node.SelectSingleNode("Outfit");
                    if (outfit != null) c.Outfit = new OutfitSignature { Drawables = ParseIntArray(ReadText(outfit, "Drawables", string.Empty), 12), Textures = ParseIntArray(ReadText(outfit, "Textures", string.Empty), 12) };
                    XmlNode vehicle = node.SelectSingleNode("Vehicle");
                    if (vehicle != null)
                    {
                        c.Vehicle = new VehicleSignature { ModelHash = ReadInt(vehicle, "ModelHash", 0), Plate = ReadText(vehicle, "Plate", string.Empty), PlateKnown = ReadBool(vehicle, "PlateKnown", false), PrimaryColor = ReadInt(vehicle, "PrimaryColor", -1), SecondaryColor = ReadInt(vehicle, "SecondaryColor", -1), TrackerPresent = ReadBool(vehicle, "TrackerPresent", false), TrackerKnownByPolice = ReadBool(vehicle, "TrackerKnownByPolice", false) };
                        if(c.VehicleConfidence<=0f)c.VehicleConfidence=c.Vehicle.PlateKnown?82f:45f;
                    }
                    repository.Put(c);
                }
                repository.ClearExpired(); if (log != null) log("Persistent police cases loaded (0.3 confidence migration compatible).");
            }
            catch (Exception ex) { if (log != null) log("Persistence load failed: " + ex.Message); }
        }

        public static void SaveCases(string path, CaseRepository repository, Action<string> log)
        {
            if (repository == null) return;
            try
            {
                string dir = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
                using (XmlWriter w = XmlWriter.Create(path, settings))
                {
                    w.WriteStartDocument(); w.WriteStartElement("PoliceOverhaulVI"); w.WriteAttributeString("version", "0.3.0"); w.WriteStartElement("Cases");
                    foreach (CaseMemory c in repository.Cases)
                    {
                        if (c == null || (!c.Active && !c.WarrantActive && c.UnpaidFines <= 0 && c.Notoriety<=0f)) continue;
                        w.WriteStartElement("Case");
                        Write(w,"SuspectModelHash",c.SuspectModelHash); Write(w,"Active",c.Active); Write(w,"FaceKnown",c.FaceKnown); Write(w,"OutfitKnown",c.OutfitKnown);
                        Write(w,"FaceConfidence",c.FaceConfidence);Write(w,"OutfitConfidence",c.OutfitConfidence);Write(w,"VehicleConfidence",c.VehicleConfidence);Write(w,"IdentityConfidence",c.IdentityConfidence);Write(w,"IdentityConfirmed",c.IdentityConfirmed);Write(w,"Notoriety",c.Notoriety);Write(w,"MostWanted",c.MostWanted);Write(w,"MajorHeistsKnown",c.MajorHeistsKnown);Write(w,"SurrenderCount",c.SurrenderCount);
                        Write(w,"ThreatLevel",c.ThreatLevel); Write(w,"HeatPoints",c.HeatPoints);Write(w,"LastWantedEndedAt",c.LastWantedEndedAt); Write(w,"ExpiresUtcTicks",c.ExpiresUtcTicks); Write(w,"WarrantActive",c.WarrantActive); Write(w,"WarrantExpiresUtcTicks",c.WarrantExpiresUtcTicks); Write(w,"LastKnownX",c.LastKnownX); Write(w,"LastKnownY",c.LastKnownY); Write(w,"LastKnownZ",c.LastKnownZ); Write(w,"LastSource",(int)c.LastSource);Write(w,"LastObservedGameTime",c.LastObservedGameTime); Write(w,"WeaponKnown",c.WeaponKnown); Write(w,"WeaponHash",c.WeaponHash); Write(w,"SuspectCountKnown",c.SuspectCountKnown); Write(w,"SuspectCount",c.SuspectCount); Write(w,"UnpaidFines",c.UnpaidFines);
                        if (c.Outfit != null) { w.WriteStartElement("Outfit"); Write(w,"Drawables",Join(c.Outfit.Drawables)); Write(w,"Textures",Join(c.Outfit.Textures)); w.WriteEndElement(); }
                        if (c.Vehicle != null) { w.WriteStartElement("Vehicle"); Write(w,"ModelHash",c.Vehicle.ModelHash); Write(w,"Plate",c.Vehicle.Plate ?? string.Empty); Write(w,"PlateKnown",c.Vehicle.PlateKnown); Write(w,"PrimaryColor",c.Vehicle.PrimaryColor); Write(w,"SecondaryColor",c.Vehicle.SecondaryColor); Write(w,"TrackerPresent",c.Vehicle.TrackerPresent); Write(w,"TrackerKnownByPolice",c.Vehicle.TrackerKnownByPolice); w.WriteEndElement(); }
                        w.WriteEndElement();
                    }
                    w.WriteEndElement(); w.WriteEndElement(); w.WriteEndDocument();
                }
            }
            catch (Exception ex) { if (log != null) log("Persistence save failed: " + ex.Message); }
        }

        private static void Write(XmlWriter w,string name,object value) { string s; if(value is float) s=((float)value).ToString(CultureInfo.InvariantCulture); else if(value is double) s=((double)value).ToString(CultureInfo.InvariantCulture); else if(value is bool) s=((bool)value)?"true":"false"; else s=Convert.ToString(value,CultureInfo.InvariantCulture)??string.Empty; w.WriteElementString(name,s); }
        private static string Join(int[] values) { return values==null?string.Empty:string.Join(",",values); }
        private static int[] ParseIntArray(string text,int length) { var result=new int[length]; if(string.IsNullOrWhiteSpace(text))return result; string[] parts=text.Split(','); for(int i=0;i<result.Length&&i<parts.Length;i++){int v;if(int.TryParse(parts[i],NumberStyles.Integer,CultureInfo.InvariantCulture,out v))result[i]=v;} return result; }
        private static string ReadText(XmlNode node,string name,string fallback){XmlNode child=node==null?null:node.SelectSingleNode(name);return child==null?fallback:child.InnerText;}
        private static int ReadInt(XmlNode node,string name,int fallback){int v;return int.TryParse(ReadText(node,name,string.Empty),NumberStyles.Integer,CultureInfo.InvariantCulture,out v)?v:fallback;}
        private static long ReadLong(XmlNode node,string name,long fallback){long v;return long.TryParse(ReadText(node,name,string.Empty),NumberStyles.Integer,CultureInfo.InvariantCulture,out v)?v:fallback;}
        private static float ReadFloat(XmlNode node,string name,float fallback){float v;return float.TryParse(ReadText(node,name,string.Empty),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;}
        private static bool ReadBool(XmlNode node,string name,bool fallback){bool v;return bool.TryParse(ReadText(node,name,string.Empty),out v)?v:fallback;}
    }
}
